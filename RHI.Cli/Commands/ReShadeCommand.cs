using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

internal static class ReShadeCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"reshade.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        return args.Verb switch
        {
            "install" => await InstallAsync(args, sp, ctx, command),
            "update" => await UpdateAsync(args, sp, ctx, command),
            "uninstall" => Uninstall(args, sp, ctx, command),
            _ => CliOutput.Fail(command, "UNKNOWN_VERB",
                $"Unknown reshade verb '{args.Verb}'. Try: install, update, uninstall.", ExitCodes.Usage),
        };
    }

    private static async Task<int> InstallAsync(Args args, IServiceProvider sp, GameContext ctx, string command)
    {
        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "Installing writes ReShade into the game folder. Re-run with --yes (or --dry-run to preview).",
                ExitCodes.Usage);

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

        if (ctx.IsVulkanOnly)
            return CliOutput.Fail(command, "VULKAN_UNSUPPORTED",
                "Vulkan ReShade uses the global Vulkan layer and is not wired into the CLI yet.", ExitCodes.Failed);

        CliState.EnsureInitialized(sp);

        var settings = sp.GetRequiredService<SettingsViewModel>();
        var names = sp.GetRequiredService<IGameNameService>();
        var aux = sp.GetRequiredService<IAuxInstallService>();
        var shaderPacks = sp.GetRequiredService<IShaderPackService>();

        var key = GameKey.From(ctx.Name, ctx.Store).ToKey();
        var useNormal = args.Has("no-addons") ||
                        string.Equals(args.Get("channel"), "no-addons", StringComparison.OrdinalIgnoreCase);

        var channel = ResolveChannel(args, names, settings, ctx, key, useNormal);
        var dll = NormalizeDll(args.Get("dll")) ?? ReShadeDllName.Resolve(ctx.AllApis);
        var packs = ResolvePacks(args, settings);

        var plan = new
        {
            game = ctx.Name,
            path = ctx.Path,
            channel = useNormal ? "(No Addons)" : channel,
            dll = dll ?? AuxInstallService.RsNormalName,
            shaderPacks = packs,
            useNormalReShade = useNormal,
            is32Bit = ctx.Is32Bit,
            store = ctx.Store,
        };

        if (args.DryRun)
        {
            CliOutput.Human(() =>
            {
                Console.WriteLine("Dry run - nothing downloaded or written.");
                Console.WriteLine($"  game:       {plan.game}");
                Console.WriteLine($"  path:       {plan.path}");
                Console.WriteLine($"  channel:    {plan.channel}");
                Console.WriteLine($"  dll:        {plan.dll}");
                Console.WriteLine($"  shaders:    {(packs.Count == 0 ? "(none)" : string.Join(", ", packs))}");
                Console.WriteLine($"  bitness:    {(ctx.Is32Bit ? 32 : 64)}-bit");
            });
            return CliOutput.Ok(command, new { dryRun = true, plan });
        }

        var progress = new CliProgress(!CliOutput.Json);

        try
        {
            if (packs.Count > 0)
                await shaderPacks.EnsurePacksAsync(packs);

            var stagingOk = true;
            if (useNormal)
            {
                var normal = sp.GetRequiredService<INormalReShadeUpdateService>();
                await normal.EnsureLatestAsync(progress);
                stagingOk = File.Exists(ctx.Is32Bit ? AuxInstallService.RsNormalStagedPath32 : AuxInstallService.RsNormalStagedPath64);
            }
            else if (IsDownloadableChannel(channel))
            {
                var updater = sp.GetRequiredService<IReShadeUpdateService>();
                await updater.EnsureLatestAsync(progress);
                stagingOk = File.Exists(ctx.Is32Bit ? AuxInstallService.RsStagedPath32 : AuxInstallService.RsStagedPath64);
            }

            if (!stagingOk && IsDownloadableChannel(channel))
                throw new FileNotFoundException("ReShade staging is incomplete: the expected DLL is not in the staging folder.");

            var record = await aux.InstallReShadeAsync(
                ctx.Name,
                ctx.Path,
                use32Bit: ctx.Is32Bit,
                filenameOverride: dll,
                selectedPackIds: packs,
                progress: progress,
                screenshotSavePath: ScreenshotPath(settings, ctx.Name),
                useNormalReShade: useNormal,
                overlayHotkey: settings.OverlayHotkey,
                screenshotHotkey: settings.ScreenshotHotkey,
                channel: useNormal ? null : channel,
                store: ctx.Store,
                mergeIni: true);

            var version = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.InstalledAs);

            CliOutput.Human(() =>
            {
                Console.WriteLine($"ReShade installed for '{ctx.Name}'");
                Console.WriteLine($"  file:     {Path.Combine(record.InstallPath, record.InstalledAs)}");
                Console.WriteLine($"  version:  {version ?? "unknown"}");
                Console.WriteLine($"  channel:  {record.Channel ?? "(No Addons)"}");
            });

            return CliOutput.Ok(command, new
            {
                installed = true,
                file = Path.Combine(record.InstallPath, record.InstalledAs),
                installedAs = record.InstalledAs,
                version,
                channel = record.Channel,
                shaderPacks = packs,
            });
        }
        catch (Exception ex)
        {
            return CliOutput.Fail(command, "INSTALL_FAILED", ex.Message, ExitCodes.Failed);
        }
    }

    private static async Task<int> UpdateAsync(Args args, IServiceProvider sp, GameContext ctx, string command)
    {
        var aux = sp.GetRequiredService<IAuxInstallService>();
        var record = FindReShadeRecord(aux, ctx);

        if (record == null)
            return CliOutput.Fail(command, "NOT_INSTALLED",
                $"ReShade is not recorded for '{ctx.Name}'. Use 'rhictl reshade install'.", ExitCodes.NotFound);

        var hasUpdate = false;
        try { hasUpdate = await aux.CheckForUpdateAsync(record); }
        catch (Exception ex) { CliOutput.Warn($"update check failed: {ex.Message}"); }

        if (!hasUpdate)
        {
            CliOutput.Human(() => Console.WriteLine($"ReShade for '{ctx.Name}' is up to date."));
            return CliOutput.Ok(command, new { updated = false, upToDate = true, installedAs = record.InstalledAs });
        }

        var forwardedRaw = new List<string>
        {
            "reshade", "install", "--path", ctx.Path, "--yes", "--dll", record.InstalledAs,
        };
        if (args.Has("force")) forwardedRaw.Add("--force");

        return await InstallAsync(Args.Parse(forwardedRaw.ToArray()), sp, ctx, command);
    }

    private static int Uninstall(Args args, IServiceProvider sp, GameContext ctx, string command)
    {
        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "Uninstalling removes ReShade files from the game folder. Re-run with --yes.", ExitCodes.Usage);

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

        var aux = sp.GetRequiredService<IAuxInstallService>();
        var record = FindReShadeRecord(aux, ctx);

        if (record == null)
            return CliOutput.Fail(command, "NOT_INSTALLED",
                $"ReShade is not recorded for '{ctx.Name}'.", ExitCodes.NotFound);

        if (args.DryRun)
        {
            CliOutput.Human(() => Console.WriteLine(
                $"Would remove {Path.Combine(record.InstallPath, record.InstalledAs)} and its record."));
            return CliOutput.Ok(command, new { dryRun = true, installedAs = record.InstalledAs });
        }

        aux.Uninstall(record);

        CliOutput.Human(() => Console.WriteLine($"ReShade removed from '{ctx.Name}'."));
        return CliOutput.Ok(command, new { removed = true, installedAs = record.InstalledAs });
    }

    private static AuxInstalledRecord? FindReShadeRecord(IAuxInstallService aux, GameContext ctx)
    {
        try
        {
            return aux.LoadAll().FirstOrDefault(r =>
                ctx.MatchesRecord(r.GameName, r.InstallPath, r.Store) &&
                (r.AddonType.Equals(AuxInstallService.TypeReShade, StringComparison.OrdinalIgnoreCase) ||
                 r.AddonType.Equals(AuxInstallService.TypeReShadeNormal, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeDll(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? value : value + ".dll";
    }
    private static string ResolveChannel(
        Args args,
        IGameNameService names,
        SettingsViewModel settings,
        GameContext ctx,
        string key,
        bool useNormal)
    {
        if (useNormal) return "";

        var requested = args.Get("channel");
        if (!string.IsNullOrWhiteSpace(requested))
            return NormalizeChannel(requested);

        if (names.ReShadeChannelOverrides.TryGetValue(key, out var perGame) && !string.IsNullOrWhiteSpace(perGame))
            return perGame;

        return string.IsNullOrWhiteSpace(settings.ReShadeChannel)
            ? AuxInstallService.ChannelStable
            : NormalizeChannel(settings.ReShadeChannel);
    }

    private static string NormalizeChannel(string value) => value.ToLowerInvariant() switch
    {
        "stable" => AuxInstallService.ChannelStable,
        "nightly" => AuxInstallService.ChannelNightly,
        "custom" => AuxInstallService.ChannelCustom,
        _ => value,
    };

    private static bool IsDownloadableChannel(string channel) =>
        channel.Equals(AuxInstallService.ChannelStable, StringComparison.OrdinalIgnoreCase) ||
        channel.Equals(AuxInstallService.ChannelNightly, StringComparison.OrdinalIgnoreCase);

    private static List<string> ResolvePacks(Args args, SettingsViewModel settings)
    {
        var explicitPacks = args.Get("packs");
        if (!string.IsNullOrWhiteSpace(explicitPacks))
            return explicitPacks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        var shaders = args.Get("shaders");
        if (string.Equals(shaders, "off", StringComparison.OrdinalIgnoreCase))
            return new List<string>();
        if (string.Equals(shaders, "custom", StringComparison.OrdinalIgnoreCase))
            return new List<string> { ShaderPackService.CustomShaderSentinel };

        if (settings.GlobalShadersOff) return new List<string>();
        if (settings.UseCustomShaders) return new List<string> { ShaderPackService.CustomShaderSentinel };
        return settings.SelectedShaderPacks.ToList();
    }

    private static string? ScreenshotPath(SettingsViewModel settings, string gameName)
    {
        var basePath = settings.ScreenshotPath;
        if (string.IsNullOrEmpty(basePath)) return null;
        if (!settings.PerGameScreenshotFolders) return basePath;

        var sanitized = AuxInstallService.SanitizeDirectoryName(gameName);
        return string.IsNullOrEmpty(sanitized) ? basePath : basePath + @"\" + sanitized;
    }
}

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

internal static class LumaCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"luma.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        var luma = sp.GetRequiredService<ILumaService>();

        switch (args.Verb)
        {
            case "list":
            {
                CliState.EnsureInitialized(sp);
                var text = new CliTextProgress();
                var mods = await luma.FetchCompletedModsAsync(text);

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"{mods.Count} Luma mod(s):");
                    foreach (var m in mods.OrderBy(m => m.Name)) Console.WriteLine($"  {m.Name}");
                });

                return CliOutput.Ok(command, new { count = mods.Count, mods = mods.Select(m => m.Name) });
            }

            case "install":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Installing Luma writes into the game folder. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                CliState.EnsureInitialized(sp);
                var settings = sp.GetRequiredService<SettingsViewModel>();
                var names = sp.GetRequiredService<IGameNameService>();
                var detection = sp.GetRequiredService<IGameDetectionService>();

                var text = new CliTextProgress();
                var mods = await luma.FetchCompletedModsAsync(text);
                var mod = Match(mods, ctx.Name, names, detection);

                if (mod == null)
                    return CliOutput.Fail(command, "NO_MOD",
                        $"No Luma mod found for '{ctx.Name}'.", ExitCodes.NotFound);

                var url = mod.DownloadUrl;

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing downloaded or written.");
                        Console.WriteLine($"  game:   {ctx.Name}");
                        Console.WriteLine($"  mod:    {mod.Name}");
                        Console.WriteLine($"  url:    {url ?? "(none)"}");
                    });
                    return CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, mod = mod.Name, url });
                }

                var progress = new CliProgress(!CliOutput.Json);
                var packs = settings.GlobalShadersOff
                    ? Array.Empty<string>()
                    : settings.SelectedShaderPacks.ToArray();

                var record = await luma.InstallAsync(
                    mod,
                    ctx.Path,
                    selectedShaderPacks: packs,
                    screenshotSavePath: ScreenshotPath(settings, ctx.Name),
                    overlayHotkey: settings.OverlayHotkey,
                    screenshotHotkey: settings.ScreenshotHotkey,
                    gameName: ctx.Name,
                    progress: progress,
                    store: ctx.Store);

                CliOutput.Human(() => Console.WriteLine(
                    $"Luma installed for '{ctx.Name}' (build {record.InstalledBuildNumber})."));

                return CliOutput.Ok(command, new
                {
                    installed = true,
                    mod = mod.Name,
                    build = record.InstalledBuildNumber,
                    files = record.InstalledFiles,
                });
            }

            case "uninstall":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Uninstalling removes Luma files. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                var record = LumaService.GetRecordByPath(ctx.Path);
                if (record == null)
                    return CliOutput.Fail(command, "NOT_INSTALLED",
                        $"Luma is not recorded for '{ctx.Name}'.", ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would remove {record.InstalledFiles.Count} file(s) recorded for '{ctx.Name}'."));
                    return CliOutput.Ok(command, new { dryRun = true, files = record.InstalledFiles.Count });
                }

                luma.Uninstall(record);
                CliOutput.Human(() => Console.WriteLine($"Luma removed from '{ctx.Name}'."));
                return CliOutput.Ok(command, new { removed = true });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown luma verb. Try: list, install, uninstall.", ExitCodes.Usage);
        }
    }

    private static LumaMod? Match(
        List<LumaMod> mods,
        string gameName,
        IGameNameService names,
        IGameDetectionService detection)
    {
        if (names.NameMappings.TryGetValue(gameName, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            var mappedNorm = Normalize(detection, mapped);
            var byMapping = mods.FirstOrDefault(m => Normalize(detection, m.Name) == mappedNorm);
            if (byMapping != null) return byMapping;
        }

        var norm = Normalize(detection, gameName);
        return mods.FirstOrDefault(m => Normalize(detection, m.Name) == norm);
    }

    private static string Normalize(IGameDetectionService detection, string value)
    {
        try
        {
            var normalized = detection.NormalizeName(value);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        }
        catch
        {
            // fall through
        }
        return value.Trim().ToLowerInvariant();
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

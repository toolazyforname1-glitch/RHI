using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class RenodxCommand
{
    private const string UeExtendedFallbackUrl =
        "https://marat569.github.io/renodx/renodx-ue-extended.addon64";

    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"renodx.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        var installer = sp.GetRequiredService<IModInstallService>();

        switch (args.Verb)
        {
            case "install":
            case "update":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Installing RenoDX writes an addon into the game folder. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                if (args.Verb == "update")
                {
                    var existing = installer.FindRecord(ctx.Name, ctx.Path);
                    if (existing == null)
                        return CliOutput.Fail(command, "NOT_INSTALLED",
                            $"RenoDX is not recorded for '{ctx.Name}'. Use 'rhictl renodx install'.", ExitCodes.NotFound);

                    var hasUpdate = await installer.CheckForUpdateAsync(existing);
                    if (!hasUpdate)
                    {
                        CliOutput.Human(() => Console.WriteLine($"RenoDX for '{ctx.Name}' is up to date."));
                        return CliOutput.Ok(command, new { updated = false, upToDate = true });
                    }
                }

                var detected = new DetectedGame { Name = ctx.Name, InstallPath = ctx.Path, Source = ctx.Store };
                var wiki = sp.GetRequiredService<IWikiService>();
                var detection = sp.GetRequiredService<IGameDetectionService>();
                var names = sp.GetRequiredService<IGameNameService>();

                CliState.EnsureInitialized(sp);

                var text = new CliTextProgress();
                var (wikiMods, _) = await wiki.FetchAllAsync(text);

                var mod = detection.MatchGame(detected, wikiMods, names.NameMappings);
                var matchedWiki = mod != null;

                if (mod == null || (mod.SnapshotUrl == null && mod.SnapshotUrl32 == null))
                {
                    mod = GenericFor(ctx, await ResolveUeExtendedUrlAsync(sp, names, ctx));
                    matchedWiki = false;
                }

                if (mod?.SnapshotUrl == null && mod?.SnapshotUrl32 == null)
                    return CliOutput.Fail(command, "NO_MOD",
                        ctx.Engine == EngineType.UnrealLegacy.ToString()
                            ? "Unreal Engine 3 and older cannot use the RenoDX addon system."
                            : $"No RenoDX mod or generic fallback is available for '{ctx.Name}' (engine={ctx.Engine}).",
                        ExitCodes.NotFound);

                var url = ctx.Is32Bit ? (mod.SnapshotUrl32 ?? mod.SnapshotUrl) : mod.SnapshotUrl;
                if (string.IsNullOrEmpty(url))
                    return CliOutput.Fail(command, "NO_MOD",
                        $"No {(ctx.Is32Bit ? "32-bit" : "64-bit")} build available for '{ctx.Name}'.", ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing downloaded or written.");
                        Console.WriteLine($"  game:     {ctx.Name}");
                        Console.WriteLine($"  mod:      {mod.Name} ({mod.Maintainer})");
                        Console.WriteLine($"  source:   {(matchedWiki ? "wiki match" : "generic engine fallback")}");
                        Console.WriteLine($"  url:      {url}");
                        Console.WriteLine($"  target:   {ModInstallService.GetAddonDeployPath(ctx.Path)}");
                    });
                    return CliOutput.Ok(command, new
                    {
                        dryRun = true,
                        game = ctx.Name,
                        mod = mod.Name,
                        maintainer = mod.Maintainer,
                        generic = !matchedWiki,
                        url,
                        target = ModInstallService.GetAddonDeployPath(ctx.Path),
                    });
                }

                var progress = new CliProgress(!CliOutput.Json);
                var record = await installer.InstallAsync(mod, ctx.Path, progress, ctx.Name, ctx.Store);
                var version = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName);

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"RenoDX installed for '{ctx.Name}'");
                    Console.WriteLine($"  mod:     {mod.Name} ({mod.Maintainer})");
                    Console.WriteLine($"  file:    {Path.Combine(record.InstallPath, record.AddonFileName)}");
                    Console.WriteLine($"  version: {version ?? "unknown"}");
                });

                return CliOutput.Ok(command, new
                {
                    installed = true,
                    mod = mod.Name,
                    maintainer = mod.Maintainer,
                    generic = !matchedWiki,
                    addon = record.AddonFileName,
                    path = record.InstallPath,
                    version,
                });
            }

            case "uninstall":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Uninstalling removes the RenoDX addon. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                var record = installer.FindRecord(ctx.Name, ctx.Path);
                if (record == null)
                    return CliOutput.Fail(command, "NOT_INSTALLED",
                        $"RenoDX is not recorded for '{ctx.Name}'.", ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would remove {Path.Combine(record.InstallPath, record.AddonFileName)} and its record."));
                    return CliOutput.Ok(command, new { dryRun = true, addon = record.AddonFileName });
                }

                installer.Uninstall(record);
                CliOutput.Human(() => Console.WriteLine($"RenoDX removed from '{ctx.Name}'."));
                return CliOutput.Ok(command, new { removed = true, addon = record.AddonFileName });
            }

            case "ue-extended":
            {
                var raw = (args.Get("value") ?? args.Positional.FirstOrDefault() ?? "").Trim().ToLowerInvariant();
                var enable = raw is "on" or "true" or "1" or "enable";
                var disable = raw is "off" or "false" or "0" or "disable";
                if (!enable && !disable)
                    return CliOutput.Fail(command, "BAD_VALUE",
                        "--value must be on or off.", ExitCodes.Usage);

                if (!string.Equals(ctx.Engine, "Unreal", StringComparison.OrdinalIgnoreCase))
                    return CliOutput.Fail(command, "NOT_ELIGIBLE",
                        $"UE-Extended applies to Unreal Engine games only (engine={ctx.Engine}).", ExitCodes.Usage);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the RenoDX source used for the game. Re-run with --yes (or --dry-run).", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var ueGuard))
                    return CliOutput.Fail(command, "BLOCKED", ueGuard, ExitCodes.GameRunning);

                CliState.EnsureInitialized(sp);
                var ueNames = sp.GetRequiredService<IGameNameService>();

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would turn UE-Extended {(enable ? "on" : "off")} for '{ctx.Name}'."));
                    return CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, ueExtended = enable });
                }

                if (enable)
                {
                    ueNames.UeExtendedGames.Add(ctx.Name);
                    ueNames.UeExtendedOptOutGames.Remove(ctx.Name);
                }
                else
                {
                    ueNames.UeExtendedGames.Remove(ctx.Name);
                    ueNames.UeExtendedOptOutGames.Add(ctx.Name);
                }
                CliState.Save(sp);

                CliOutput.Human(() => Console.WriteLine(
                    $"UE-Extended {(enable ? "enabled" : "disabled")} for '{ctx.Name}'. " +
                    (enable ? "Run 'rhictl renodx install' to deploy the extended addon." : "")));
                return CliOutput.Ok(command, new { game = ctx.Name, ueExtended = enable });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown renodx verb. Try: install, update, uninstall, ue-extended.", ExitCodes.Usage);
        }
    }

    private static async Task<string> ResolveUeExtendedUrlAsync(
        IServiceProvider sp, IGameNameService names, GameContext ctx)
    {
        if (!string.Equals(ctx.Engine, "Unreal", StringComparison.OrdinalIgnoreCase) ||
            !names.UeExtendedGames.Contains(ctx.Name))
            return WikiService.GenericUnrealUrl;

        try
        {
            var manifest = await sp.GetRequiredService<IManifestService>().FetchAsync();
            if (manifest?.ComponentUrls?.TryGetValue("ueExtended", out var url) == true &&
                !string.IsNullOrWhiteSpace(url))
                return url;
        }
        catch (Exception ex)
        {
            CliOutput.Detail($"ueExtended manifest lookup failed, using built-in URL: {ex.Message}");
        }

        return UeExtendedFallbackUrl;
    }

    private static GameMod? GenericFor(GameContext ctx, string unrealUrl) => ctx.Engine switch
    {
        "Unreal" => new GameMod
        {
            Name = "Generic Unreal Engine",
            Maintainer = "ShortFuse",
            SnapshotUrl = unrealUrl,
            Status = "?",
            IsGenericUnreal = true,
        },
        "Unity" => new GameMod
        {
            Name = "Generic Unity Engine",
            Maintainer = "Voosh",
            SnapshotUrl = WikiService.GenericUnityUrl64,
            SnapshotUrl32 = WikiService.GenericUnityUrl32,
            Status = "?",
            IsGenericUnity = true,
        },
        _ => null,
    };
}

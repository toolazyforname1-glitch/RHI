using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

/// <summary>
/// Implements `game reset-overrides`: clears the same per-game override collections the
/// GUI's "Reset Overrides" button clears, through the same IGameNameService objects.
/// Also replicates the button's side effects: ReShade is reinstalled on the global channel
/// when a channel override existed, and tracked DXVK installs are removed.
/// </summary>
internal static class GameOverridesReset
{
    public static Task<int> RunAsync(Args args, IServiceProvider sp, string command)
    {
        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return Task.FromResult(CliOutput.Fail(command, code, message,
                code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

        if (!args.Yes && !args.DryRun)
            return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "This clears every per-game override. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return Task.FromResult(CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning));

        CliState.EnsureInitialized(sp);
        var n = sp.GetRequiredService<IGameNameService>();
        var dll = sp.GetRequiredService<IDllOverrideService>();
        var previousChannel = GetValue(n.ReShadeChannelOverrides, ctx.Name);
        var clear = !args.DryRun;
        var report = new List<(string Label, int Count)>();

        // Name mapping goes through the service so the manifest-origin marker is cleared too.
        var hadMapping = n.GetUserNameMapping(ctx.Name) != null;
        if (clear) n.RemoveNameMapping(ctx.Name);
        report.Add(("name mapping", hadMapping ? 1 : 0));

        // Game renames are keyed by install path, not by name.
        var renameHits = n.GameRenames.Keys
            .Where(k => Normalize(k) == Normalize(ctx.Path))
            .ToList();
        if (clear) foreach (var k in renameHits) n.GameRenames.Remove(k);
        report.Add(("game rename", renameHits.Count));

        var targets = new (string Label, object Collection)[]
        {
            ("name/paths", n.NameMappings),
            ("folder override", n.FolderOverrides),
            ("bitness override", n.BitnessOverrides),
            ("api override", n.ApiOverrides),
            ("vulkan render path", n.VulkanRenderingPaths),
            ("engine version", n.EngineVersionOverrides),
            ("ReShade channel", n.ReShadeChannelOverrides),
            ("custom ReShade DLL", n.CustomReShadeSelection),
            ("normal ReShade", n.NormalReShadeGames),
            ("ReShade ASI", n.RsAsiGames),
            ("shader mode", n.PerGameShaderMode),
            ("shader selection", n.PerGameShaderSelection),
            ("addon mode", n.PerGameAddonMode),
            ("addon selection", n.PerGameAddonSelection),
            ("UE-Extended", n.UeExtendedGames),
            ("UE-Extended opt-out", n.UeExtendedOptOutGames),
            ("launch exe", n.LaunchExeOverrides),
            ("launch args", n.LaunchArgsOverrides),
            ("HDR toggle", n.HdrToggleOverrides),
            ("resolution toggle", n.ResToggleOverrides),
            ("DXVK variant", n.DxvkVariantOverrides),
            ("Lilium preset", n.LiliumPresetOverrides),
            ("OptiScaler variant", n.OsVariantOverrides),
            ("OptiScaler nightly", n.OsNightlyBuild),
            ("OptiScaler NR runtime", n.OsNrRuntime),
            ("NR method", n.NrMethodOverrides),
            ("NR addon version", n.NrAddonVersion),
            ("NR DLL version", n.NrDllVersion),
            ("NR pack version", n.NrPackVersion),
            ("FG input", n.OsFgInput),
            ("FG output", n.OsFgOutput),
            ("FG nvngx", n.OsFgNvngxReplacement),
            ("Streamline version", n.OsStreamlineVersion),
            ("FSR crash fix", n.OsFsrCrashFix),
            ("OS deploy Streamline", n.OsDeployStreamline),
            ("OS deploy DLSS Enabler", n.OsDeployDlssEnabler),
            ("OS dilated motion vectors", n.OsDilatedMotionVectorsOff),
            ("OS FSR-FG swapchain", n.OsFsrFgSwapchain),
            ("OS upscaler plugin", n.OsUpscalerPlugin),
            ("update-all: ReShade", n.UpdateAllExcludedReShade),
            ("update-all: RenoDX", n.UpdateAllExcludedRenoDx),
            ("update-all: ReLimiter", n.UpdateAllExcludedUl),
            ("update-all: Display Commander", n.UpdateAllExcludedDc),
            ("update-all: OptiScaler", n.UpdateAllExcludedOs),
            ("update-all: RE Framework", n.UpdateAllExcludedRef),
            ("wiki exclusion", n.WikiExclusions),
        };

        foreach (var (label, collection) in targets)
            report.Add((label, CountAndClear(collection, ctx.Name, clear)));

        var hadDll = dll.HasDllOverride(ctx.Name);
        if (hadDll && clear)
        {
            try
            {
                var card = new GameCardViewModel
                {
                    GameName = ctx.Name,
                    InstallPath = ctx.Path,
                    Source = ctx.Store,
                };
                dll.DisableDllOverride(card);
            }
            catch (Exception ex)
            {
                CliOutput.Warn($"DLL override could not be reverted on disk: {ex.Message}");
            }
        }
        report.Add(("DLL override", hadDll ? 1 : 0));

        if (clear) CliState.Save(sp);

        var reinstalled = false;
        if (clear && !string.IsNullOrWhiteSpace(previousChannel))
        {
            var aux = sp.GetRequiredService<IAuxInstallService>();
            var rs = aux.FindRecord(ctx.Name, ctx.Path, "ReShade")
                     ?? aux.FindRecord(ctx.Name, ctx.Path, "ReShadeNormal");
            if (rs != null)
            {
                reinstalled = ReShadeCommand.RunAsync(
                        Args.Parse(new[] { "reshade", "install", "--game", ctx.Name, "--yes" }), sp)
                    .GetAwaiter().GetResult() == 0;
                if (!reinstalled)
                    CliOutput.Warn("ReShade could not be reinstalled on the global channel.");
            }
        }

        if (clear)
        {
            var dxvk = sp.GetRequiredService<IDxvkService>();
            var dxvkCard = new GameCardViewModel
            {
                GameName = ctx.Name,
                InstallPath = ctx.Path,
                Source = ctx.Store,
            };
            dxvk.UninstallAsync(dxvkCard).GetAwaiter().GetResult();
        }

        var touched = report.Where(r => r.Count > 0).ToList();
        var total = touched.Sum(r => r.Count);

        CliOutput.Human(() =>
        {
            Console.WriteLine(args.DryRun
                ? $"Dry run - would clear {total} override(s) for '{ctx.Name}':"
                : $"Cleared {total} override(s) for '{ctx.Name}':");
            if (touched.Count == 0) Console.WriteLine("  (nothing was set)");
            foreach (var (label, count) in touched)
                Console.WriteLine($"  {label,-30} {count}");
            if (previousChannel != null)
                Console.WriteLine(reinstalled
                    ? "  ReShade reinstalled on the global channel."
                    : "  ReShade channel override cleared (no reinstall needed).");
            Console.WriteLine("  DXVK tracked install removed if one existed.");
        });

        return Task.FromResult(CliOutput.Ok(command, new
        {
            game = ctx.Name,
            dryRun = args.DryRun,
            total,
            reShadeReinstalled = reinstalled,
            groups = touched.Select(t => new { group = t.Label, count = t.Count }).ToList(),
        }));
    }

    private static int CountAndClear(object collection, string gameName, bool clear)
    {
        switch (collection)
        {
            case IDictionary dict:
            {
                var hits = dict.Keys.Cast<object>()
                    .Select(k => k.ToString() ?? "")
                    .Where(k => Matches(k, gameName))
                    .ToList();
                if (clear) foreach (var k in hits) dict.Remove(k);
                return hits.Count;
            }
            case ICollection<string> set:
            {
                var hits = set.Where(k => Matches(k, gameName)).ToList();
                if (clear) foreach (var k in hits) set.Remove(k);
                return hits.Count;
            }
            default:
                return 0;
        }
    }

    private static bool Matches(string key, string gameName) =>
        key.Equals(gameName, StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(gameName + "|", StringComparison.OrdinalIgnoreCase);

    private static string? GetValue(Dictionary<string, string> dict, string gameName)
    {
        foreach (var kv in dict)
            if (Matches(kv.Key, gameName)) return kv.Value;
        return null;
    }

    private static string Normalize(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
}

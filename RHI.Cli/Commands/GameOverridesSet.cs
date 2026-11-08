using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class GameOverridesSet
{
    public static Task<int> RunAsync(Args args, IServiceProvider sp, string command, bool clear)
    {
        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return Task.FromResult(CliOutput.Fail(command, code, message,
                code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

        var kind = (args.Get("kind") ?? "").Trim().ToLowerInvariant();
        if (kind.Length == 0)
            return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                "--kind <name> is required. Run 'rhictl help' for the list.", ExitCodes.Usage));

        var raw = args.Get("value");
        if (!clear && raw == null)
            return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                "--value <text> is required (or use 'game clear-override').", ExitCodes.Usage));

        if (!args.Yes && !args.DryRun)
            return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "This writes a per-game override. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return Task.FromResult(CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning));

        CliState.EnsureInitialized(sp);
        var n = sp.GetRequiredService<IGameNameService>();
        var key = GameKey.From(ctx.Name, ctx.Store).ToKey();
        var doClear = clear || IsAuto(raw);

        var outcome = Apply(kind, n, key, ctx.Name, raw, doClear, args.DryRun);
        if (outcome.Error != null)
            return Task.FromResult(CliOutput.Fail(command, outcome.ErrorValue!, outcome.Error, ExitCodes.Usage));

        if (!args.DryRun) CliState.Save(sp);

        CliOutput.Human(() => Console.WriteLine(doClear
            ? $"{kind}: cleared for '{ctx.Name}'."
            : $"{kind}: {outcome.Stored} for '{ctx.Name}'."));

        return Task.FromResult(CliOutput.Ok(command, new
        {
            game = ctx.Name,
            kind,
            key,
            value = doClear ? null : outcome.Stored,
            cleared = doClear,
        }));
    }

    private static (string? Error, string? ErrorValue, string? Stored) Apply(
        string kind, IGameNameService n, string key, string name, string? raw, bool clear, bool dryRun)
    {
        var stored = Canon(kind, raw, out var badValue);
        if (badValue != null) return ("BAD_VALUE", badValue, null);

        return kind switch
        {
            "bitness" => MapDict(n.BitnessOverrides, key),
            "channel" => MapDict(n.ReShadeChannelOverrides, key),
            "folder" => MapDict(n.FolderOverrides, key),
            "vulkan-path" => MapDict(n.VulkanRenderingPaths, key),
            "custom-reshade" => MapDict(n.CustomReShadeSelection, key),
            "shader-mode" => MapDict(n.PerGameShaderMode, key),
            "addon-mode" => MapDict(n.PerGameAddonMode, key),
            "dxvk-variant" => MapDict(n.DxvkVariantOverrides, key),
            "os-variant" => MapDict(n.OsVariantOverrides, key),
            "os-nightly" => MapDict(n.OsNightlyBuild, key),
            "nr-runtime" => MapDict(n.OsNrRuntime, key),
            "nr-method" => MapDict(n.NrMethodOverrides, key),
            "nr-addon" => MapDict(n.NrAddonVersion, key),
            "nr-dll" => MapDict(n.NrDllVersion, key),
            "nr-pack" => MapDict(n.NrPackVersion, key),
            "fg-input" => MapDict(n.OsFgInput, key),
            "fg-output" => MapDict(n.OsFgOutput, key),
            "fg-nvngx" => MapDict(n.OsFgNvngxReplacement, key),
            "streamline" => MapDict(n.OsStreamlineVersion, key),
            "fsr-fix" => MapDict(n.OsFsrCrashFix, key),
            "dgvoodoo-version" => MapDict(n.DgVoodooVersionOverride, key),
            "engine-version" => MapDict(n.EngineVersionOverrides, name),
            "hdr-toggle" => MapDict(n.HdrToggleOverrides, name),
            "res-toggle" => MapDict(n.ResToggleOverrides, name),
            "api" => MapList(n.ApiOverrides, key),
            "shader-selection" => MapList(n.PerGameShaderSelection, key),
            "addon-selection" => MapList(n.PerGameAddonSelection, key),
            "lilium-preset" => MapInt(n.LiliumPresetOverrides, key),
            "wiki-exclude" => MapSet(n.WikiExclusions, name),
            "normal-reshade" => MapSet(n.NormalReShadeGames, name),
            "rs-asi" => MapSet(n.RsAsiGames, name),
            "exclude-reshade" => MapSet(n.UpdateAllExcludedReShade, key),
            "exclude-renodx" => MapSet(n.UpdateAllExcludedRenoDx, key),
            "exclude-ul" => MapSet(n.UpdateAllExcludedUl, key),
            "exclude-dc" => MapSet(n.UpdateAllExcludedDc, key),
            "exclude-os" => MapSet(n.UpdateAllExcludedOs, key),
            "exclude-ref" => MapSet(n.UpdateAllExcludedRef, key),
            "os-deploy-streamline" => MapSet(n.OsDeployStreamline, key),
            "os-deploy-dlss-enabler" => MapSet(n.OsDeployDlssEnabler, key),
            "os-dilated-mv-off" => MapSet(n.OsDilatedMotionVectorsOff, key),
            "os-fsr-fg-swapchain" => MapSet(n.OsFsrFgSwapchain, key),
            "os-upscaler-plugin" => MapSet(n.OsUpscalerPlugin, key),
            "dgvoodoo-standalone" => MapSet(n.DgVoodooStandaloneGames, key),
            _ => ("BAD_KIND",
                "Unknown kind. Use: bitness, api, channel, folder, vulkan-path, custom-reshade, " +
                "shader-mode, shader-selection, addon-mode, addon-selection, dxvk-variant, lilium-preset, " +
                "os-variant, os-nightly, nr-runtime, nr-method, nr-addon, nr-dll, nr-pack, fg-input, " +
                "fg-output, fg-nvngx, streamline, fsr-fix, dgvoodoo-version, engine-version, hdr-toggle, " +
                "res-toggle, wiki-exclude, normal-reshade, rs-asi, exclude-reshade, exclude-renodx, " +
                "exclude-ul, exclude-dc, exclude-os, exclude-ref, os-deploy-streamline, " +
                "os-deploy-dlss-enabler, os-dilated-mv-off, os-fsr-fg-swapchain, os-upscaler-plugin, " +
                "dgvoodoo-standalone.", null),
        };

        (string?, string?, string?) MapDict(Dictionary<string, string> d, string k)
        {
            var existed = d.TryGetValue(k, out var old);
            if (dryRun) return (null, null, clear ? null : stored);
            if (clear) d.Remove(k); else d[k] = stored!;
            return (null, null, clear ? null : stored);
        }

        (string?, string?, string?) MapList(Dictionary<string, List<string>> d, string k)
        {
            if (dryRun) return (null, null, clear ? null : stored);
            if (clear) d.Remove(k);
            else d[k] = (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return (null, null, clear ? null : stored);
        }

        (string?, string?, string?) MapInt(Dictionary<string, int> d, string k)
        {
            if (!clear && !int.TryParse(stored, out _)) return ("BAD_VALUE", "--value must be an integer 0-5.", null);
            if (dryRun) return (null, null, stored);
            if (clear) d.Remove(k); else d[k] = int.Parse(stored!);
            return (null, null, clear ? null : stored);
        }

        (string?, string?, string?) MapSet(HashSet<string> s, string k)
        {
            var on = clear ? false : IsOn(stored);
            if (!clear && !IsOnOff(stored)) return ("BAD_VALUE", "--value must be on or off.", null);
            if (dryRun) return (null, null, clear ? null : (on ? "on" : "off"));
            if (on) s.Add(k); else s.Remove(k);
            return (null, null, on ? "on" : "off");
        }
    }

    private static string? Canon(string kind, string? raw, out string? badValue)
    {
        badValue = null;
        if (raw == null) return null;
        var v = raw.Trim();
        var lower = v.ToLowerInvariant();

        string? Pick(params (string Alias, string Value)[] map)
        {
            foreach (var (alias, value) in map)
                if (lower == alias) return value;
            return null;
        }

        var mapped = kind switch
        {
            "bitness" => Pick(("32", "32"), ("32-bit", "32"), ("x86", "32"), ("64", "64"), ("64-bit", "64"), ("x64", "64")),
            "channel" => Pick(("stable", "Stable"), ("nightly", "Nightly"), ("no-addons", "NoAddons"), ("noaddons", "NoAddons")),
            "shader-mode" or "addon-mode" => Pick(("global", "Global"), ("custom", "Custom")),
            "dxvk-variant" => Pick(("stable", "Stable"), ("development", "Development"), ("dev", "Development"), ("lilium", "LiliumHdr"), ("liliumhdr", "LiliumHdr")),
            "os-variant" => Pick(("stable", "Stable"), ("nightly", "Nightly"), ("dlssnr", "DlssNr")),
            "hdr-toggle" or "res-toggle" => Pick(("on", "On"), ("off", "Off")),
            "lilium-preset" => int.TryParse(v, out var p) && p is >= 0 and <= 5 ? p.ToString() : null,
            _ => v,
        };

        if (mapped == null || mapped.Length == 0) badValue = $"Invalid --value '{raw}' for kind '{kind}'.";
        return mapped;
    }

    private static bool IsAuto(string? raw) =>
        raw != null && raw.Trim().ToLowerInvariant() is "auto" or "default" or "none" or "clear";

    private static bool IsOn(string? raw) =>
        raw != null && raw.Trim().ToLowerInvariant() is "on" or "true" or "1" or "yes" or "enable";

    private static bool IsOnOff(string? raw) =>
        raw != null && raw.Trim().ToLowerInvariant() is "on" or "true" or "1" or "yes" or "enable"
            or "off" or "false" or "0" or "no" or "disable";
}

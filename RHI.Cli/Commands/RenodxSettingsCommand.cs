using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Cli.Commands;

internal static class RenodxSettingsCommand
{
    public static Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"renodx.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return Task.FromResult(CliOutput.Fail(command, code, message,
                code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

        var path = Path.Combine(ctx.Path, "reshade.ini");
        if (!File.Exists(path))
            return Task.FromResult(CliOutput.Fail(command, "NO_INI",
                $"No reshade.ini in '{ctx.Path}'. Install ReShade first.", ExitCodes.NotFound));

        if (!args.Yes && !args.DryRun)
            return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "This edits RenoDX settings in reshade.ini. Re-run with --yes.", ExitCodes.Usage));

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return Task.FromResult(CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning));

        return args.Verb switch
        {
            "mode" => SetMode(args, ctx, path, command),
            "peak-nits" => SetPeakNits(args, ctx, path, command),
            _ => Task.FromResult(CliOutput.Fail(command, "UNKNOWN_VERB",
                "Unknown renodx setting. Try: mode, peak-nits.", ExitCodes.Usage)),
        };
    }

    private static Task<int> SetMode(Args args, GameContext ctx, string path, string command)
    {
        var raw = (args.Get("value") ?? "").Trim().ToLowerInvariant();
        var mode = raw switch
        {
            "simple" or "0" => 0,
            "intermediate" or "1" => 1,
            "advanced" or "2" => 2,
            _ => -1,
        };

        if (mode < 0)
            return Task.FromResult(CliOutput.Fail(command, "BAD_VALUE",
                "--value must be simple, intermediate or advanced (or 0/1/2).", ExitCodes.Usage));

        if (args.DryRun)
        {
            CliOutput.Human(() => Console.WriteLine(
                $"Would set RenoDX SettingsMode to {mode} ({raw}) in {path}."));
            return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, mode, label = raw }));
        }

        if (!IniEditor.Set(path, "renodx", "SettingsMode", mode.ToString(), out var error))
            return Task.FromResult(CliOutput.Fail(command, "WRITE_FAILED", error ?? "unknown error", ExitCodes.Failed));

        CliOutput.Human(() => Console.WriteLine(
            $"RenoDX mode set to {raw} (SettingsMode={mode}) for '{ctx.Name}'."));

        return Task.FromResult(CliOutput.Ok(command, new { mode, label = raw, file = path }));
    }

    private static Task<int> SetPeakNits(Args args, GameContext ctx, string path, string command)
    {
        var value = args.GetInt("value");
        if (value == null || value <= 0)
            return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                "--value <nits> is required (e.g. 800).", ExitCodes.Usage));

        var sections = IniEditor.ListSections(path)
            .Where(s => s.StartsWith("renodx-preset", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sections.Count == 0) sections.Add("renodx-preset1");

        if (args.DryRun)
        {
            CliOutput.Human(() =>
            {
                Console.WriteLine($"Would set ToneMapPeakNits={value} in {sections.Count} preset section(s):");
                foreach (var s in sections) Console.WriteLine($"  [{s}]");
            });
            return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, value, sections }));
        }

        var changed = new List<string>();
        foreach (var section in sections)
        {
            if (IniEditor.Set(path, section, "ToneMapPeakNits", value.Value.ToString(), out var error))
                changed.Add(section);
            else
                CliOutput.Warn($"[{section}] write failed: {error}");
        }

        if (changed.Count == 0)
            return Task.FromResult(CliOutput.Fail(command, "WRITE_FAILED",
                "No preset section could be written.", ExitCodes.Failed));

        CliOutput.Human(() => Console.WriteLine(
            $"ToneMapPeakNits={value} applied to {changed.Count} preset(s) for '{ctx.Name}': {string.Join(", ", changed)}"));

        return Task.FromResult(CliOutput.Ok(command, new { value, sections = changed, file = path }));
    }
}

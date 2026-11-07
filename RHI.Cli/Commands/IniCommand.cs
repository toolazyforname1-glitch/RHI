using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

/// <summary>Per-game ini editing for the components that expose a config file.</summary>
internal static class IniCommand
{
    public static int Run(Args args, IServiceProvider sp)
    {
        var command = $"{args.Group}.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        var path = ResolveIni(args.Group, ctx, out var label);
        if (path == null)
            return CliOutput.Fail(command, "NO_INI",
                $"No {label} found for '{ctx.Name}'. Install the component first.", ExitCodes.NotFound);

        switch (args.Verb)
        {
            case "ini-list":
            {
                var rows = IniEditor.List(path, args.Get("section"));
                CliOutput.Human(() =>
                {
                    Console.WriteLine($"{label}: {path}");
                    CliOutput.Table(new[] { "SECTION", "KEY", "VALUE" },
                        rows.Select(r => new[] { r.Section, r.Key, r.Value }));
                });
                return CliOutput.Ok(command, new
                {
                    file = path,
                    entries = rows.Select(r => new { section = r.Section, key = r.Key, value = r.Value }),
                });
            }

            case "ini-get":
            {
                var section = args.Get("section");
                var key = args.Get("key");
                if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
                    return CliOutput.Fail(command, "MISSING_ARG",
                        "--section <name> and --key <name> are required.", ExitCodes.Usage);

                var value = IniEditor.Get(path, section, key);
                if (value == null)
                    return CliOutput.Fail(command, "KEY_NOT_FOUND",
                        $"[{section}] {key} is not present in {path}.", ExitCodes.NotFound);

                CliOutput.Human(() => Console.WriteLine(value));
                return CliOutput.Ok(command, new { file = path, section, key, value });
            }

            case "ini-set":
            {
                var section = args.Get("section");
                var key = args.Get("key");
                var value = args.Get("value");

                if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key) || value == null)
                    return CliOutput.Fail(command, "MISSING_ARG",
                        "--section <name>, --key <name> and --value <text> are required.", ExitCodes.Usage);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This edits a component config file. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                var previous = IniEditor.Get(path, section, key);

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing written.");
                        Console.WriteLine($"  file:     {path}");
                        Console.WriteLine($"  [{section}] {key}");
                        Console.WriteLine($"  current:  {previous ?? "(absent)"}");
                        Console.WriteLine($"  new:      {value}");
                    });
                    return CliOutput.Ok(command, new { dryRun = true, file = path, section, key, previous, value });
                }

                if (!IniEditor.Set(path, section, key, value, out var error))
                    return CliOutput.Fail(command, "WRITE_FAILED", error ?? "unknown error", ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine(
                    $"[{section}] {key}: {(previous ?? "(absent)")} -> {value}   ({path})"));

                return CliOutput.Ok(command, new { file = path, section, key, previous, value });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown ini verb. Try: ini-list, ini-get, ini-set.", ExitCodes.Usage);
        }
    }

    private static string? ResolveIni(string group, GameContext ctx, out string label)
    {
        var deployPath = ModInstallService.GetAddonDeployPath(ctx.Path);
        label = group;

        string? existing(params string[] candidates) =>
            candidates.FirstOrDefault(File.Exists);

        return group switch
        {
            "reshade" or "renodx" => existing(Path.Combine(ctx.Path, "reshade.ini")),
            "relimiter" => existing(
                Path.Combine(deployPath, "relimiter.ini"),
                Path.Combine(ctx.Path, "relimiter.ini")),
            "display-commander" => existing(
                Path.Combine(deployPath, "DisplayCommander.ini"),
                Path.Combine(ctx.Path, "DisplayCommander.ini")),
            "optiscaler" => existing(
                Path.Combine(ctx.Path, "OptiScaler.ini"),
                Path.Combine(ctx.Path, "OptiScaler", "OptiScaler.ini"),
                Path.Combine(ctx.Path, "plugins", "OptiScaler.ini")),
            _ => null,
        };
    }
}

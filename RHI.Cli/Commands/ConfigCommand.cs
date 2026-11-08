using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

/// <summary>
/// Implements the GUI's Settings pages from the terminal by reading and writing the
/// same settings.json through <see cref="SettingsFile"/>. Atomic read-modify-write,
/// so it can never clobber keys written by the GUI or by the services.
/// </summary>
internal static class ConfigCommand
{
    private static readonly string[] SensitiveMarkers =
    {
        "token", "secret", "password", "apikey", "api_key", "credential",
    };

    public static Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"config.{args.Verb}";
        return Task.FromResult(args.Verb switch
        {
            "list" => List(command, args),
            "get" => Get(command, args),
            "set" => Set(command, args, sp),
            "unset" => Unset(command, args, sp),
            _ => CliOutput.Fail(command, "UNKNOWN_VERB",
                $"Unknown config verb '{args.Verb}'. Try: list, get, set, unset.", ExitCodes.Usage),
        });
    }

    private static int List(string command, Args args)
    {
        var showSecrets = args.Has("show-secrets");
        var snapshot = SettingsFile.Snapshot();
        var entries = snapshot
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new
            {
                key = kv.Key,
                value = !showSecrets && IsSensitive(kv.Key) ? "***" : kv.Value,
            })
            .ToList();

        CliOutput.Human(() =>
        {
            Console.WriteLine($"settings: {SettingsFile.FilePath}");
            CliOutput.Table(
                new[] { "KEY", "VALUE" },
                entries.Select(e => new[] { e.key, e.value }));
            Console.WriteLine($"\n{entries.Count} key(s).");
        });

        return CliOutput.Ok(command, new { file = SettingsFile.FilePath, count = entries.Count, settings = entries });
    }

    private static int Get(string command, Args args)
    {
        var key = args.Get("key");
        if (string.IsNullOrWhiteSpace(key))
            return CliOutput.Fail(command, "MISSING_ARG", "--key <name> is required.", ExitCodes.Usage);

        var snapshot = SettingsFile.Snapshot();
        if (!TryGet(snapshot, key, out var actualKey, out var value))
            return CliOutput.Fail(command, "KEY_NOT_FOUND",
                $"Key '{key}' is not present in settings.json.", ExitCodes.NotFound);

        var showSecrets = args.Has("show-secrets");
        var shown = !showSecrets && IsSensitive(actualKey) ? "***" : value;

        CliOutput.Human(() => Console.WriteLine(shown));
        return CliOutput.Ok(command, new { key = actualKey, value = shown });
    }

    private static int Set(string command, Args args, IServiceProvider sp)
    {
        var key = args.Get("key");
        if (string.IsNullOrWhiteSpace(key))
            return CliOutput.Fail(command, "MISSING_ARG", "--key <name> is required.", ExitCodes.Usage);
        if (args.Get("value") is not { } value)
            return CliOutput.Fail(command, "MISSING_ARG", "--value <text> is required (use \"\" for empty).", ExitCodes.Usage);

        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "This rewrites settings.json. Re-run with --yes (or --dry-run).", ExitCodes.Usage);

        if (!args.Force && CliGuards.RhiIsRunning(out var guard))
            return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

        var snapshot = SettingsFile.Snapshot();
        var existed = TryGet(snapshot, key, out var actualKey, out var oldValue);

        if (args.DryRun)
        {
            CliOutput.Human(() => Console.WriteLine(
                $"Would set {actualKey ?? key}: {(existed ? oldValue : "(absent)")} -> {value}"));
            return CliOutput.Ok(command, new { dryRun = true, key = actualKey ?? key, oldValue = existed ? oldValue : null, value });
        }

        var target = actualKey ?? key;
        var written = false;
        try
        {
            written = SettingsFile.Update(d =>
            {
                // Preserve the existing casing when the key is already present.
                var existing = d.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                d[existing ?? key] = value;
            }, "rhictl config set");
        }
        catch (Exception ex)
        {
            return CliOutput.Fail(command, "WRITE_FAILED", ex.Message, ExitCodes.Failed);
        }

        if (!written)
            return CliOutput.Fail(command, "WRITE_FAILED",
                $"settings.json could not be updated (unreadable or locked): {SettingsFile.FilePath}", ExitCodes.Failed);

        CliOutput.Human(() => Console.WriteLine($"{target}: {(existed ? oldValue : "(absent)")} -> {value}"));
        return CliOutput.Ok(command, new { key = target, oldValue = existed ? oldValue : null, value });
    }

    private static int Unset(string command, Args args, IServiceProvider sp)
    {
        var key = args.Get("key");
        if (string.IsNullOrWhiteSpace(key))
            return CliOutput.Fail(command, "MISSING_ARG", "--key <name> is required.", ExitCodes.Usage);

        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "This rewrites settings.json. Re-run with --yes (or --dry-run).", ExitCodes.Usage);

        if (!args.Force && CliGuards.RhiIsRunning(out var guard))
            return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

        var snapshot = SettingsFile.Snapshot();
        if (!TryGet(snapshot, key, out var actualKey, out var oldValue))
            return CliOutput.Fail(command, "KEY_NOT_FOUND",
                $"Key '{key}' is not present in settings.json.", ExitCodes.NotFound);

        if (args.DryRun)
        {
            CliOutput.Human(() => Console.WriteLine($"Would remove {actualKey} (was {oldValue})."));
            return CliOutput.Ok(command, new { dryRun = true, key = actualKey, oldValue });
        }

        var removed = false;
        try
        {
            removed = SettingsFile.Update(d =>
            {
                var existing = d.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (existing != null) d.Remove(existing);
            }, "rhictl config unset");
        }
        catch (Exception ex)
        {
            return CliOutput.Fail(command, "WRITE_FAILED", ex.Message, ExitCodes.Failed);
        }

        if (!removed)
            return CliOutput.Fail(command, "WRITE_FAILED",
                $"settings.json could not be updated: {SettingsFile.FilePath}", ExitCodes.Failed);

        CliOutput.Human(() => Console.WriteLine($"Removed {actualKey} (was {oldValue})."));
        return CliOutput.Ok(command, new { removed = true, key = actualKey, oldValue });
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> dict, string key, out string actualKey, out string value)
    {
        foreach (var kv in dict)
        {
            if (!kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            actualKey = kv.Key;
            value = kv.Value;
            return true;
        }
        actualKey = key;
        value = "";
        return false;
    }

    private static bool IsSensitive(string key) =>
        SensitiveMarkers.Any(m => key.Contains(m, StringComparison.OrdinalIgnoreCase));
}

using System.Diagnostics;

namespace RenoDXCommander.Cli;

internal static class CliGuards
{
    public static bool GameIsRunning(GameContext ctx, out string message)
    {
        message = "";
        if (ctx.Exe == null) return false;

        var processName = Path.GetFileNameWithoutExtension(ctx.Exe);
        if (string.IsNullOrWhiteSpace(processName)) return false;

        try
        {
            var matches = Process.GetProcessesByName(processName);
            if (matches.Length == 0) return false;
            foreach (var p in matches) p.Dispose();
            message = $"'{processName}' is running. Close the game first (or use --force).";
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RhiIsRunning(out string message)
    {
        message = "";
        try
        {
            var matches = Process.GetProcessesByName("RHI");
            if (matches.Length == 0) return false;
            foreach (var p in matches) p.Dispose();
            message = "RHI.exe is running and shares settings.json with the CLI. Close it first (or use --force).";
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool CheckMutation(Args args, GameContext ctx, out string message)
    {
        message = "";
        if (args.Force) return false;

        if (RhiIsRunning(out message)) return true;
        if (GameIsRunning(ctx, out message)) return true;
        return false;
    }
}

internal sealed class CliTextProgress : IProgress<string>
{
    public void Report(string value)
    {
        if (CliOutput.Verbose) Console.Error.WriteLine(value);
    }
}

internal sealed class CliProgress : IProgress<(string message, double percent)>
{
    private readonly bool _enabled;
    private int _lastPercent = -100;

    public CliProgress(bool enabled) => _enabled = enabled;

    public void Report((string message, double percent) value)
    {
        if (!_enabled) return;

        var pct = (int)Math.Round(value.percent);
        if (pct < 100 && pct - _lastPercent < 5) return;
        _lastPercent = pct;

        Console.Error.Write($"\r[{pct,3}%] {value.message,-70}");
        if (pct >= 100) Console.Error.WriteLine();
    }
}

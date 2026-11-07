using System.Diagnostics;

namespace RenoDXCommander.Cli;

/// <summary>
/// Picks the game's real executable.
///
/// PeHeaderService.FindGameExe takes the largest .exe, which on Unity titles is
/// UnityCrashHandler64.exe. This scores by version metadata instead: a game exe
/// almost always has ProductName set, crash handlers and redistributables do not.
/// </summary>
internal static class ExeLocator
{
    private static readonly string[] NoisePrefixes =
    {
        "UnityCrashHandler", "UnityPlayer", "CrashReport", "unins", "vcredist",
        "dxsetup", "dotnet", "UE4PrereqSetup", "launcher_installer",
    };

    public static string? Find(string dir)
    {
        List<string> exes;
        try
        {
            exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            if (exes.Count == 0)
                exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories).Take(300).ToList();
        }
        catch
        {
            return null;
        }

        return exes
            .Where(e => !IsNoise(e))
            .Select(e => (path: e, score: Score(e)))
            .OrderByDescending(x => x.score)
            .Select(x => x.path)
            .FirstOrDefault();
    }

    public static string? DeriveName(string? exePath)
    {
        if (exePath == null) return null;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(vi.ProductName)) return vi.ProductName.Trim();
            if (!string.IsNullOrWhiteSpace(vi.FileDescription)) return vi.FileDescription.Trim();
        }
        catch
        {
            // fall through to filename
        }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    private static bool IsNoise(string exePath)
    {
        var file = Path.GetFileName(exePath);
        return NoisePrefixes.Any(p => file.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static int Score(string exePath)
    {
        var score = 0;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(vi.ProductName)) score += 1000;
            if (!string.IsNullOrWhiteSpace(vi.FileDescription)) score += 100;
            score += (int)Math.Min(new FileInfo(exePath).Length / 1024, 99);
        }
        catch
        {
            // version info is best-effort
        }
        return score;
    }
}

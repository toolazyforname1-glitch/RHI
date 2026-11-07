// Headless stand-in for the four pure static helpers on RenoDXCommander.DragDropHandler.
// Upstream moved game-root/name inference into that class, but the class itself is UI-bound
// (MainWindow, ContentDialog). RHI.Core compiles only these helpers, copied verbatim from
// DragDropHandler.Exe.cs. If upstream changes their behaviour, this file must be updated —
// and the build will NOT catch that, so keep it in sync manually.

using System.Text.RegularExpressions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public static class DragDropHandler
{
    public static bool LooksLikeGameRoot(string dirPath)
    {
        try
        {
            if (File.Exists(Path.Combine(dirPath, "steam_appid.txt"))
             || File.Exists(Path.Combine(dirPath, "steam_api64.dll"))
             || File.Exists(Path.Combine(dirPath, "steam_api.dll")))
                return true;

            if (File.Exists(Path.Combine(dirPath, "goglog.ini"))
             || File.Exists(Path.Combine(dirPath, "gog.ico"))
             || File.Exists(Path.Combine(dirPath, "goggame.sdb")))
                return true;
            if (Directory.GetFiles(dirPath, "goggame-*.dll").Length > 0)
                return true;

            if (Directory.Exists(Path.Combine(dirPath, ".egstore")))
                return true;

            if (File.Exists(Path.Combine(dirPath, "installerdata.xml"))
             || File.Exists(Path.Combine(dirPath, "__Installer")))
                return true;

            if (File.Exists(Path.Combine(dirPath, "MicrosoftGame.config"))
             || File.Exists(Path.Combine(dirPath, "appxmanifest.xml")))
                return true;

            if (File.Exists(Path.Combine(dirPath, "uplay_install.state"))
             || File.Exists(Path.Combine(dirPath, "upc.exe"))
             || Directory.GetFiles(dirPath, "uplay_*.dll").Length > 0)
                return true;

            if (File.Exists(Path.Combine(dirPath, ".build.info"))
             || File.Exists(Path.Combine(dirPath, ".product.db"))
             || File.Exists(Path.Combine(dirPath, "Blizzard Launcher.exe")))
                return true;

            if (File.Exists(Path.Combine(dirPath, "PlayGTAV.exe"))
             || File.Exists(Path.Combine(dirPath, "RockstarService.exe"))
             || Directory.GetFiles(dirPath, "socialclub*.dll").Length > 0)
                return true;

            if (File.Exists(Path.Combine(dirPath, "UnityPlayer.dll")))
                return true;

            if (Directory.Exists(Path.Combine(dirPath, "Binaries"))
             || Directory.Exists(Path.Combine(dirPath, "Engine")))
                return true;
        }
        catch (Exception ex) { CrashReporter.Log($"[DragDropHandler.LooksLikeGameRoot] Permission error checking '{dirPath}' - {ex.Message}"); }

        return false;
    }

    public static string InferGameName(string exePath, string gameRoot, EngineType engine)
    {
        var exeName = Path.GetFileNameWithoutExtension(exePath);
        var rootDirName = Path.GetFileName(gameRoot) ?? exeName;

        if (engine == EngineType.Unreal || engine == EngineType.UnrealLegacy)
        {
            var cleanExe = CleanUnrealExeName(exeName);

            if (rootDirName.Contains(' ') || rootDirName.Contains('-'))
                return CleanFolderName(rootDirName);

            try
            {
                var subdirs = Directory.GetDirectories(gameRoot)
                    .Select(Path.GetFileName)
                    .Where(d => d != null
                        && !d.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Engine", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Content", StringComparison.OrdinalIgnoreCase)
                        && !d.StartsWith(".", StringComparison.Ordinal))
                    .ToList();

                if (subdirs.Count > 0 && subdirs.Count <= 3)
                {
                    var candidate = subdirs.FirstOrDefault(d =>
                        !string.IsNullOrEmpty(d)
                        && !d.Equals("Saved", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Plugins", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Intermediate", StringComparison.OrdinalIgnoreCase));

                    if (candidate != null && candidate.Length > 2)
                        return CleanFolderName(candidate);
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DragDropHandler.InferGameName] Failed to enumerate subdirs in '{gameRoot}' - {ex.Message}"); }

            return !string.IsNullOrEmpty(cleanExe) ? cleanExe : CleanFolderName(rootDirName);
        }

        if (engine == EngineType.Unity)
            return CleanFolderName(exeName);

        return CleanFolderName(rootDirName);
    }

    public static string CleanUnrealExeName(string exeName)
    {
        var cleaned = Regex.Replace(exeName, @"[_-]?(Win64|WinGDK|Win32)[_-]?Shipping$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?Shipping$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?(Win64|WinGDK|Win32)[_-]?Test$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?(Win64|WinGDK|Win32)$", "", RegexOptions.IgnoreCase);
        return cleaned.Trim('-', '_', ' ');
    }

    public static string CleanFolderName(string name)
    {
        var cleaned = name.Replace('_', ' ').Replace('-', ' ');
        cleaned = Regex.Replace(cleaned, @"(?<=[a-z])(?=[A-Z])", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }
}

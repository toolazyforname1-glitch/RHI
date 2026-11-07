using System.Diagnostics;
using System.Security.Principal;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class DoctorCommand
{
    public static int Run(Args args)
    {
        const string command = "doctor";
        var checks = new List<Check>();

        checks.Add(new Check("os", "OS", Environment.OSVersion.ToString(), true));
        checks.Add(new Check("dotnet", ".NET runtime", Environment.Version.ToString(), true));
        checks.Add(new Check("arch", "Process arch", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), true));

        var sevenZip = AppPaths.Find7Zip();
        checks.Add(new Check("7zip", "7-Zip", sevenZip ?? "not found", sevenZip != null));

        var rhiExe = @"C:\Program Files\RHI\RHI.exe";
        if (File.Exists(rhiExe))
        {
            var vi = FileVersionInfo.GetVersionInfo(rhiExe);
            checks.Add(new Check("rhi", "RHI install", $"{rhiExe} ({vi.FileVersion})", true));
        }
        else
        {
            checks.Add(new Check("rhi", "RHI install", "not found at C:\\Program Files\\RHI", false));
        }

        var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");
        checks.Add(new Check("state", "State folder", stateDir, Directory.Exists(stateDir)));

        var settings = Path.Combine(stateDir, "settings.json");
        checks.Add(new Check("settings", "settings.json", ExistsLabel(settings), File.Exists(settings)));

        var library = Path.Combine(stateDir, "game_library.json");
        checks.Add(new Check("library", "game_library.json", ExistsLabel(library), File.Exists(library)));

        var logs = Path.Combine(stateDir, "Logs");
        checks.Add(new Check("logs", "Logs", logs, Directory.Exists(logs)));

        var isAdmin = IsAdmin();
        checks.Add(new Check("admin", "Elevated", isAdmin ? "yes" : "no", true));

        var nvidia = Directory.Exists(@"C:\Program Files\NVIDIA Corporation");
        checks.Add(new Check("nvidia", "NVIDIA driver", nvidia ? "present" : "not found", true));

        CliOutput.Human(() =>
        {
            Console.WriteLine($"rhictl {ThisVersion()} — app dir {AppPaths.ExeDir}");
            Console.WriteLine();
            CliOutput.Table(new[] { "CHECK", "STATUS", "DETAIL" },
                checks.Select(c => new[] { c.Name, c.Ok ? "ok" : "MISSING", c.Detail }));
        });

        return CliOutput.Ok(command, new
        {
            appDir = AppPaths.ExeDir,
            checks = checks.Select(c => new { c.Key, c.Name, c.Ok, c.Detail }),
        });
    }

    private static string ThisVersion() =>
        typeof(DoctorCommand).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private static string ExistsLabel(string path) =>
        File.Exists(path) ? path : $"{path} (missing)";

    private static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private sealed record Check(string Key, string Name, string Detail, bool Ok);
}

internal static class PathsCommand
{
    public static int Run(Args args)
    {
        const string command = "paths";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var state = Path.Combine(local, "RHI");

        var paths = new (string Key, string Path)[]
        {
            ("appDir", AppPaths.ExeDir),
            ("state", state),
            ("settings", Path.Combine(state, "settings.json")),
            ("library", Path.Combine(state, "game_library.json")),
            ("logs", Path.Combine(state, "Logs")),
            ("downloads", DownloadPaths.Root),
            ("downloads.renodx", DownloadPaths.RenoDX),
            ("downloads.shaders", DownloadPaths.Shaders),
            ("downloads.luma", DownloadPaths.Luma),
            ("downloads.misc", DownloadPaths.Misc),
            ("downloads.framelimiter", DownloadPaths.FrameLimiter),
        };

        CliOutput.Human(() => CliOutput.Table(new[] { "NAME", "PATH" },
            paths.Select(p => new[] { p.Key, p.Path })));

        return CliOutput.Ok(command, paths.ToDictionary(p => p.Key, p => p.Path));
    }
}

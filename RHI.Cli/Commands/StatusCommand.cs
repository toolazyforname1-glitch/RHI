using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class StatusCommand
{
    public static int Run(Args args, IServiceProvider sp)
    {
        const string command = "status";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        var aux = sp.GetRequiredService<IAuxInstallService>();
        var mods = sp.GetRequiredService<IModInstallService>();
        var dxvk = sp.GetRequiredService<IDxvkService>();
        var dlss = sp.GetRequiredService<IDlssStreamlineService>();
        var reff = sp.GetRequiredService<IREFrameworkService>();
        var dof = sp.GetRequiredService<DofFixService>();

        // ── Record stores (the same source of truth the GUI uses) ────────────
        var auxRecords = SafeLoad(() => aux.LoadAll())
            .Where(r => ctx.MatchesRecord(r.GameName, r.InstallPath, r.Store))
            .ToList();

        var reshadeRec = auxRecords.FirstOrDefault(r => r.AddonType.Equals("ReShade", StringComparison.OrdinalIgnoreCase));
        var osRec = auxRecords.FirstOrDefault(r => r.AddonType.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase));
        var dcRec = auxRecords.FirstOrDefault(r => r.AddonType.Equals("DisplayCommander", StringComparison.OrdinalIgnoreCase));
        var ulRec = auxRecords.FirstOrDefault(r => r.AddonType.Equals("ReLimiter", StringComparison.OrdinalIgnoreCase));

        var renodxRec = SafeLoad(() => mods.LoadAll())
            .FirstOrDefault(r => ctx.MatchesRecord(r.GameName, r.InstallPath, r.Store));

        var dxvkRec = SafeLoad(() => dxvk.LoadAllRecords())
            .FirstOrDefault(r => ctx.MatchesRecord(r.GameName, r.InstallPath, r.Store));

        var refRec = SafeLoad(() => reff.GetRecords())
            .FirstOrDefault(r => ctx.MatchesRecord(r.GameName, r.InstallPath, r.Store));

        var lumaRec = LumaService.GetRecordByPath(ctx.Path);

        // ── File-level checks ────────────────────────────────────────────────
        var reshadeIni = Path.Combine(ctx.Path, "reshade.ini");
        var shadersDir = Path.Combine(ctx.Path, "reshade-shaders");
        var shaderCount = CountFiles(Path.Combine(shadersDir, "Shaders"), "*.fx");
        var addonCount = CountFiles(Path.Combine(shadersDir, "Addons"), "*.addon64")
                       + CountFiles(Path.Combine(shadersDir, "Addons"), "*.addon32")
                       + CountFiles(ctx.Path, "*.addon64")
                       + CountFiles(ctx.Path, "*.addon32");

        var reshadeDll = FindFirstExisting(ctx.Path, "dxgi.dll", "d3d9.dll", "d3d8.dll", "opengl32.dll", "ReShade64.dll", "ReShade32.dll");
        var reshadeVersion = reshadeRec != null
            ? SafeVersion(reshadeRec.InstallPath, reshadeRec.InstalledAs)
            : null;

        var reshadeInstalled = reshadeRec != null || File.Exists(reshadeIni) || shaderCount > 0;
        var renodxInstalled = renodxRec != null
            && File.Exists(Path.Combine(renodxRec.InstallPath, renodxRec.AddonFileName));
        var renodxVersion = renodxRec != null ? SafeVersion(renodxRec.InstallPath, renodxRec.AddonFileName) : null;

        var dxvkPath = Safe(() => dxvk.DetectInstallation(ctx.Path, ctx.Api), null);
        var dlssResult = Safe(() => dlss.Detect(ctx.Path), null);
        var dofInstalled = Safe(() => dof.IsInstalledIn(ctx.Path), false);

        var deployPath = ModInstallService.GetAddonDeployPath(ctx.Path);
        var ulFile = FindFirstExisting(deployPath,
            "relimiter.addon64", "relimiter.addon32",
            "ultra_limiter.addon64", "ultra_limiter.addon32");
        var dcFile = FindFirstExisting(deployPath,
            "zzz_display_commander.addon64", "zzz_display_commander.addon32",
            "zzz_display_commander_lite.addon64", "zzz_display_commander_lite.addon32");

        var ulVersion = ulFile != null
            ? CliDownloads.ReadMetaVersion(Path.Combine(DownloadPaths.FrameLimiter, "ul_meta.json"), ctx.Is32Bit)
            : null;
        var dcVersion = dcFile != null
            ? SafeVersion(deployPath, dcFile)
              ?? CliDownloads.ReadMetaVersion(Path.Combine(DownloadPaths.FrameLimiter, "dc_meta.json"), ctx.Is32Bit)
            : null;

        var components = new List<ComponentStatus>
        {
            new("reshade", "ReShade", reshadeInstalled,
                reshadeVersion ?? (reshadeInstalled ? "(untracked)" : null),
                reshadeRec?.InstalledAs ?? reshadeDll),
            new("renodx", "RenoDX", renodxInstalled,
                renodxVersion,
                renodxRec?.AddonFileName),
            new("luma", "Luma", lumaRec != null, lumaRec?.InstalledBuildNumber.ToString(), null),
            new("optiscaler", "OptiScaler", osRec != null, osRec?.OsVariant, osRec?.InstalledAs),
            new("dxvk", "DXVK", dxvkRec != null || dxvkPath != null,
                dxvkRec?.DxvkVersion ?? dxvkPath, null),
            new("reframework", "RE Framework", refRec != null, refRec?.InstalledVersion, null),
            new("dofix", "DOF Fix", dofInstalled, null, null),
            new("displaycommander", "Display Commander", dcRec != null || dcFile != null, dcVersion, dcRec?.InstalledAs ?? dcFile),
            new("relimiter", "ReLimiter", ulRec != null || ulFile != null, ulVersion, ulRec?.InstalledAs ?? ulFile),
        };

        CliOutput.Human(() =>
        {
            Console.WriteLine($"{ctx.Name}  [{ctx.Store}]");
            Console.WriteLine($"  path:    {ctx.Path}");
            Console.WriteLine($"  exe:     {ctx.Exe ?? "(not found)"}");
            Console.WriteLine($"  engine:  {ctx.Engine}   api: {ctx.Api}   {(ctx.Is32Bit ? "32-bit" : "64-bit")}");
            Console.WriteLine();
            CliOutput.Table(new[] { "COMPONENT", "STATE", "VERSION", "FILE" },
                components.Select(c => new[]
                {
                    c.Label,
                    c.Installed ? "installed" : "not installed",
                    c.Version ?? "",
                    c.File ?? "",
                }));
            Console.WriteLine();
            Console.WriteLine($"  reshade.ini: {(File.Exists(reshadeIni) ? "yes" : "no")}   " +
                              $"shaders: {shaderCount}   addons: {addonCount}");
            if (dlssResult != null)
            {
                Console.WriteLine($"  DLSS:      sr {dlssResult.DlssVersion ?? "-"}  " +
                                  $"rr {dlssResult.DlssdVersion ?? "-"}  " +
                                  $"fg {dlssResult.DlssgVersion ?? "-"}  " +
                                  $"nr {dlssResult.DlssnrVersion ?? "-"}  " +
                                  $"streamline {dlssResult.StreamlineVersion ?? "-"}");
            }
        });

        return CliOutput.Ok(command, new
        {
            game = new
            {
                name = ctx.Name,
                store = ctx.Store,
                path = ctx.Path,
                exe = ctx.Exe,
                engine = ctx.Engine,
                api = ctx.Api.ToString(),
                is32Bit = ctx.Is32Bit,
            },
            components = components.Select(c => new
            {
                key = c.Key,
                label = c.Label,
                installed = c.Installed,
                version = c.Version,
                file = c.File,
            }),
            reshadeIni = File.Exists(reshadeIni),
            shaderCount,
            addonCount,
            dlss = dlssResult == null ? null : new
            {
                sr = dlssResult.DlssVersion,
                rr = dlssResult.DlssdVersion,
                fg = dlssResult.DlssgVersion,
                nr = dlssResult.DlssnrVersion,
                streamline = dlssResult.StreamlineVersion,
            },
        });
    }

    private static T? Safe<T>(Func<T> action, T? fallback)
    {
        try { return action(); }
        catch (Exception ex) { CliOutput.Detail($"probe failed: {ex.Message}"); return fallback; }
    }

    private static List<T> SafeLoad<T>(Func<List<T>> action)
    {
        try { return action(); }
        catch (Exception ex) { CliOutput.Detail($"record load failed: {ex.Message}"); return new List<T>(); }
    }

    private static string? SafeVersion(string dir, string file)
    {
        try { return AuxInstallService.ReadInstalledVersion(dir, file); }
        catch { return null; }
    }

    private static int CountFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).Length : 0; }
        catch { return 0; }
    }

    private static string? FindFirstExisting(string dir, params string[] names)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(dir, n);
            if (File.Exists(p)) return n;
        }
        return null;
    }

    private sealed record ComponentStatus(string Key, string Label, bool Installed, string? Version, string? File);
}

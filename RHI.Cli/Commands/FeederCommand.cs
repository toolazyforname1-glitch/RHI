using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class FeederCommand
{
    private const string FeederDeployFile64 = "dlss5-feed.addon64";
    private const string FeederDeployFile32 = "dlss5-feed.addon32";
    private const string FeederComponent = "Feeder";
    private const string ToolDeployFile = "renodx-dlss5.addon64";

    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"feeder.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message,
                code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        var rdx5 = sp.GetRequiredService<Renodx5AddonService>();
        var dlss = sp.GetRequiredService<IDlssStreamlineService>();
        var shader = sp.GetRequiredService<IShaderPackService>();

        switch (args.Verb)
        {
            case "status":
            {
                var feederName = ctx.Is32Bit ? FeederDeployFile32 : FeederDeployFile64;
                var toolPath = Path.Combine(ModInstallService.GetAddonDeployPath(ctx.Path), ToolDeployFile);
                var installed = File.Exists(Path.Combine(ctx.Path, feederName));

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"{ctx.Name} — DLSS5 Feeder");
                    Console.WriteLine($"  feeder:   {(installed ? "installed" : "not installed")}  {Path.Combine(ctx.Path, feederName)}");
                    Console.WriteLine($"  tool:     {(File.Exists(toolPath) ? "present" : "absent")}  {toolPath}");
                    Console.WriteLine($"  nr dll:   {(File.Exists(Path.Combine(ctx.Path, "nvngx_dlssnr.dll")) ? "present" : "absent")}");
                    Console.WriteLine($"  sr dll:   {(File.Exists(Path.Combine(ctx.Path, "nvngx_dlss.dll")) ? "present" : "absent")}");
                });

                return CliOutput.Ok(command, new
                {
                    game = ctx.Name,
                    installed,
                    feeder = Path.Combine(ctx.Path, feederName),
                    tool = File.Exists(toolPath),
                    nrDll = File.Exists(Path.Combine(ctx.Path, "nvngx_dlssnr.dll")),
                    srDll = File.Exists(Path.Combine(ctx.Path, "nvngx_dlss.dll")),
                });
            }

            case "install":
            {
                if (ctx.Is32Bit)
                    return CliOutput.Fail(command, "NOT_SUPPORTED",
                        "The 32-bit Feeder flow needs the host64 bootstrap; use the GUI for 32-bit games.", ExitCodes.Usage);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This deploys the Feeder addon, DLSS5 Tool, NR/SR DLLs and shaders. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                CliState.EnsureInitialized(sp);

                var latest = rdx5.GetLatestAvailableVersion(Renodx5AddonService.FeederSubDir);
                if (string.IsNullOrWhiteSpace(latest))
                    return CliOutput.Fail(command, "NO_VERSION",
                        "No Feeder release is cached. Run the GUI once or retry when the version list refreshes.",
                        ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine($"Would install Feeder {latest} for '{ctx.Name}':");
                        Console.WriteLine($"  {FeederDeployFile64}");
                        Console.WriteLine($"  {ToolDeployFile} + nvngx_dlssnr.dll");
                        Console.WriteLine("  nvngx_dlss.dll (newest cached)");
                        Console.WriteLine(@"  reshade-shaders\Shaders\DLSS5_Feed.fx + lumenite_Kernel.fx");
                    });
                    return CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, version = latest });
                }

                if (!await rdx5.EnsureVersionStagedAsync(Renodx5AddonService.FeederSubDir, latest))
                    return CliOutput.Fail(command, "DOWNLOAD_FAILED",
                        $"Feeder {latest} could not be downloaded.", ExitCodes.Failed);

                var source = rdx5.GetVersionedStagedFilePath(Renodx5AddonService.FeederSubDir, latest);
                if (!File.Exists(source))
                    return CliOutput.Fail(command, "DOWNLOAD_FAILED",
                        $"Feeder archive missing after staging: {source}", ExitCodes.Failed);

                File.Copy(source, Path.Combine(ctx.Path, FeederDeployFile64), overwrite: true);

                await rdx5.InstallAsync(ctx.Path);

                var sr = await dlss.EnsureNewestDlssCachedAsync();
                if (sr != null && new FileInfo(sr).Length > 0)
                {
                    var srDest = Path.Combine(ctx.Path, "nvngx_dlss.dll");
                    var srSentinel = srDest + ".original";
                    if (File.Exists(srDest) && !File.Exists(srSentinel)) File.Copy(srDest, srSentinel);
                    else if (!File.Exists(srDest) && !File.Exists(srSentinel)) File.WriteAllBytes(srSentinel, Array.Empty<byte>());
                    File.Copy(sr, srDest, overwrite: true);
                }

                await shader.EnsurePacksAsync(new[] { "LumeniteFX" });
                EnsureFeedShader(shader);

                var lumenite = shader.GetPackShaderFiles(new[] { "LumeniteFX" })
                    .Where(f => !f.Equals("lumenite_Kernel.fx", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var feeder = shader.GetPackShaderFiles(new[] { "DLSS5Feeder" })
                    .Where(f => !f.Equals("DLSS5_Feed.fx", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var exclusions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LumeniteFX"] = lumenite,
                    ["DLSS5Feeder"] = feeder,
                };
                shader.DeployToGameFolder(ctx.Path, new[] { "LumeniteFX", "DLSS5Feeder" }, exclusions);

                var deployed = new List<string>
                {
                    FeederDeployFile64,
                    ToolDeployFile,
                    "nvngx_dlssnr.dll",
                    "nvngx_dlss.dll",
                    @"reshade-shaders\Shaders\lumenite_Kernel.fx",
                    @"reshade-shaders\Shaders\DLSS5_Feed.fx",
                };
                RhiInstallManifest.SetComponent(ctx.Path, FeederComponent, deployed);

                CliOutput.Human(() => Console.WriteLine($"Feeder {latest} installed for '{ctx.Name}'."));
                return CliOutput.Ok(command, new { installed = true, game = ctx.Name, version = latest, files = deployed });
            }

            case "uninstall":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This removes the Feeder and its DLSS5 Tool. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                CliState.EnsureInitialized(sp);

                var files = RhiInstallManifest.GetComponentFiles(ctx.Path, FeederComponent).ToList();
                if (files.Count == 0)
                {
                    files.AddRange(new[]
                    {
                        FeederDeployFile64,
                        ToolDeployFile,
                        "nvngx_dlssnr.dll",
                        "nvngx_dlss.dll",
                        @"reshade-shaders\Shaders\lumenite_Kernel.fx",
                        @"reshade-shaders\Shaders\DLSS5_Feed.fx",
                    });
                }

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine($"Would remove {files.Count} Feeder file(s) from '{ctx.Name}':");
                        foreach (var f in files) Console.WriteLine($"  {f}");
                    });
                    return CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, files });
                }

                rdx5.Uninstall(ctx.Path);

                foreach (var relative in files)
                {
                    var full = Path.Combine(ctx.Path, relative);
                    var name = Path.GetFileName(relative);

                    if (!File.Exists(full))
                    {
                        var rsRoot = Path.Combine(ctx.Path, "reshade-shaders");
                        if (Directory.Exists(rsRoot))
                        {
                            try
                            {
                                full = Directory.EnumerateFiles(rsRoot, name, SearchOption.AllDirectories).FirstOrDefault() ?? full;
                            }
                            catch { }
                        }
                    }

                    if (name.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("nvngx_dlssnr.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        RestoreSentinel(full);
                    }
                    else
                    {
                        TryDelete(full);
                    }
                }

                CleanupFeederShaders(ctx.Path);
                RhiInstallManifest.RemoveComponent(ctx.Path, FeederComponent);

                CliOutput.Human(() => Console.WriteLine($"Feeder removed from '{ctx.Name}'."));
                return CliOutput.Ok(command, new { removed = true, game = ctx.Name, files });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown feeder verb. Try: status, install, uninstall.", ExitCodes.Usage);
        }
    }

    private static void EnsureFeedShader(IShaderPackService shader)
    {
        var dest = Path.Combine(ShaderPackService.ShadersDir, "DLSS5Feeder", "DLSS5_Feed.fx");
        if (File.Exists(dest)) return;

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "rdx5", "feeder"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "addons"),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var zipPath in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
                    var entry = zip.Entries.FirstOrDefault(e =>
                        e.Name.Equals("DLSS5_Feed.fx", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var src = entry.Open();
                    using var dst = File.Create(dest);
                    src.CopyTo(dst);
                    shader.RecordExtractedFilesFromDir("DLSS5Feeder");
                    return;
                }
                catch (Exception ex)
                {
                    CliOutput.Warn($"could not read {Path.GetFileName(zipPath)}: {ex.Message}");
                }
            }
        }

        CliOutput.Warn("DLSS5_Feed.fx not found in any staged Feeder archive.");
    }

    private static void CleanupFeederShaders(string gamePath)
    {
        var rs = Path.Combine(gamePath, "reshade-shaders");
        if (!Directory.Exists(rs)) return;

        foreach (var sub in new[]
                 {
                     Path.Combine("Shaders", "DLSS5Feeder"),
                     Path.Combine("Shaders", "LumeniteFX"),
                     Path.Combine("Textures", "LumeniteFX"),
                 })
            TryDeleteDirectory(Path.Combine(rs, sub));

        var shaders = Path.Combine(rs, "Shaders");
        if (Directory.Exists(shaders))
        {
            var remaining = Directory.EnumerateFiles(shaders, "*", SearchOption.AllDirectories)
                .Any(f => !Path.GetFileName(f).Equals("ReShade.fxh", StringComparison.OrdinalIgnoreCase)
                          && !Path.GetFileName(f).Equals("ReShadeUI.fxh", StringComparison.OrdinalIgnoreCase));

            if (!remaining)
            {
                TryDelete(Path.Combine(shaders, "ReShade.fxh"));
                TryDelete(Path.Combine(shaders, "ReShadeUI.fxh"));
            }
        }

        TryDelete(Path.Combine(rs, "Managed by RDXC.txt"));
        TryDeleteDirectoryIfEmpty(Path.Combine(rs, "Shaders"));
        TryDeleteDirectoryIfEmpty(Path.Combine(rs, "Textures"));
        TryDeleteDirectoryIfEmpty(rs);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { CliOutput.Warn($"could not delete {path}: {ex.Message}"); }
    }

    private static void TryDeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (Exception ex) { CliOutput.Warn($"could not remove {path}: {ex.Message}"); }
    }

    private static void RestoreSentinel(string file)
    {
        var sentinel = file + ".original";
        if (!File.Exists(sentinel)) return;

        if (new FileInfo(sentinel).Length == 0) TryDelete(file);
        else
        {
            try { File.Copy(sentinel, file, overwrite: true); }
            catch (Exception ex) { CliOutput.Warn($"could not restore {Path.GetFileName(file)}: {ex.Message}"); }
        }
        TryDelete(sentinel);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { CliOutput.Warn($"could not delete {path}: {ex.Message}"); }
    }
}

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

internal static class LimitersCommand
{
    private const string UlFile64 = "relimiter.addon64";
    private const string UlFile32 = "relimiter.addon32";
    private const string LegacyUlFile64 = "ultra_limiter.addon64";
    private const string LegacyUlFile32 = "ultra_limiter.addon32";
    private const string UlApi = "https://api.github.com/repos/RankFTW/ReLimiter/releases/latest";

    private const string DcFile64 = "zzz_display_commander.addon64";
    private const string DcFile32 = "zzz_display_commander.addon32";
    private const string LegacyDcFile64 = "zzz_display_commander_lite.addon64";
    private const string LegacyDcFile32 = "zzz_display_commander_lite.addon32";
    private const string DcApi = "https://api.github.com/repos/pmnoxx/display-commander/releases/tags/latest";

    private static string UlMetaPath => Path.Combine(DownloadPaths.FrameLimiter, "ul_meta.json");
    private static string DcMetaPath => Path.Combine(DownloadPaths.FrameLimiter, "dc_meta.json");

    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"{args.Group}.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        if (args.Verb is not ("install" or "uninstall"))
            return CliOutput.Fail(command, "UNKNOWN_VERB",
                $"Unknown verb '{args.Verb}'. Try: install, uninstall.", ExitCodes.Usage);

        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "This writes an addon into the game folder. Re-run with --yes (or --dry-run).", ExitCodes.Usage);

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

        CliState.EnsureInitialized(sp);
        var settings = sp.GetRequiredService<SettingsViewModel>();
        var progress = new CliProgress(!CliOutput.Json);

        return args.Group == "relimiter"
            ? await ReLimiterAsync(args, sp, ctx, command, progress, settings)
            : await DisplayCommanderAsync(args, sp, ctx, command, progress);
    }

    private static async Task<int> ReLimiterAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress, SettingsViewModel settings)
    {
        var deployPath = ModInstallService.GetAddonDeployPath(ctx.Path);
        var fileName = ctx.Is32Bit ? UlFile32 : UlFile64;

        if (args.Verb == "uninstall")
        {
            if (args.DryRun)
                return DryRun(command, ctx, "ReLimiter", "uninstall");

            RemoveFile(Path.Combine(deployPath, fileName));
            RemoveFile(Path.Combine(ctx.Path, fileName));
            RemoveFile(Path.Combine(deployPath, LegacyUlFile64));
            RemoveFile(Path.Combine(ctx.Path, LegacyUlFile64));
            RemoveFile(Path.Combine(deployPath, LegacyUlFile32));
            RemoveFile(Path.Combine(ctx.Path, LegacyUlFile32));
            RemoveFile(Path.Combine(deployPath, "relimiter.ini"));
            RemoveFile(Path.Combine(ctx.Path, "relimiter.ini"));

            foreach (var pattern in new[] { "relimiter*.log", "relimiter*.csv" })
            {
                try { foreach (var f in Directory.GetFiles(ctx.Path, pattern)) File.Delete(f); }
                catch { /* best effort */ }
            }

            TryDlss(sp, s => s.ClearPerGameFpsLimit(ctx.Name, ctx.Path));

            CliOutput.Human(() => Console.WriteLine($"ReLimiter removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true });
        }

        if (File.Exists(Path.Combine(deployPath, DcFile64)) || File.Exists(Path.Combine(deployPath, DcFile32)))
            return CliOutput.Fail(command, "CONFLICT",
                "Display Commander is installed for this game. Remove it first (they are mutually exclusive).", ExitCodes.Usage);

        var http = sp.GetRequiredService<HttpClient>();
        var etag = sp.GetRequiredService<GitHubETagCache>();
        var cachePath = Path.Combine(DownloadPaths.FrameLimiter, fileName);

        if (args.DryRun)
        {
            CliOutput.Human(() =>
            {
                Console.WriteLine("Dry run - nothing downloaded or written.");
                Console.WriteLine($"  component: ReLimiter");
                Console.WriteLine($"  deploy:    {Path.Combine(deployPath, fileName)}");
                Console.WriteLine($"  cache:     {(File.Exists(cachePath) ? "present" : "will download")}");
            });
            return CliOutput.Ok(command, new { dryRun = true, deploy = Path.Combine(deployPath, fileName) });
        }

        string? version = null;
        if (!File.Exists(cachePath) || args.Has("fresh"))
        {
            var (tag, url) = await CliDownloads.GetReleaseAssetAsync(http, etag, UlApi, fileName);
            version = tag;
            if (string.IsNullOrEmpty(url))
                return CliOutput.Fail(command, "NO_RELEASE",
                    $"No '{fileName}' asset in the ReLimiter GitHub release.", ExitCodes.NotFound);

            await CliDownloads.DownloadAsync(http, url, cachePath, progress);
        }
        else
        {
            CliOutput.Detail("using cached ReLimiter binary");
        }

        version ??= CliDownloads.ReadMetaVersion(UlMetaPath, ctx.Is32Bit);

        Directory.CreateDirectory(deployPath);
        File.Copy(cachePath, Path.Combine(deployPath, fileName), overwrite: true);

        RemoveFile(Path.Combine(deployPath, LegacyUlFile64));
        RemoveFile(Path.Combine(ctx.Path, LegacyUlFile64));
        RemoveFile(Path.Combine(deployPath, LegacyUlFile32));
        RemoveFile(Path.Combine(ctx.Path, LegacyUlFile32));

        if (!string.IsNullOrEmpty(version))
            CliDownloads.WriteMeta(UlMetaPath, version, ctx.Is32Bit);

        AuxInstallService.DeployUlIniIfAbsent(ctx.Path);

        if (settings.UlSharedPresets)
        {
            var ini = Path.Combine(deployPath, "relimiter.ini");
            if (File.Exists(ini))
            {
                AuxInstallService.ApplyUlSharedPresets(ini, true);
                if (settings.UlDlssHooks) AuxInstallService.ApplyUlDlssHooks(ini, true);
            }
        }

        if (settings.UlTargetFps > 0)
        {
            var ini = Path.Combine(deployPath, "relimiter.ini");
            if (File.Exists(ini)) AuxInstallService.ApplyUlTargetFps(ini, settings.UlTargetFps);
        }

        TryDlss(sp, s => s.SetPerGameFpsLimit(ctx.Name, ctx.Path, 0));

        var installedVersion = version ?? AuxInstallService.ReadInstalledVersion(deployPath, fileName);

        CliOutput.Human(() =>
        {
            Console.WriteLine($"ReLimiter installed for '{ctx.Name}'");
            Console.WriteLine($"  file:    {Path.Combine(deployPath, fileName)}");
            Console.WriteLine($"  version: {installedVersion ?? "unknown"}");
        });

        return CliOutput.Ok(command, new
        {
            installed = true,
            file = Path.Combine(deployPath, fileName),
            version = installedVersion,
        });
    }

    private static async Task<int> DisplayCommanderAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var aux = sp.GetRequiredService<IAuxInstallService>();
        var deployPath = ModInstallService.GetAddonDeployPath(ctx.Path);
        var fileName = ctx.Is32Bit ? DcFile32 : DcFile64;

        if (args.Verb == "uninstall")
        {
            if (args.DryRun)
                return DryRun(command, ctx, "Display Commander", "uninstall");

            var record = aux.FindRecord(ctx.Name, ctx.Path, "DisplayCommander");
            var installedAs = record?.InstalledAs ?? fileName;

            RemoveFile(Path.Combine(deployPath, installedAs));
            RemoveFile(Path.Combine(ctx.Path, installedAs));
            if (record != null) aux.RemoveRecord(record);

            TryDlss(sp, s => s.ClearPerGameFpsLimit(ctx.Name, ctx.Path));

            CliOutput.Human(() => Console.WriteLine($"Display Commander removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true, installedAs });
        }

        if (File.Exists(Path.Combine(deployPath, UlFile64)) || File.Exists(Path.Combine(deployPath, UlFile32)))
            return CliOutput.Fail(command, "CONFLICT",
                "ReLimiter is installed for this game. Remove it first (they are mutually exclusive).", ExitCodes.Usage);

        var http = sp.GetRequiredService<HttpClient>();
        var etag = sp.GetRequiredService<GitHubETagCache>();
        var cachePath = Path.Combine(DownloadPaths.FrameLimiter, fileName);

        if (args.DryRun)
        {
            CliOutput.Human(() =>
            {
                Console.WriteLine("Dry run - nothing downloaded or written.");
                Console.WriteLine($"  component: Display Commander");
                Console.WriteLine($"  deploy:    {Path.Combine(deployPath, fileName)}");
                Console.WriteLine($"  cache:     {(File.Exists(cachePath) ? "present" : "will download")}");
            });
            return CliOutput.Ok(command, new { dryRun = true, deploy = Path.Combine(deployPath, fileName) });
        }

        string? version = null;
        if (!File.Exists(cachePath) || args.Has("fresh"))
        {
            var (tag, url) = await CliDownloads.GetReleaseAssetAsync(http, etag, DcApi, fileName);
            version = tag;
            if (string.IsNullOrEmpty(url))
                return CliOutput.Fail(command, "NO_RELEASE",
                    $"No '{fileName}' asset in the Display Commander release.", ExitCodes.NotFound);

            await CliDownloads.DownloadAsync(http, url, cachePath, progress);
        }

        version ??= CliDownloads.ReadMetaVersion(DcMetaPath, ctx.Is32Bit);

        Directory.CreateDirectory(deployPath);
        File.Copy(cachePath, Path.Combine(deployPath, fileName), overwrite: true);

        RemoveFile(Path.Combine(deployPath, ctx.Is32Bit ? LegacyDcFile64 : LegacyDcFile32));
        RemoveFile(Path.Combine(ctx.Path, ctx.Is32Bit ? LegacyDcFile64 : LegacyDcFile32));

        if (!string.IsNullOrEmpty(version))
            CliDownloads.WriteMeta(DcMetaPath, version, ctx.Is32Bit);

        AuxInstallService.DeployDcIniIfAbsent(ctx.Path);
        TryDlss(sp, s => s.SetPerGameFpsLimit(ctx.Name, ctx.Path, 0));

        var recordNew = new AuxInstalledRecord
        {
            GameName = ctx.Name,
            InstallPath = ctx.Path,
            Store = ctx.Store,
            AddonType = "DisplayCommander",
            InstalledAs = fileName,
            InstalledAt = DateTime.UtcNow,
        };
        aux.SaveAuxRecord(recordNew);

        var installedVersion = AuxInstallService.ReadInstalledVersion(deployPath, fileName) ?? version;

        CliOutput.Human(() =>
        {
            Console.WriteLine($"Display Commander installed for '{ctx.Name}'");
            Console.WriteLine($"  file:    {Path.Combine(deployPath, fileName)}");
            Console.WriteLine($"  version: {installedVersion ?? "unknown"}");
        });

        return CliOutput.Ok(command, new
        {
            installed = true,
            file = Path.Combine(deployPath, fileName),
            version = installedVersion,
        });
    }

    private static void TryDlss(IServiceProvider sp, Action<DlssPresetService> action)
    {
        try { action(sp.GetRequiredService<DlssPresetService>()); }
        catch (Exception ex) { CliOutput.Detail($"driver FPS limit update failed: {ex.Message}"); }
    }

    private static void RemoveFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { CliOutput.Warn($"could not delete {path}: {ex.Message}"); }
    }

    private static int DryRun(string command, GameContext ctx, string component, string verb)
    {
        CliOutput.Human(() =>
        {
            Console.WriteLine("Dry run - nothing written.");
            Console.WriteLine($"  would {verb}: {component}");
            Console.WriteLine($"  game:        {ctx.Name}");
            Console.WriteLine($"  path:        {ctx.Path}");
        });
        return CliOutput.Ok(command, new { dryRun = true, action = verb, component, game = ctx.Name });
    }
}

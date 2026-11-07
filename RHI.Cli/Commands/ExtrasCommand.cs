using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

/// <summary>Extras: ASI loader, DLSS Enabler, 20/30 FG, RTX Encore, MFG unlocker, dgVoodoo2, Ultra+.</summary>
internal static class ExtrasCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"{args.Group}.{args.Verb}";

        if (args.Group == "ultraplus" && args.Verb == "url")
            return await UltraPlusUrlAsync(args, sp, command);

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        if (args.Verb is not ("install" or "uninstall"))
            return CliOutput.Fail(command, "UNKNOWN_VERB",
                $"Unknown verb '{args.Verb}'. Try: install, uninstall.", ExitCodes.Usage);

        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "This writes DLLs into the game folder. Re-run with --yes (or --dry-run).", ExitCodes.Usage);

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

        CliState.EnsureInitialized(sp);
        var settings = sp.GetRequiredService<SettingsViewModel>();
        var progress = new CliProgress(!CliOutput.Json);

        try
        {
            return args.Group switch
            {
                "asi" => await AsiAsync(args, sp, ctx, command, progress),
                "dlss-enabler" => await DlssEnablerAsync(args, sp, ctx, command, progress),
                "fg2030" => await Fg2030Async(args, sp, ctx, command, progress),
                "rtx-encore" => await RtxEncoreAsync(args, sp, ctx, command, progress),
                "mfg40" => await Mfg40Async(args, sp, ctx, command, progress),
                "dgvoodoo" => await DgVoodooAsync(args, sp, ctx, command),
                _ => CliOutput.Fail(command, "UNKNOWN_COMMAND", $"Unknown extras component '{args.Group}'.", ExitCodes.Usage),
            };
        }
        catch (Exception ex)
        {
            return CliOutput.Fail(command, "FAILED", ex.Message, ExitCodes.Failed);
        }
    }

    // ── ASI Loader ───────────────────────────────────────────────────────────

    private static async Task<int> AsiAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var ual = sp.GetRequiredService<UltimateAsiLoaderService>();
        var aux = sp.GetRequiredService<IAuxInstallService>();
        var card = BuildCard(ctx);

        if (args.Verb == "uninstall")
        {
            if (args.DryRun) return DryRun(command, ctx, "ASI Loader", "uninstall");

            var record = aux.FindRecord(ctx.Name, ctx.Path, UltimateAsiLoaderService.AddonType);
            var removed = ual.Uninstall(card);

            CliOutput.Human(() => Console.WriteLine(removed
                ? $"ASI Loader removed from '{ctx.Name}'."
                : $"ASI Loader was not installed in '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed, installedAs = record?.InstalledAs });
        }

        var dll = args.Get("dll") ?? UltimateAsiLoaderService.DefaultDllName;
        var conflict = FindConflict(args, sp, ctx, dll, "ASI Loader");
        if (conflict != null) return CliOutput.Fail(command, "CONFLICT", conflict, ExitCodes.Usage);

        if (args.DryRun)
            return DryRunWith(command, ctx, "ASI Loader", "install", dll);

        var (success, hookedOriginal) = await ual.InstallAsync(card, dll, progress);

        if (!success)
            return CliOutput.Fail(command, "INSTALL_FAILED",
                "ASI Loader staging/install failed (GitHub download or file copy).", ExitCodes.Failed);

        CliOutput.Human(() => Console.WriteLine($"ASI Loader installed for '{ctx.Name}' as {dll}."));
        if (hookedOriginal != null)
            CliOutput.Warn($"existing '{dll}' renamed to '{hookedOriginal}' for chain-loading");

        return CliOutput.Ok(command, new { installed = true, installedAs = dll, hookedOriginal });
    }

    // ── DLSS Enabler ─────────────────────────────────────────────────────────

    private static async Task<int> DlssEnablerAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var de = sp.GetRequiredService<DlssEnablerService>();
        var names = sp.GetRequiredService<IGameNameService>();
        var card = BuildCard(ctx);

        var current = Setting(names.DeInstalledAs, ctx);

        if (args.Verb == "uninstall")
        {
            if (args.DryRun) return DryRun(command, ctx, "DLSS Enabler", "uninstall");

            var dll = args.Get("dll") ?? current;
            if (string.IsNullOrEmpty(dll))
                return CliOutput.Fail(command, "UNKNOWN_DLL",
                    "No recorded DLSS Enabler DLL for this game. Pass --dll <name>.", ExitCodes.Usage);

            var removed = de.UninstallStandalone(ctx.Path, dll);
            if (removed) SetSetting(names.DeInstalledAs, ctx, null);
            SaveState(sp);

            CliOutput.Human(() => Console.WriteLine(removed
                ? $"DLSS Enabler removed from '{ctx.Name}'."
                : $"DLSS Enabler was not installed as '{dll}'."));
            return CliOutput.Ok(command, new { removed, installedAs = dll });
        }

        var chosen = args.Get("dll") ?? current ?? "version.dll";
        var conflict = FindConflict(args, sp, ctx, chosen, "DLSS Enabler");
        if (conflict != null) return CliOutput.Fail(command, "CONFLICT", conflict, ExitCodes.Usage);

        if (args.DryRun)
            return DryRunWith(command, ctx, "DLSS Enabler", "install", chosen);

        await de.EnsureStagingAsync(progress);
        var ok = await de.InstallStandaloneAsync(ctx.Name, ctx.Path, ctx.Store, chosen, current);

        if (!ok)
            return CliOutput.Fail(command, "INSTALL_FAILED", "DLSS Enabler install failed.", ExitCodes.Failed);

        SetSetting(names.DeInstalledAs, ctx, chosen);
        SaveState(sp);

        CliOutput.Human(() => Console.WriteLine($"DLSS Enabler installed for '{ctx.Name}' as {chosen}."));
        return CliOutput.Ok(command, new { installed = true, installedAs = chosen });
    }

    // ── 20/30 FG unlock ─────────────────────────────────────────────────────

    private static async Task<int> Fg2030Async(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var fg = sp.GetRequiredService<Dlssg20_30Service>();
        var names = sp.GetRequiredService<IGameNameService>();
        var current = Setting(names.Dlssg2030InstalledAs, ctx);

        if (args.Verb == "uninstall")
        {
            if (args.DryRun) return DryRun(command, ctx, "20/30 FG Unlock", "uninstall");
            if (string.IsNullOrEmpty(current))
                return CliOutput.Fail(command, "NOT_INSTALLED",
                    "No recorded 20/30 FG install for this game.", ExitCodes.NotFound);

            fg.Uninstall(ctx.Path, current);
            SetSetting(names.Dlssg2030InstalledAs, ctx, null);
            SaveState(sp);

            CliOutput.Human(() => Console.WriteLine($"20/30 FG Unlock removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true, installedAs = current });
        }

        var chosen = args.Get("dll") ?? current ?? "version.dll";
        var conflict = FindConflict(args, sp, ctx, chosen, "20/30 FG Unlock");
        if (conflict != null) return CliOutput.Fail(command, "CONFLICT", conflict, ExitCodes.Usage);

        if (args.DryRun)
            return DryRunWith(command, ctx, "20/30 FG Unlock", "install", chosen);

        if (!fg.IsStagingReady || fg.HasUpdate)
            await fg.EnsureStagingAsync(progress);

        var ok = fg.Install(ctx.Path, chosen);
        if (!ok)
            return CliOutput.Fail(command, "INSTALL_FAILED", "20/30 FG Unlock install failed.", ExitCodes.Failed);

        SetSetting(names.Dlssg2030InstalledAs, ctx, chosen);
        SaveState(sp);

        CliOutput.Human(() => Console.WriteLine($"20/30 FG Unlock installed for '{ctx.Name}' as {chosen}."));
        return CliOutput.Ok(command, new { installed = true, installedAs = chosen });
    }

    // ── RTX Encore ───────────────────────────────────────────────────────────

    private static async Task<int> RtxEncoreAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var encore = sp.GetRequiredService<RtxEncoreService>();
        var names = sp.GetRequiredService<IGameNameService>();
        var current = Setting(names.RtxEncoreInstalledAs, ctx);

        if (args.Verb == "uninstall")
        {
            if (args.DryRun) return DryRun(command, ctx, "RTX Encore", "uninstall");
            if (string.IsNullOrEmpty(current))
                return CliOutput.Fail(command, "NOT_INSTALLED",
                    "No recorded RTX Encore install for this game.", ExitCodes.NotFound);

            var modDir = encore.ModDir(ctx.Path, current);
            encore.Uninstall(ctx.Path, current, removeNrDll: false);

            var mfg = sp.GetRequiredService<Rtx40MfgService>();
            try { mfg.RemoveEncoreXessFiles(modDir); } catch { /* best effort */ }

            SetSetting(names.RtxEncoreInstalledAs, ctx, null);
            SaveState(sp);

            CliOutput.Human(() => Console.WriteLine($"RTX Encore removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true, installedAs = current });
        }

        var chosen = args.Get("dll") ?? current ?? "version.dll";
        var conflict = FindConflict(args, sp, ctx, chosen, "RTX Encore");
        if (conflict != null) return CliOutput.Fail(command, "CONFLICT", conflict, ExitCodes.Usage);

        if (args.DryRun)
            return DryRunWith(command, ctx, "RTX Encore", "install", chosen);

        await encore.EnsureStagingAsync(progress);
        var ok = await encore.InstallAsync(ctx.Path, chosen, current);
        if (!ok)
            return CliOutput.Fail(command, "INSTALL_FAILED", "RTX Encore install failed.", ExitCodes.Failed);

        SetSetting(names.RtxEncoreInstalledAs, ctx, chosen);
        SaveState(sp);

        var nrDeployed = false;
        if (args.Has("nr"))
        {
            var dlss = sp.GetRequiredService<IDlssStreamlineService>();
            var nrPath = await encore.EnsureNrDllCachedAsync(dlss);
            if (nrPath != null) nrDeployed = encore.DeployNrDll(ctx.Path);
            else CliOutput.Warn("required NR DLL is not available in the DLSS manifest");
        }

        CliOutput.Human(() => Console.WriteLine($"RTX Encore installed for '{ctx.Name}' as {chosen}."));
        return CliOutput.Ok(command, new { installed = true, installedAs = chosen, nrDeployed });
    }

    // ── Universal MFG Unlocker ──────────────────────────────────────────────

    private static async Task<int> Mfg40Async(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var mfg = sp.GetRequiredService<Rtx40MfgService>();
        var names = sp.GetRequiredService<IGameNameService>();
        var current = Setting(names.Rtx40MfgInstalledAs, ctx);

        if (args.Verb == "uninstall")
        {
            if (args.DryRun) return DryRun(command, ctx, "Universal MFG Unlocker", "uninstall");
            if (string.IsNullOrEmpty(current))
                return CliOutput.Fail(command, "NOT_INSTALLED",
                    "No recorded MFG Unlocker install for this game.", ExitCodes.NotFound);

            mfg.Uninstall(ctx.Path, current);
            SetSetting(names.Rtx40MfgInstalledAs, ctx, null);
            SaveState(sp);

            CliOutput.Human(() => Console.WriteLine($"Universal MFG Unlocker removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true, installedAs = current });
        }

        var chosen = args.Get("dll") ?? current ?? "version.dll";
        var conflict = FindConflict(args, sp, ctx, chosen, "Universal MFG Unlocker");
        if (conflict != null) return CliOutput.Fail(command, "CONFLICT", conflict, ExitCodes.Usage);

        if (args.DryRun)
            return DryRunWith(command, ctx, "Universal MFG Unlocker", "install", chosen);

        if (!mfg.IsStagingReady || mfg.HasUpdate)
            await mfg.EnsureStagingAsync();
        await mfg.EnsureXessStagingAsync();

        var ok = mfg.Install(ctx.Path, chosen);
        if (!ok)
            return CliOutput.Fail(command, "INSTALL_FAILED", "Universal MFG Unlocker install failed.", ExitCodes.Failed);

        SetSetting(names.Rtx40MfgInstalledAs, ctx, chosen);
        SaveState(sp);

        CliOutput.Human(() => Console.WriteLine($"Universal MFG Unlocker installed for '{ctx.Name}' as {chosen}."));
        return CliOutput.Ok(command, new { installed = true, installedAs = chosen });
    }

    // ── dgVoodoo2 ────────────────────────────────────────────────────────────

    private static async Task<int> DgVoodooAsync(Args args, IServiceProvider sp, GameContext ctx, string command)
    {
        var dg = sp.GetRequiredService<DgVoodooService>();
        var names = sp.GetRequiredService<IGameNameService>();
        var manifestService = sp.GetRequiredService<IManifestService>();

        if (args.Verb == "uninstall")
        {
            if (args.DryRun) return DryRun(command, ctx, "dgVoodoo2", "uninstall");

            dg.RemoveFromGame(ctx.Path);
            SetHash(names.DgVoodooStandaloneGames, ctx, false);
            SaveState(sp);

            CliOutput.Human(() => Console.WriteLine($"dgVoodoo2 removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true });
        }

        var manifest = manifestService.LoadCached() ?? await manifestService.FetchAsync();
        var versions = manifest?.DgVoodooVersions;
        if (versions == null || versions.Count == 0)
            return CliOutput.Fail(command, "NO_VERSIONS",
                "No dgVoodoo2 versions are listed in the manifest. Run 'rhictl manifest refresh' first.", ExitCodes.NotFound);

        var requested = args.Get("version");
        var entry = !string.IsNullOrEmpty(requested) && versions.TryGetValue(requested, out var requestedUrl)
            ? new KeyValuePair<string, string>(requested, requestedUrl)
            : versions.First();

        if (args.DryRun)
        {
            CliOutput.Human(() =>
            {
                Console.WriteLine("Dry run - nothing downloaded or written.");
                Console.WriteLine($"  component: dgVoodoo2 v{entry.Key}");
                Console.WriteLine($"  game:      {ctx.Name}");
                Console.WriteLine($"  path:      {ctx.Path}  ({(ctx.Is32Bit ? "32" : "64")}-bit)");
            });
            return CliOutput.Ok(command, new { dryRun = true, version = entry.Key });
        }

        await dg.EnsureStagedAsync(entry.Key, entry.Value);
        var deployed = await Task.Run(() => dg.DeployToGame(ctx.Path, entry.Key, is64Bit: !ctx.Is32Bit));

        if (deployed.Count == 0)
            return CliOutput.Fail(command, "INSTALL_FAILED", "dgVoodoo2 deploy produced no files.", ExitCodes.Failed);

        SetHash(names.DgVoodooStandaloneGames, ctx, true);
        names.DgVoodooVersionOverride[GameKey.From(ctx.Name, ctx.Store).ToKey()] = entry.Key;
        SaveState(sp);

        CliOutput.Human(() => Console.WriteLine(
            $"dgVoodoo2 v{entry.Key} deployed to '{ctx.Name}' ({deployed.Count} file(s))."));

        return CliOutput.Ok(command, new { installed = true, version = entry.Key, files = deployed });
    }

    // ── Ultra+ ───────────────────────────────────────────────────────────────

    private static async Task<int> UltraPlusUrlAsync(Args args, IServiceProvider sp, string command)
    {
        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        var ultra = sp.GetRequiredService<IUltraPlusService>();
        var manifestService = sp.GetRequiredService<IManifestService>();

        try { await ultra.InitAsync(); }
        catch (Exception ex) { CliOutput.Warn($"Ultra+ list refresh failed: {ex.Message}"); }

        var manifest = manifestService.LoadCached() ?? await manifestService.FetchAsync();
        var url = ultra.ResolveUrl(ctx.Name, manifest);

        if (string.IsNullOrEmpty(url))
            return CliOutput.Fail(command, "NO_MOD",
                $"No Ultra+ mod is listed for '{ctx.Name}'.", ExitCodes.NotFound);

        if (args.Has("open"))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            CliOutput.Human(() => Console.WriteLine($"Opened {url}"));
        }
        else
        {
            CliOutput.Human(() => Console.WriteLine(url));
        }

        return CliOutput.Ok(command, new { game = ctx.Name, url, opened = args.Has("open") });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static GameCardViewModel BuildCard(GameContext ctx) => new()
    {
        GameName = ctx.Name,
        InstallPath = ctx.Path,
        Source = ctx.Store,
        Is32Bit = ctx.Is32Bit,
        GraphicsApi = ctx.Api,
        DetectedApis = ctx.AllApis,
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.Instance,
        VulkanRenderingPath = ctx.IsVulkanOnly ? "Vulkan" : "DirectX",
    };

    private static string? FindConflict(Args args, IServiceProvider sp, GameContext ctx, string dll, string owner)
    {
        if (args.Force) return null;

        var aux = sp.GetRequiredService<IAuxInstallService>();
        var names = sp.GetRequiredService<IGameNameService>();
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in aux.LoadAll())
        {
            if (!ctx.MatchesRecord(record.GameName, record.InstallPath, record.Store)) continue;
            if (!string.IsNullOrEmpty(record.InstalledAs) &&
                !record.InstalledAs.Equals("displaycommander", StringComparison.OrdinalIgnoreCase))
            {
                taken[record.InstalledAs] = record.AddonType;
            }
        }

        foreach (var (dict, label) in new (Dictionary<string, string>, string)[]
                 {
                     (names.UalInstalledAs, "ASI Loader"),
                     (names.DeInstalledAs, "DLSS Enabler"),
                     (names.Dlssg2030InstalledAs, "20/30 FG Unlock"),
                     (names.RtxEncoreInstalledAs, "RTX Encore"),
                     (names.Rtx40MfgInstalledAs, "MFG Unlocker"),
                 })
        {
            var value = Setting(dict, ctx);
            if (!string.IsNullOrEmpty(value)) taken[value] = label;
        }

        if (!taken.TryGetValue(dll, out var existing)) return null;
        if (existing.Equals(owner, StringComparison.OrdinalIgnoreCase)) return null;

        return $"'{dll}' is already used by {existing} in this game. Choose another --dll (or pass --force).";
    }

    private static string? Setting(Dictionary<string, string> dict, GameContext ctx)
    {
        var key = GameKey.From(ctx.Name, ctx.Store).ToKey();
        if (dict.TryGetValue(key, out var value)) return value;
        return dict.TryGetValue(ctx.Name, out value) ? value : null;
    }

    private static void SetSetting(Dictionary<string, string> dict, GameContext ctx, string? value)
    {
        var key = GameKey.From(ctx.Name, ctx.Store).ToKey();
        if (value == null) dict.Remove(key);
        else dict[key] = value;
    }

    private static void SetHash(HashSet<string> set, GameContext ctx, bool present)
    {
        var key = GameKey.From(ctx.Name, ctx.Store).ToKey();
        if (present) set.Add(key);
        else set.Remove(key);
    }

    private static void SaveState(IServiceProvider sp)
    {
        var names = sp.GetRequiredService<IGameNameService>();
        names.SaveNameMappings(
            sp.GetRequiredService<IDllOverrideService>(),
            sp.GetRequiredService<SettingsViewModel>(),
            default,
            false,
            "",
            new List<CustomFilter>());
        names.FlushPendingSave();
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

    private static int DryRunWith(string command, GameContext ctx, string component, string verb, string dll)
    {
        CliOutput.Human(() =>
        {
            Console.WriteLine("Dry run - nothing written.");
            Console.WriteLine($"  would {verb}: {component} as {dll}");
            Console.WriteLine($"  game:        {ctx.Name}");
            Console.WriteLine($"  path:        {ctx.Path}");
        });
        return CliOutput.Ok(command, new { dryRun = true, action = verb, component, dll, game = ctx.Name });
    }
}

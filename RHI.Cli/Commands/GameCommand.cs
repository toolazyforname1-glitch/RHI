using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

internal static class GameCommand
{
    public static Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"game.{args.Verb}";
        var library = sp.GetRequiredService<IGameLibraryService>();

        switch (args.Verb)
        {
            case "add":
            {
                var rawPath = args.Get("path") ?? args.Positional.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(rawPath))
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "--path <game folder> is required.", ExitCodes.Usage));

                if (!Directory.Exists(rawPath))
                    return Task.FromResult(CliOutput.Fail(command, "PATH_NOT_FOUND",
                        $"Directory not found: {rawPath}", ExitCodes.NotFound));

                if (rawPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(CliOutput.Fail(command, "WINDOWSAPPS",
                        "Game Pass folders under WindowsApps cannot be modified.", ExitCodes.Permission));

                var detection = sp.GetRequiredService<IGameDetectionService>();
                var (installPath, engine) = detection.DetectEngineAndPath(rawPath);
                if (!Directory.Exists(installPath)) installPath = rawPath;

                var exe = ExeLocator.Find(installPath);
                var name = args.Get("name")
                           ?? ExeLocator.DeriveName(exe)
                           ?? new DirectoryInfo(installPath).Name;

                var storedPath = engine is EngineType.Unreal or EngineType.UnrealLegacy
                    ? GameResolver.UeProjectRoot(installPath) ?? rawPath
                    : installPath;

                var lib = LibraryStore.Load(library);
                var manual = library.ToManualGames(lib);

                // Replace an existing entry for the same name *or* same path — never duplicate.
                manual.RemoveAll(g =>
                    g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    LibraryStore.NormalizePath(g.InstallPath) == LibraryStore.NormalizePath(storedPath) ||
                    LibraryStore.NormalizePath(g.InstallPath) == LibraryStore.NormalizePath(installPath));
                manual.Add(new DetectedGame
                {
                    Name = name,
                    InstallPath = storedPath,
                    Source = "Manual",
                    IsManuallyAdded = true,
                });

                var rootKey = LibraryStore.NormalizePath(storedPath);
                if (engine != EngineType.Unknown)
                    lib.EngineTypeCache[rootKey] = engine.ToString();
                lib.ResolvedPathCache[rootKey] = installPath;

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run — nothing written.");
                        Console.WriteLine($"  name:      {name}");
                        Console.WriteLine($"  install:   {storedPath}");
                        Console.WriteLine($"  exe:       {exe ?? "(not found)"}");
                        Console.WriteLine($"  engine:    {engine}");
                    });
                    return Task.FromResult(CliOutput.Ok(command, new
                    {
                        dryRun = true,
                        name,
                        installPath = storedPath,
                        exe,
                        engine = engine.ToString(),
                    }));
                }

                LibraryStore.Save(library, lib, null, manual);

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"Added '{name}'");
                    Console.WriteLine($"  install: {storedPath}");
                    Console.WriteLine($"  exe:     {exe ?? "(not found)"}");
                    Console.WriteLine($"  engine:  {engine}");
                });

                return Task.FromResult(CliOutput.Ok(command, new
                {
                    name,
                    installPath = storedPath,
                    exe,
                    engine = engine.ToString(),
                }));
            }

            case "remove":
            {
                var name = args.Get("game") ?? args.Positional.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(name))
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "--game <name> is required.", ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Removing an entry rewrites game_library.json. Re-run with --yes.", ExitCodes.Usage));

                var lib = LibraryStore.Load(library);
                var storeGames = library.ToDetectedGames(lib);
                var manual = library.ToManualGames(lib);

                var removedStore = storeGames.RemoveAll(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                var removedManual = manual.RemoveAll(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (removedStore + removedManual == 0)
                    return Task.FromResult(CliOutput.Fail(command, "GAME_NOT_FOUND",
                        $"No game named '{name}' in the library.", ExitCodes.NotFound));

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would remove {removedStore} store entry/entries and {removedManual} manual entry/entries."));
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, removedStore, removedManual }));
                }

                LibraryStore.Save(library, lib, storeGames, manual);

                CliOutput.Human(() => Console.WriteLine(
                    $"Removed '{name}' ({removedStore} store, {removedManual} manual)."));

                return Task.FromResult(CliOutput.Ok(command, new { removedStore, removedManual }));
            }

            case "launch":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                if (CliGuards.GameIsRunning(ctx, out var running))
                    return Task.FromResult(CliOutput.Fail(command, "ALREADY_RUNNING", running, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var names = sp.GetRequiredService<IGameNameService>();

                var exe = ctx.Exe;
                if (names.LaunchExeOverrides.TryGetValue(ctx.Name, out var overrideExe)
                    && !string.IsNullOrWhiteSpace(overrideExe) && File.Exists(overrideExe))
                {
                    exe = overrideExe;
                }

                if (exe == null || !File.Exists(exe))
                    return Task.FromResult(CliOutput.Fail(command, "NO_EXE",
                        $"No launchable executable found for '{ctx.Name}'.", ExitCodes.NotFound));

                var launchArgs = names.LaunchArgsOverrides.TryGetValue(ctx.Name, out var savedArgs) && !string.IsNullOrWhiteSpace(savedArgs)
                    ? savedArgs
                    : "";

                if (args.Get("args") is { } extraArgs && !string.IsNullOrWhiteSpace(extraArgs))
                    launchArgs = string.IsNullOrWhiteSpace(launchArgs) ? extraArgs : launchArgs + " " + extraArgs;

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing launched.");
                        Console.WriteLine($"  exe:  {exe}");
                        Console.WriteLine($"  args: {launchArgs}");
                        Console.WriteLine($"  dir:  {Path.GetDirectoryName(exe)}");
                    });
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, exe, args = launchArgs }));
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = launchArgs,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ctx.Path,
                    UseShellExecute = true,
                });

                CliOutput.Human(() => Console.WriteLine($"Launched '{ctx.Name}'."));
                return Task.FromResult(CliOutput.Ok(command, new { launched = true, exe, args = launchArgs }));
            }

            case "favourite":
            case "unfavourite":
            case "hide":
            case "unhide":
            {
                var name = args.Get("game") ?? args.Positional.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(name))
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "--game <name> is required.", ExitCodes.Usage));

                var lib = LibraryStore.Load(library);
                var game = LibraryStore.Find(library, lib, name);
                if (game == null)
                    return Task.FromResult(CliOutput.Fail(command, "GAME_NOT_FOUND",
                        $"No game named '{name}' in the library.", ExitCodes.NotFound));

                var target = args.Verb is "favourite" or "unfavourite"
                    ? lib.FavouriteGames
                    : lib.HiddenGames;

                var key = LibraryStore.CompositeKey(game);
                var enable = args.Verb is "favourite" or "hide";
                if (enable) target.Add(key);
                else target.Remove(key);

                // Drop any legacy name-only entry written by older CLI builds.
                target.Remove(game.Name);
                target.Remove(game.Name + "|");

                LibraryStore.Save(library, lib);

                CliOutput.Human(() => Console.WriteLine(
                    $"{game.Name}: {(args.Verb is "favourite" or "unfavourite" ? "favourite" : "hidden")} = {(enable ? "yes" : "no")}"));

                return Task.FromResult(CliOutput.Ok(command, new
                {
                    game = game.Name,
                    key,
                    favourite = lib.FavouriteGames.Contains(key),
                    hidden = lib.HiddenGames.Contains(key),
                }));
            }

            case "set-path":
            {
                var name = args.Get("game") ?? args.Positional.FirstOrDefault();
                var newPath = args.Get("path");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(newPath))
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "--game <name> and --path <dir> are required.", ExitCodes.Usage));

                if (!Directory.Exists(newPath))
                    return Task.FromResult(CliOutput.Fail(command, "PATH_NOT_FOUND",
                        $"Directory not found: {newPath}", ExitCodes.NotFound));

                var lib = LibraryStore.Load(library);
                var manual = library.ToManualGames(lib);
                var entry = manual.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    return Task.FromResult(CliOutput.Fail(command, "NOT_MANUAL",
                        $"'{name}' is a store-managed entry. Only manual games can change their install path.",
                        ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This rewrites the library entry. Re-run with --yes.", ExitCodes.Usage));

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine($"Would change '{entry.Name}' path to {newPath}."));
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, game = entry.Name, path = newPath }));
                }

                var oldPath = entry.InstallPath;
                entry.InstallPath = newPath;

                var oldKey = LibraryStore.NormalizePath(oldPath);
                lib.ResolvedPathCache.Remove(oldKey);
                lib.BitnessCache.Remove(oldKey);
                lib.EngineTypeCache.Remove(oldKey);

                LibraryStore.Save(library, lib, null, manual);

                CliOutput.Human(() => Console.WriteLine($"'{entry.Name}' path updated:\n  {oldPath}\n  -> {newPath}"));
                return Task.FromResult(CliOutput.Ok(command, new { game = entry.Name, oldPath, path = newPath }));
            }

            case "set-launch":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                var exeArg = args.Get("exe");
                var argsArg = args.Get("args");
                if (string.IsNullOrWhiteSpace(exeArg) && argsArg == null)
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "Provide --exe <path> and/or --args \"...\".", ExitCodes.Usage));

                if (!string.IsNullOrWhiteSpace(exeArg))
                {
                    if (!File.Exists(exeArg))
                        return Task.FromResult(CliOutput.Fail(command, "PATH_NOT_FOUND",
                            $"Executable not found: {exeArg}", ExitCodes.NotFound));
                    exeArg = Path.GetFullPath(exeArg);
                }

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This writes a per-game launch override. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

                if (!args.Force && CliGuards.RhiIsRunning(out var launchGuard))
                    return Task.FromResult(CliOutput.Fail(command, "BLOCKED", launchGuard, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var names = sp.GetRequiredService<IGameNameService>();
                names.LaunchExeOverrides.TryGetValue(ctx.Name, out var oldExe);
                names.LaunchArgsOverrides.TryGetValue(ctx.Name, out var oldArgs);

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing written.");
                        if (exeArg != null) Console.WriteLine($"  exe:  {oldExe ?? "(auto)"} -> {exeArg}");
                        if (argsArg != null) Console.WriteLine($"  args: {oldArgs ?? "(none)"} -> {argsArg}");
                    });
                    return Task.FromResult(CliOutput.Ok(command, new
                    {
                        dryRun = true,
                        game = ctx.Name,
                        exe = exeArg,
                        args = argsArg,
                    }));
                }

                if (exeArg != null) names.LaunchExeOverrides[ctx.Name] = exeArg;
                if (argsArg != null) names.LaunchArgsOverrides[ctx.Name] = argsArg;
                CliState.Save(sp);

                CliOutput.Human(() =>
                {
                    if (exeArg != null) Console.WriteLine($"Launch exe for '{ctx.Name}': {exeArg}");
                    if (argsArg != null) Console.WriteLine($"Launch args for '{ctx.Name}': {argsArg}");
                });
                return Task.FromResult(CliOutput.Ok(command, new { game = ctx.Name, exe = exeArg, args = argsArg }));
            }

            case "reset-launch":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This clears the per-game launch override. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

                if (!args.Force && CliGuards.RhiIsRunning(out var resetGuard))
                    return Task.FromResult(CliOutput.Fail(command, "BLOCKED", resetGuard, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var names = sp.GetRequiredService<IGameNameService>();
                var hadExe = names.LaunchExeOverrides.Remove(ctx.Name);
                var hadArgs = names.LaunchArgsOverrides.Remove(ctx.Name);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would clear launch overrides for '{ctx.Name}' (exe={hadExe}, args={hadArgs})."));
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, hadExe, hadArgs }));
                }

                CliState.Save(sp);
                CliOutput.Human(() => Console.WriteLine(
                    $"Launch overrides cleared for '{ctx.Name}' (exe={hadExe}, args={hadArgs})."));
                return Task.FromResult(CliOutput.Ok(command, new { game = ctx.Name, hadExe, hadArgs }));
            }

            case "set-wiki-name":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                var wikiName = args.Get("name") ?? args.Get("value");
                if (string.IsNullOrWhiteSpace(wikiName))
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "--name <wiki mod name> is required.", ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This writes a name mapping. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

                if (!args.Force && CliGuards.RhiIsRunning(out var nameGuard))
                    return Task.FromResult(CliOutput.Fail(command, "BLOCKED", nameGuard, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var names = sp.GetRequiredService<IGameNameService>();
                var previous = names.GetUserNameMapping(ctx.Name);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would map '{ctx.Name}' -> '{wikiName}' (was '{previous ?? "(none)"}')."));
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, wikiName, previous }));
                }

                names.AddNameMapping(ctx.Name, wikiName);
                CliState.Save(sp);

                CliOutput.Human(() => Console.WriteLine(
                    $"'{ctx.Name}' now maps to wiki mod '{wikiName}' (was '{previous ?? "(none)"}')."));
                return Task.FromResult(CliOutput.Ok(command, new { game = ctx.Name, wikiName, previous }));
            }

            case "reset-wiki-name":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This removes the name mapping. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

                if (!args.Force && CliGuards.RhiIsRunning(out var resetNameGuard))
                    return Task.FromResult(CliOutput.Fail(command, "BLOCKED", resetNameGuard, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var names = sp.GetRequiredService<IGameNameService>();
                var previous = names.GetUserNameMapping(ctx.Name);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would remove the wiki mapping for '{ctx.Name}' (was '{previous ?? "(none)"}')."));
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, previous }));
                }

                names.RemoveNameMapping(ctx.Name);
                CliState.Save(sp);

                CliOutput.Human(() => Console.WriteLine(
                    $"Wiki mapping removed for '{ctx.Name}' (was '{previous ?? "(none)"}')."));
                return Task.FromResult(CliOutput.Ok(command, new { game = ctx.Name, previous }));
            }

            case "set-name":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                var newName = args.Get("name") ?? args.Get("value");
                if (string.IsNullOrWhiteSpace(newName))
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "--name <new display name> is required.", ExitCodes.Usage));
                if (newName.Equals(ctx.Name, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(CliOutput.Fail(command, "NO_CHANGE",
                        $"'{ctx.Name}' already has that name.", ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Renaming migrates settings, records and the library entry. Re-run with --yes.", ExitCodes.Usage));

                if (CliGuards.CheckMutation(args, ctx, out var renameGuard))
                    return Task.FromResult(CliOutput.Fail(command, "BLOCKED", renameGuard, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var names = sp.GetRequiredService<IGameNameService>();
                var dll = sp.GetRequiredService<IDllOverrideService>();
                var aux = sp.GetRequiredService<IAuxInstallService>();
                var mods = sp.GetRequiredService<IModInstallService>();

                var lib = LibraryStore.Load(library);
                var storeGames = library.ToDetectedGames(lib);
                var manual = library.ToManualGames(lib);

                var card = new GameCardViewModel
                {
                    GameName = ctx.Name,
                    InstallPath = ctx.Path,
                    Source = ctx.Store,
                };
                card.RsRecord = aux.FindRecord(ctx.Name, ctx.Path, "ReShade")
                                ?? aux.FindRecord(ctx.Name, ctx.Path, "ReShadeNormal");
                card.InstalledRecord = mods.FindRecord(ctx.Name, ctx.Path);
                card.LumaRecord = LumaService.GetRecordByPath(ctx.Path);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would rename '{ctx.Name}' -> '{newName}' (settings, records and library)."));
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, oldName = ctx.Name, newName }));
                }

                names.RenameGame(ctx.Name, newName, new List<GameCardViewModel> { card }, manual, dll);
                CliState.Save(sp);

                foreach (var g in manual.Concat(storeGames))
                {
                    if (g.Name.Equals(ctx.Name, StringComparison.OrdinalIgnoreCase))
                        g.Name = newName;
                }
                LibraryStore.Save(library, lib, storeGames, manual);

                CliOutput.Human(() => Console.WriteLine($"Renamed '{ctx.Name}' -> '{newName}'."));
                return Task.FromResult(CliOutput.Ok(command, new { oldName = ctx.Name, newName }));
            }

            case "set-dll":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                var rsName = args.Get("reshade");
                var dcName = args.Get("dc");
                var osName = args.Get("os");
                if (string.IsNullOrWhiteSpace(rsName) && string.IsNullOrWhiteSpace(dcName) && string.IsNullOrWhiteSpace(osName))
                    return Task.FromResult(CliOutput.Fail(command, "MISSING_ARG",
                        "Provide --reshade <file.dll>, --dc <file.dll> and/or --os <file.dll>.", ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This renames component DLLs in the game folder. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

                if (CliGuards.CheckMutation(args, ctx, out var dllGuard))
                    return Task.FromResult(CliOutput.Fail(command, "BLOCKED", dllGuard, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var dll = sp.GetRequiredService<IDllOverrideService>();
                var aux = sp.GetRequiredService<IAuxInstallService>();
                var existing = dll.GetDllOverride(ctx.Name);

                if (!string.IsNullOrWhiteSpace(osName))
                {
                    var available = dll.GetAvailableOsDllNames(ctx.Name, ctx.Is32Bit,
                        existing?.ReShadeFileName, existing?.DcFileName);
                    if (!available.Contains(osName, StringComparer.OrdinalIgnoreCase))
                        return Task.FromResult(CliOutput.Fail(command, "BAD_VALUE",
                            $"'{osName}' is not available for this game. Choose one of: {string.Join(", ", available)}",
                            ExitCodes.Usage));
                }

                var effectiveRs = rsName ?? existing?.ReShadeFileName ?? dll.GetEffectiveRsName(ctx.Name);
                var effectiveDc = dcName ?? existing?.DcFileName ?? "";

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing renamed.");
                        if (rsName != null || dcName != null)
                            Console.WriteLine($"  ReShade/DC: {effectiveRs} / {(string.IsNullOrEmpty(effectiveDc) ? "(unchanged)" : effectiveDc)}");
                        if (osName != null) Console.WriteLine($"  OptiScaler: {osName}");
                    });
                    return Task.FromResult(CliOutput.Ok(command, new
                    {
                        dryRun = true,
                        game = ctx.Name,
                        reshade = rsName,
                        dc = dcName,
                        os = osName,
                    }));
                }

                var card = new GameCardViewModel
                {
                    GameName = ctx.Name,
                    InstallPath = ctx.Path,
                    Source = ctx.Store,
                    Is32Bit = ctx.Is32Bit,
                };
                card.RsRecord = aux.FindRecord(ctx.Name, ctx.Path, "ReShade")
                                ?? aux.FindRecord(ctx.Name, ctx.Path, "ReShadeNormal");
                card.RsInstalledFile = card.RsRecord?.InstalledAs;

                if (rsName != null || dcName != null)
                {
                    if (existing == null) dll.EnableDllOverride(card, effectiveRs, effectiveDc);
                    else dll.UpdateDllOverrideNames(card, effectiveRs, effectiveDc);
                }
                if (osName != null) dll.SetOsDllOverride(ctx.Name, osName);

                CliState.Save(sp);

                CliOutput.Human(() => Console.WriteLine(
                    $"DLL override set for '{ctx.Name}': ReShade='{effectiveRs}'" +
                    (string.IsNullOrEmpty(effectiveDc) ? "" : $", DC='{effectiveDc}'") +
                    (osName == null ? "" : $", OptiScaler='{osName}'")));
                return Task.FromResult(CliOutput.Ok(command, new
                {
                    game = ctx.Name,
                    reshade = effectiveRs,
                    dc = effectiveDc,
                    os = osName,
                }));
            }

            case "reset-dll":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return Task.FromResult(CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage));

                if (!args.Yes && !args.DryRun)
                    return Task.FromResult(CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This renames the custom DLLs back. Re-run with --yes (or --dry-run).", ExitCodes.Usage));

                if (CliGuards.CheckMutation(args, ctx, out var resetDllGuard))
                    return Task.FromResult(CliOutput.Fail(command, "BLOCKED", resetDllGuard, ExitCodes.GameRunning));

                CliState.EnsureInitialized(sp);
                var dll = sp.GetRequiredService<IDllOverrideService>();
                var had = dll.HasDllOverride(ctx.Name);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        had ? $"Would disable the DLL override for '{ctx.Name}'." : $"No DLL override set for '{ctx.Name}'."));
                    return Task.FromResult(CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, had }));
                }

                if (had)
                {
                    var aux = sp.GetRequiredService<IAuxInstallService>();
                    var card = new GameCardViewModel
                    {
                        GameName = ctx.Name,
                        InstallPath = ctx.Path,
                        Source = ctx.Store,
                    };
                    card.RsRecord = aux.FindRecord(ctx.Name, ctx.Path, "ReShade")
                                    ?? aux.FindRecord(ctx.Name, ctx.Path, "ReShadeNormal");
                    card.RsInstalledFile = card.RsRecord?.InstalledAs;
                    dll.DisableDllOverride(card);
                }
                dll.RemoveDllOverride(ctx.Name);
                CliState.Save(sp);

                CliOutput.Human(() => Console.WriteLine(had
                    ? $"DLL override disabled for '{ctx.Name}'."
                    : $"No DLL override was set for '{ctx.Name}'."));
                return Task.FromResult(CliOutput.Ok(command, new { game = ctx.Name, removed = had }));
            }

            case "set-override":
                return GameOverridesSet.RunAsync(args, sp, command, clear: false);

            case "clear-override":
                return GameOverridesSet.RunAsync(args, sp, command, clear: true);

            case "reset-overrides":
                return GameOverridesReset.RunAsync(args, sp, command);

            default:
                return Task.FromResult(CliOutput.Fail(command, "UNKNOWN_VERB",
                    $"Unknown game verb '{args.Verb}'. Try: add, remove, launch, set-launch, reset-launch, set-wiki-name, reset-wiki-name, reset-overrides.", ExitCodes.Usage));
        }
    }

}

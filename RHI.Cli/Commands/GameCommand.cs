using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

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

                var lib = LibraryStore.Load(library);
                var manual = library.ToManualGames(lib);

                // Replace an existing entry for the same name *or* same path — never duplicate.
                manual.RemoveAll(g =>
                    g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    LibraryStore.NormalizePath(g.InstallPath) == LibraryStore.NormalizePath(installPath));
                manual.Add(new DetectedGame
                {
                    Name = name,
                    InstallPath = installPath,
                    Source = "Manual",
                    IsManuallyAdded = true,
                });

                // Cache the engine lookup the same way the GUI does.
                var rootKey = LibraryStore.NormalizePath(rawPath);
                if (engine != EngineType.Unknown)
                    lib.EngineTypeCache[rootKey] = engine.ToString();
                lib.ResolvedPathCache[rootKey] = installPath;

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run — nothing written.");
                        Console.WriteLine($"  name:      {name}");
                        Console.WriteLine($"  install:   {installPath}");
                        Console.WriteLine($"  exe:       {exe ?? "(not found)"}");
                        Console.WriteLine($"  engine:    {engine}");
                    });
                    return Task.FromResult(CliOutput.Ok(command, new
                    {
                        dryRun = true,
                        name,
                        installPath,
                        exe,
                        engine = engine.ToString(),
                    }));
                }

                LibraryStore.Save(library, lib, null, manual);

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"Added '{name}'");
                    Console.WriteLine($"  install: {installPath}");
                    Console.WriteLine($"  exe:     {exe ?? "(not found)"}");
                    Console.WriteLine($"  engine:  {engine}");
                });

                return Task.FromResult(CliOutput.Ok(command, new
                {
                    name,
                    installPath,
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

            default:
                return Task.FromResult(CliOutput.Fail(command, "UNKNOWN_VERB",
                    $"Unknown game verb '{args.Verb}'. Try: add, remove.", ExitCodes.Usage));
        }
    }

}

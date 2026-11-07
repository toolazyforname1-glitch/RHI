using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class LibraryCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"library.{args.Verb}";
        var library = sp.GetRequiredService<IGameLibraryService>();

        switch (args.Verb)
        {
            case "list":
            {
                var lib = LibraryStore.Load(library);
                var all = LibraryStore.All(library, lib).ToList();

                var favouritesOnly = args.Has("favourites");
                var hiddenOnly = args.Has("hidden");
                var showAll = args.Has("all");
                var installedOnly = args.Has("installed");
                var engineFilter = args.Get("engine");
                var storeFilter = args.Get("store");

                var games = all
                    .Where(g => showAll || (hiddenOnly
                        ? lib.HiddenGames.Contains(LibraryStore.CompositeKey(g))
                        : !lib.HiddenGames.Contains(LibraryStore.CompositeKey(g))))
                    .Where(g => !favouritesOnly || lib.FavouriteGames.Contains(LibraryStore.CompositeKey(g)))
                    .Where(g => string.IsNullOrWhiteSpace(storeFilter)
                                || SourceLabel(g).Equals(storeFilter, StringComparison.OrdinalIgnoreCase))
                    .Where(g => string.IsNullOrWhiteSpace(engineFilter)
                                || EngineOf(lib, g).Equals(engineFilter, StringComparison.OrdinalIgnoreCase))
                    .Where(g => !installedOnly || HasAnyComponent(sp, g))
                    .ToList();

                CliOutput.Human(() =>
                {
                    if (games.Count == 0)
                    {
                        Console.WriteLine("No games match the filter. Run 'rhictl library refresh' or 'rhictl game add --path <dir>'.");
                        return;
                    }
                    CliOutput.Table(new[] { "NAME", "STORE", "ENGINE", "PATH" },
                        games.Select(g => new[] { g.Name, SourceLabel(g), EngineOf(lib, g), g.InstallPath }));
                    Console.WriteLine();
                    Console.WriteLine($"{games.Count} game(s). Last scanned: {LastScanned(lib)}");
                });

                return CliOutput.Ok(command, new
                {
                    count = games.Count,
                    lastScannedUtc = lib.LastScanned,
                    games = games.Select(g => new
                    {
                        name = g.Name,
                        store = SourceLabel(g),
                        engine = EngineOf(lib, g),
                        path = g.InstallPath,
                        manual = g.IsManuallyAdded,
                        favourite = lib.FavouriteGames.Contains(LibraryStore.CompositeKey(g)),
                        hidden = lib.HiddenGames.Contains(LibraryStore.CompositeKey(g)),
                    }),
                });
            }

            case "show":
            {
                var name = args.Get("game") ?? args.Positional.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(name))
                    return CliOutput.Fail(command, "MISSING_ARG", "--game <name> is required.", ExitCodes.Usage);

                var lib = LibraryStore.Load(library);
                var game = LibraryStore.Find(library, lib, name);
                if (game == null)
                    return CliOutput.Fail(command, "GAME_NOT_FOUND", $"No game named '{name}' in the library.", ExitCodes.NotFound);

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"Name:    {game.Name}");
                    Console.WriteLine($"Store:   {SourceLabel(game)}");
                    Console.WriteLine($"Path:    {game.InstallPath}");
                    Console.WriteLine($"Exists:  {(Directory.Exists(game.InstallPath) ? "yes" : "no")}");
                });

                return CliOutput.Ok(command, new
                {
                    name = game.Name,
                    store = SourceLabel(game),
                    path = game.InstallPath,
                    manual = game.IsManuallyAdded,
                    exists = Directory.Exists(game.InstallPath),
                });
            }

            case "refresh":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Refreshing rewrites game_library.json. Re-run with --yes (or --dry-run to preview).",
                        ExitCodes.Usage);

                var det = sp.GetRequiredService<IGameDetectionService>();
                var found = new List<DetectedGame>();

                void Scan(string label, Func<List<DetectedGame>> scan)
                {
                    try
                    {
                        var result = scan();
                        found.AddRange(result);
                        CliOutput.Detail($"{label}: {result.Count}");
                    }
                    catch (Exception ex)
                    {
                        CliOutput.Warn($"{label} scan failed: {ex.Message}");
                    }
                }

                await Task.Run(() =>
                {
                    Scan("Steam", det.FindSteamGames);
                    Scan("GOG", det.FindGogGames);
                    Scan("Epic", det.FindEpicGames);
                    Scan("EA", det.FindEaGames);
                    Scan("Xbox", det.FindXboxGames);
                    Scan("Ubisoft", det.FindUbisoftGames);
                    Scan("Battle.net", det.FindBattleNetGames);
                    Scan("Rockstar", det.FindRockstarGames);
                });

                var distinct = found
                    .GroupBy(LibraryStore.DedupeKey, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                var lib = LibraryStore.Load(library);
                var manual = library.ToManualGames(lib);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would save {distinct.Count} store game(s) + {manual.Count} manual game(s)."));
                    return CliOutput.Ok(command, new { dryRun = true, storeGames = distinct.Count, manualGames = manual.Count });
                }

                LibraryStore.Save(library, lib, distinct, manual);

                CliOutput.Human(() => Console.WriteLine(
                    $"Saved {distinct.Count} store game(s) + {manual.Count} manual game(s) to game_library.json."));

                return CliOutput.Ok(command, new { storeGames = distinct.Count, manualGames = manual.Count });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    $"Unknown library verb '{args.Verb}'. Try: list, show, refresh.", ExitCodes.Usage);
        }
    }

    private static string SourceLabel(DetectedGame g) =>
        g.IsManuallyAdded ? "Manual" : string.IsNullOrWhiteSpace(g.Source) ? "Unknown" : g.Source;

    private static string EngineOf(SavedGameLibrary lib, DetectedGame g)
    {
        var key = LibraryStore.NormalizePath(g.InstallPath);
        return lib.EngineTypeCache.TryGetValue(key, out var engine) ? engine : "Unknown";
    }

    private static bool HasAnyComponent(IServiceProvider sp, DetectedGame game)
    {
        try
        {
            var path = LibraryStore.NormalizePath(game.InstallPath);
            var aux = sp.GetRequiredService<IAuxInstallService>();
            if (aux.LoadAll().Any(r => LibraryStore.NormalizePath(r.InstallPath) == path)) return true;

            var mods = sp.GetRequiredService<IModInstallService>();
            if (mods.LoadAll().Any(r => LibraryStore.NormalizePath(r.InstallPath) == path)) return true;

            if (LumaService.GetRecordByPath(game.InstallPath) != null) return true;

            var dxvk = sp.GetRequiredService<IDxvkService>();
            if (dxvk.LoadAllRecords().Any(r => LibraryStore.NormalizePath(r.InstallPath) == path)) return true;

            var reff = sp.GetRequiredService<IREFrameworkService>();
            if (reff.GetRecords().Any(r => LibraryStore.NormalizePath(r.InstallPath) == path)) return true;

            var dof = sp.GetRequiredService<DofFixService>();
            if (dof.IsInstalledIn(game.InstallPath)) return true;
        }
        catch (Exception ex)
        {
            CliOutput.Detail($"installed filter probe failed: {ex.Message}");
        }

        return false;
    }

    private static string LastScanned(SavedGameLibrary lib) =>
        lib.LastScanned == default ? "never" : lib.LastScanned.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

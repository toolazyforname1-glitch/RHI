using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli;

/// <summary>
/// Load/save helpers around <see cref="IGameLibraryService"/>.
/// Every save round-trips the existing collections so the CLI never wipes the
/// GUI's caches, hidden/favourite flags or DXVK state.
/// </summary>
internal static class LibraryStore
{
    public static SavedGameLibrary Load(IGameLibraryService svc) => svc.Load() ?? new SavedGameLibrary();

    public static void Save(
        IGameLibraryService svc,
        SavedGameLibrary lib,
        List<DetectedGame>? storeGames = null,
        List<DetectedGame>? manualGames = null)
    {
        svc.Save(
            storeGames ?? svc.ToDetectedGames(lib),
            lib.AddonScanCache,
            lib.HiddenGames,
            lib.FavouriteGames,
            manualGames ?? svc.ToManualGames(lib),
            lib.EngineTypeCache,
            lib.ResolvedPathCache,
            lib.AddonFileCache,
            lib.BitnessCache,
            lib.LastSelectedGame,
            lib.DxvkEnabledGames,
            lib.DxvkInstalledVersions,
            lib.ExcludeFromUpdateAllDxvk,
            lib.UpdateAvailableSnapshot,
            lib.DlssPathsCache,
            lib.RsInstalledVersions,
            lib.RdxInstalledVersions,
            lib.EngineFolderStamps);
    }

    public static IEnumerable<DetectedGame> All(IGameLibraryService svc, SavedGameLibrary lib)
    {
        foreach (var g in svc.ToDetectedGames(lib)) yield return g;
        foreach (var g in svc.ToManualGames(lib)) yield return g;
    }

    public static DetectedGame? Find(IGameLibraryService svc, SavedGameLibrary lib, string name) =>
        All(svc, lib).FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string DedupeKey(DetectedGame g) =>
        $"{g.Name.Trim().ToLowerInvariant()}|{NormalizePath(g.InstallPath)}";

    public static string CompositeKey(DetectedGame g) =>
        GameKey.From(g.Name, g.IsManuallyAdded ? "Manual" : (g.Source ?? "")).ToKey();

    public static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant(); }
        catch { return path.Trim().TrimEnd('\\').ToLowerInvariant(); }
    }
}

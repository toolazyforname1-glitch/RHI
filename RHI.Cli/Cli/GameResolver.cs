using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli;

/// <summary>A resolved game: library entry or an explicit --path.</summary>
internal sealed class GameContext
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Store { get; init; }
    public required string Engine { get; init; }
    public required GraphicsApiType Api { get; init; }
    public required HashSet<GraphicsApiType> AllApis { get; init; }
    public required bool Is32Bit { get; init; }
    public string? Exe { get; init; }
    public bool FromLibrary { get; init; }

    public bool IsVulkanOnly =>
        AllApis.Contains(GraphicsApiType.Vulkan) &&
        !AllApis.Contains(GraphicsApiType.DirectX9) &&
        !AllApis.Contains(GraphicsApiType.DirectX10) &&
        !AllApis.Contains(GraphicsApiType.DirectX11) &&
        !AllApis.Contains(GraphicsApiType.DirectX12) &&
        !AllApis.Contains(GraphicsApiType.OpenGL);

    public bool MatchesRecord(string gameName, string installPath, string? store)
    {
        // Records are per install path. A name-only match is dangerous: two copies
        // of the same game would report each other's components. Name is used only
        // for legacy records that carry no path.
        if (string.IsNullOrWhiteSpace(installPath))
            return gameName.Equals(Name, StringComparison.OrdinalIgnoreCase);

        return LibraryStore.NormalizePath(installPath) == LibraryStore.NormalizePath(Path);
    }
}

internal static class GameResolver
{
    public static bool TryResolve(
        Args args,
        IServiceProvider sp,
        out GameContext context,
        out string errorCode,
        out string errorMessage)
    {
        context = null!;
        errorCode = "";
        errorMessage = "";

        var library = sp.GetRequiredService<IGameLibraryService>();
        var detection = sp.GetRequiredService<IGameDetectionService>();
        var pe = sp.GetRequiredService<IPeHeaderService>();
        var lib = LibraryStore.Load(library);

        string rawPath;
        string? name = null;
        var store = "";
        var fromLibrary = false;

        var explicitPath = args.Get("path");
        var explicitName = args.Get("game") ?? args.Positional.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!Directory.Exists(explicitPath))
            {
                errorCode = "PATH_NOT_FOUND";
                errorMessage = $"Directory not found: {explicitPath}";
                return false;
            }
            rawPath = explicitPath;

            var byPath = LibraryStore.All(library, lib)
                .FirstOrDefault(g => LibraryStore.NormalizePath(g.InstallPath) == LibraryStore.NormalizePath(explicitPath));
            if (byPath != null)
            {
                name = byPath.Name;
                store = byPath.IsManuallyAdded ? "Manual" : byPath.Source;
                fromLibrary = true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(explicitName))
        {
            var game = LibraryStore.Find(library, lib, explicitName);
            if (game == null)
            {
                errorCode = "GAME_NOT_FOUND";
                errorMessage = $"No game named '{explicitName}' in the library. Use --path <dir> for an unlisted game.";
                return false;
            }
            rawPath = game.InstallPath;
            name = game.Name;
            store = game.IsManuallyAdded ? "Manual" : game.Source;
            fromLibrary = true;

            if (!Directory.Exists(rawPath))
            {
                errorCode = "PATH_NOT_FOUND";
                errorMessage = $"Install path for '{game.Name}' does not exist: {rawPath}";
                return false;
            }
        }
        else
        {
            errorCode = "MISSING_ARG";
            errorMessage = "--game <name> or --path <dir> is required.";
            return false;
        }

        var (resolvedPath, engine) = detection.DetectEngineAndPath(rawPath);
        if (!Directory.Exists(resolvedPath)) resolvedPath = rawPath;

        string? exe = null;
        try { exe = ExeLocator.Find(resolvedPath); } catch { /* best effort */ }

        var api = GraphicsApiType.Unknown;
        if (exe != null)
        {
            try { api = GraphicsApiDetector.Detect(exe); } catch { /* best effort */ }
        }
        if (api == GraphicsApiType.Unknown && engine == EngineType.Unity)
        {
            try { api = GraphicsApiDetector.DetectUnityFromBootConfig(resolvedPath); } catch { /* best effort */ }
        }

        var allApis = new HashSet<GraphicsApiType>();
        if (exe != null)
        {
            try { allApis = GraphicsApiDetector.DetectAllApis(exe); } catch { /* best effort */ }
        }
        if (allApis.Count == 0 && api != GraphicsApiType.Unknown) allApis.Add(api);

        var is32Bit = false;
        try { is32Bit = pe.DetectGameArchitecture(resolvedPath) == MachineType.I386; } catch { /* best effort */ }

        context = new GameContext
        {
            Name = name ?? new DirectoryInfo(resolvedPath).Name,
            Path = resolvedPath,
            Store = string.IsNullOrWhiteSpace(store) ? "Unknown" : store,
            Engine = engine.ToString(),
            Api = api,
            AllApis = allApis,
            Is32Bit = is32Bit,
            Exe = exe,
            FromLibrary = fromLibrary,
        };
        return true;
    }
}

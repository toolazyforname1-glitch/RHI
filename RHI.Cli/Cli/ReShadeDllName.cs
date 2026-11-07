using RenoDXCommander.Models;

namespace RenoDXCommander.Cli;

internal static class ReShadeDllName
{
    /// <summary>
    /// Mirrors MainViewModel.ResolveAutoReShadeFilename. Returns null when the
    /// default dxgi.dll should be used.
    /// </summary>
    public static string? Resolve(HashSet<GraphicsApiType> apis)
    {
        if (apis.Contains(GraphicsApiType.DirectX11) || apis.Contains(GraphicsApiType.DirectX12))
            return null;
        if (apis.Contains(GraphicsApiType.DirectX9)) return "d3d9.dll";
        if (apis.Contains(GraphicsApiType.DirectX8)) return "d3d8.dll";
        if (apis.Count == 1 && apis.Contains(GraphicsApiType.OpenGL)) return "opengl32.dll";
        return null;
    }
}

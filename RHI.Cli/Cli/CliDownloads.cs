using System.Text.Json;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli;

internal static class CliDownloads
{
    public static async Task DownloadAsync(
        HttpClient http,
        string url,
        string destinationPath,
        IProgress<(string message, double percent)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var tempPath = destinationPath + ".tmp";

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        long downloaded = 0;

        using (var network = await response.Content.ReadAsStreamAsync(ct))
        using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            int read;
            while ((read = await network.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0)
                    progress?.Report(($"Downloading... {downloaded / 1024} KB", (double)downloaded / total * 100));
            }
        }

        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        File.Move(tempPath, destinationPath);
        progress?.Report(("Downloaded", 100));
    }

    /// <summary>Reads a GitHub release and returns (tag, asset download URL) for the requested asset name.</summary>
    public static async Task<(string? Version, string? Url)> GetReleaseAssetAsync(
        HttpClient http,
        GitHubETagCache etagCache,
        string apiUrl,
        string assetName)
    {
        var json = await etagCache.GetWithETagAsync(http, apiUrl);
        if (json == null) return (null, null);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var version = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        string? url = null;

        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var name) &&
                    name.GetString()?.Equals(assetName, StringComparison.OrdinalIgnoreCase) == true &&
                    asset.TryGetProperty("browser_download_url", out var urlElement))
                {
                    url = urlElement.GetString();
                    break;
                }
            }
        }

        return (version, url);
    }

    public static void WriteMeta(string metaPath, string version, bool is32Bit)
    {
        try
        {
            var clean = version.TrimStart('v', 'V');
            Dictionary<string, object> meta;

            if (File.Exists(metaPath))
            {
                try
                {
                    meta = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(metaPath))
                           ?? new Dictionary<string, object>();
                }
                catch
                {
                    meta = new Dictionary<string, object>();
                }
            }
            else
            {
                meta = new Dictionary<string, object>();
            }

            meta[is32Bit ? "InstalledVersion32" : "InstalledVersion64"] = clean;
            meta["InstalledVersion"] = clean;
            meta["UpdatedAt"] = DateTime.UtcNow.ToString("o");

            Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            CliOutput.Detail($"meta write failed: {ex.Message}");
        }
    }

    public static string? ReadMetaVersion(string metaPath, bool is32Bit)
    {
        try
        {
            if (!File.Exists(metaPath)) return null;
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(metaPath));
            if (meta == null) return null;

            return meta.TryGetValue(is32Bit ? "InstalledVersion32" : "InstalledVersion64", out var value)
                ? value.GetString()?.TrimStart('v', 'V')
                : null;
        }
        catch
        {
            return null;
        }
    }
}

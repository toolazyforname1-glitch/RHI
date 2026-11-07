using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class UpdateAllCommand
{
    private static readonly string[] DefaultComponents =
    {
        "reshade", "renodx", "luma", "dxvk", "optiscaler", "reframework", "dofix",
    };

    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        const string command = "update.all";

        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                "Update All rewrites game files. Re-run with --yes (or --dry-run to preview).", ExitCodes.Usage);

        var components = ParseComponents(args.Get("components"));
        var library = sp.GetRequiredService<IGameLibraryService>();
        var lib = LibraryStore.Load(library);
        var games = LibraryStore.All(library, lib).ToList();

        if (!string.IsNullOrWhiteSpace(args.Get("game")))
        {
            games = games.Where(g => g.Name.Equals(args.Get("game"), StringComparison.OrdinalIgnoreCase)).ToList();
            if (games.Count == 0)
                return CliOutput.Fail(command, "GAME_NOT_FOUND", $"No game named '{args.Get("game")}'.", ExitCodes.NotFound);
        }

        var results = new List<UpdateResult>();
        foreach (var game in games)
        {
            if (CliGuards.GameIsRunning(new GameContext
                {
                    Name = game.Name,
                    Path = game.InstallPath,
                    Store = game.Source ?? "",
                    Engine = "",
                    Api = GraphicsApiType.Unknown,
                    AllApis = new HashSet<GraphicsApiType>(),
                    Is32Bit = false,
                    Exe = ExeLocator.Find(game.InstallPath),
                }, out _))
            {
                results.Add(new UpdateResult(game.Name, "(skip)", "game running", 0));
                continue;
            }

            var installed = InstalledComponents(sp, game);
            foreach (var component in components)
            {
                if (!installed.Contains(component)) continue;

                var (exitCode, output) = RunSelf(component, game.InstallPath, args.DryRun);
                results.Add(new UpdateResult(game.Name, component, output, exitCode));
            }
        }

        var failed = results.Count(r => r.ExitCode != 0 && r.Component != "(skip)");
        var skipped = results.Count(r => r.Component == "(skip)");
        var ran = results.Count(r => r.Component != "(skip)");

        CliOutput.Human(() =>
        {
            CliOutput.Table(new[] { "GAME", "COMPONENT", "EXIT", "DETAIL" },
                results.Select(r => new[]
                {
                    r.Game,
                    r.Component,
                    r.ExitCode.ToString(),
                    r.Detail.Length > 80 ? r.Detail[..80] + "..." : r.Detail,
                }));

            Console.WriteLine();
            Console.WriteLine($"{ran} update(s) attempted, {failed} failed, {skipped} game(s) skipped (running).");
        });

        return CliOutput.Ok(command, new
        {
            dryRun = args.DryRun,
            games = games.Count,
            attempted = ran,
            failed,
            skipped,
            components,
            results = results.Select(r => new
            {
                game = r.Game,
                component = r.Component,
                exitCode = r.ExitCode,
                detail = r.Detail,
            }),
        });
    }

    private static HashSet<string> InstalledComponents(IServiceProvider sp, DetectedGame game)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var aux = sp.GetRequiredService<IAuxInstallService>();
            foreach (var record in aux.LoadAll().Where(r => LibraryStore.NormalizePath(r.InstallPath) == LibraryStore.NormalizePath(game.InstallPath)))
            {
                if (record.AddonType.Equals("ReShade", StringComparison.OrdinalIgnoreCase) ||
                    record.AddonType.Equals("ReShadeNormal", StringComparison.OrdinalIgnoreCase))
                    installed.Add("reshade");
                if (record.AddonType.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase))
                    installed.Add("optiscaler");
            }
        }
        catch (Exception ex) { CliOutput.Detail($"aux records unavailable: {ex.Message}"); }

        try
        {
            var mods = sp.GetRequiredService<IModInstallService>();
            if (mods.LoadAll().Any(r => LibraryStore.NormalizePath(r.InstallPath) == LibraryStore.NormalizePath(game.InstallPath)))
                installed.Add("renodx");
        }
        catch (Exception ex) { CliOutput.Detail($"mod records unavailable: {ex.Message}"); }

        try
        {
            if (LumaService.GetRecordByPath(game.InstallPath) != null) installed.Add("luma");
        }
        catch (Exception ex) { CliOutput.Detail($"luma records unavailable: {ex.Message}"); }

        try
        {
            var dxvk = sp.GetRequiredService<IDxvkService>();
            if (dxvk.LoadAllRecords().Any(r => LibraryStore.NormalizePath(r.InstallPath) == LibraryStore.NormalizePath(game.InstallPath)))
                installed.Add("dxvk");
        }
        catch (Exception ex) { CliOutput.Detail($"dxvk records unavailable: {ex.Message}"); }

        try
        {
            var reff = sp.GetRequiredService<IREFrameworkService>();
            if (reff.GetRecords().Any(r => LibraryStore.NormalizePath(r.InstallPath) == LibraryStore.NormalizePath(game.InstallPath)))
                installed.Add("reframework");
        }
        catch (Exception ex) { CliOutput.Detail($"reframework records unavailable: {ex.Message}"); }

        try
        {
            var dof = sp.GetRequiredService<DofFixService>();
            if (dof.IsInstalledIn(game.InstallPath)) installed.Add("dofix");
        }
        catch (Exception ex) { CliOutput.Detail($"dofix probe unavailable: {ex.Message}"); }

        return installed;
    }

    private static (int ExitCode, string Output) RunSelf(string component, string installPath, bool dryRun)
    {
        var argv = component switch
        {
            "reshade" => new[] { "reshade", "update" },
            "renodx" => new[] { "renodx", "update" },
            "luma" => new[] { "luma", "install" },
            "dxvk" => new[] { "dxvk", "install" },
            "optiscaler" => new[] { "optiscaler", "update" },
            "reframework" => new[] { "reframework", "install" },
            "dofix" => new[] { "dofix", "install" },
            _ => Array.Empty<string>(),
        };

        if (argv.Length == 0) return (2, "unknown component");

        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in argv) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--path");
            psi.ArgumentList.Add(installPath);
            psi.ArgumentList.Add("--json");
            psi.ArgumentList.Add(dryRun ? "--dry-run" : "--yes");

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(600_000);

            var detail = ExtractError(stdout) ?? (string.IsNullOrWhiteSpace(stderr) ? "ok" : stderr.Trim().Split('\n')[0]);
            return (process.ExitCode, detail);
        }
        catch (Exception ex)
        {
            return (7, ex.Message);
        }
    }

    private static string? ExtractError(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False &&
                root.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("version", out var version))
            {
                return "v" + (version.GetString() ?? "?");
            }
            if (root.TryGetProperty("data", out data) &&
                data.TryGetProperty("updated", out var updated))
            {
                return updated.ValueKind == JsonValueKind.True ? "updated" : "up to date";
            }
        }
        catch
        {
            // not JSON
        }
        return null;
    }

    private static string[] ParseComponents(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DefaultComponents;

        var requested = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToLowerInvariant())
            .ToArray();

        var valid = DefaultComponents.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return requested.Where(valid.Contains).ToArray();
    }

    private sealed record UpdateResult(string Game, string Component, string Detail, int ExitCode);
}

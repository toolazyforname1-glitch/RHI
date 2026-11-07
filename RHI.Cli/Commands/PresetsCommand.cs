using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class PresetsCommand
{
    private static string PresetsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "inis", "reshade-presets");

    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"preset.{args.Verb}";

        switch (args.Verb)
        {
            case "list":
            {
                var files = ListPresets();
                CliOutput.Human(() =>
                {
                    if (files.Count == 0)
                    {
                        Console.WriteLine($"No presets in {PresetsDir}");
                        return;
                    }
                    CliOutput.Table(new[] { "PRESET", "SIZE" },
                        files.Select(f => new[] { f.Name, new FileInfo(f.FullName).Length.ToString() }));
                });
                return CliOutput.Ok(command, new
                {
                    directory = PresetsDir,
                    count = files.Count,
                    presets = files.Select(f => f.Name),
                });
            }

            case "deploy":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return CliOutput.Fail(command, code, message,
                        code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Deploying copies preset files into the game folder. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                var requested = args.Get("file") ?? args.Get("preset") ?? args.Positional.FirstOrDefault();
                List<FileInfo> selected;

                if (string.IsNullOrWhiteSpace(requested) || requested.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    selected = ListPresets();
                }
                else
                {
                    selected = requested
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(name => new FileInfo(Path.Combine(PresetsDir, name)))
                        .Where(f => f.Exists)
                        .ToList();
                }

                if (selected.Count == 0)
                    return CliOutput.Fail(command, "NO_PRESETS",
                        $"No matching preset files in {PresetsDir}", ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing copied.");
                        foreach (var f in selected)
                            Console.WriteLine($"  {f.Name} -> {ctx.Path}");
                    });
                    return CliOutput.Ok(command, new
                    {
                        dryRun = true,
                        game = ctx.Name,
                        presets = selected.Select(f => f.Name),
                    });
                }

                var deployed = new List<string>();
                foreach (var file in selected)
                {
                    try
                    {
                        File.Copy(file.FullName, Path.Combine(ctx.Path, file.Name), overwrite: true);
                        deployed.Add(file.Name);
                    }
                    catch (Exception ex)
                    {
                        CliOutput.Warn($"could not copy {file.Name}: {ex.Message}");
                    }
                }

                var shaderPacksUsed = new List<string>();
                var unresolved = new List<string>();

                if (args.Has("with-shaders") && deployed.Count > 0)
                {
                    try
                    {
                        var fxFiles = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var file in selected)
                        {
                            foreach (var line in File.ReadLines(file.FullName))
                            {
                                var trimmed = line.Trim();
                                if (!trimmed.StartsWith("Techniques", StringComparison.OrdinalIgnoreCase)) continue;
                                var eq = trimmed.IndexOf('=');
                                if (eq < 0) continue;
                                fxFiles.UnionWith(TechniquesParser.ExtractFxFiles(trimmed[(eq + 1)..]));
                            }
                        }

                        if (fxFiles.Count > 0)
                        {
                            var packs = sp.GetRequiredService<IShaderPackService>();
                            var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

                            foreach (var pack in packs.AvailablePacks)
                            {
                                try
                                {
                                    var files = packs.GetPackShaderFiles(new[] { pack.Id });
                                    if (files.Count > 0) map[pack.Id] = files;
                                }
                                catch
                                {
                                    // pack not extracted yet
                                }
                            }

                            var (matched, unresolvedSet) = ShaderResolver.Resolve(fxFiles, map);
                            var expanded = packs.ExpandPackDependencies(matched).ToList();

                            if (expanded.Count > 0)
                            {
                                await packs.EnsurePacksAsync(expanded);
                                var exclusions = expanded.ToDictionary(
                                    id => id, id => packs.GetExcludedFiles(id), StringComparer.OrdinalIgnoreCase);
                                packs.SyncGameFolder(ctx.Path, expanded, exclusions);
                                shaderPacksUsed.AddRange(expanded);
                            }

                            unresolved.AddRange(unresolvedSet);
                        }
                    }
                    catch (Exception ex)
                    {
                        CliOutput.Warn($"shader resolution failed: {ex.Message}");
                    }
                }

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"Deployed {deployed.Count} preset(s) to '{ctx.Name}'.");
                    if (args.Has("with-shaders"))
                    {
                        Console.WriteLine(shaderPacksUsed.Count == 0
                            ? "  shaders: no matching packs found"
                            : $"  shaders: {string.Join(", ", shaderPacksUsed)}");
                        if (unresolved.Count > 0)
                            Console.WriteLine($"  unresolved: {string.Join(", ", unresolved)}");
                    }
                });

                return CliOutput.Ok(command, new
                {
                    deployed = deployed.Count,
                    presets = deployed,
                    path = ctx.Path,
                    shaderPacks = shaderPacksUsed,
                    unresolved,
                });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown preset verb. Try: list, deploy.", ExitCodes.Usage);
        }
    }

    private static List<FileInfo> ListPresets()
    {
        try
        {
            if (!Directory.Exists(PresetsDir)) return new List<FileInfo>();
            return new DirectoryInfo(PresetsDir)
                .GetFiles("*.ini", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<FileInfo>();
        }
    }
}

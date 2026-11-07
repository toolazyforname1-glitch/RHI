using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

internal static class ShadersCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"shaders.{args.Verb}";
        var packs = sp.GetRequiredService<IShaderPackService>();

        switch (args.Verb)
        {
            case "list":
            {
                var rows = packs.AvailablePacks
                    .OrderBy(p => p.Category)
                    .ThenBy(p => p.DisplayName)
                    .Select(p => new
                    {
                        id = p.Id,
                        name = p.DisplayName,
                        category = p.Category.ToString(),
                        cached = packs.IsPackCached(p.Id),
                    })
                    .ToList();

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"{rows.Count} shader pack(s):");
                    CliOutput.Table(new[] { "ID", "NAME", "CATEGORY", "CACHED" },
                        rows.Select(r => new[] { r.id, r.name, r.category, r.cached ? "yes" : "no" }));
                });

                return CliOutput.Ok(command, new { count = rows.Count, packs = rows });
            }

            case "install":
            case "remove":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Shader deployment writes into the game folder. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                CliState.EnsureInitialized(sp);
                var settings = sp.GetRequiredService<SettingsViewModel>();

                var selected = args.Verb == "remove"
                    ? new List<string>()
                    : Resolve(args, settings);

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing written.");
                        Console.WriteLine($"  game:  {ctx.Name}");
                        Console.WriteLine($"  path:  {ctx.Path}");
                        Console.WriteLine($"  packs: {(selected.Count == 0 ? "(none - removes managed shaders)" : string.Join(", ", selected))}");
                    });
                    return CliOutput.Ok(command, new { dryRun = true, game = ctx.Name, packs = selected });
                }

                if (selected.Count > 0)
                {
                    await packs.EnsurePacksAsync(selected, new CliTextProgress());
                    var exclusions = await Task.Run(() => selected
                        .ToDictionary(id => id, id => packs.GetExcludedFiles(id), StringComparer.OrdinalIgnoreCase));
                    packs.SyncGameFolder(ctx.Path, selected, exclusions);
                }
                else
                {
                    packs.SyncGameFolder(ctx.Path, Array.Empty<string>());
                }

                CliOutput.Human(() => Console.WriteLine(selected.Count == 0
                    ? $"Managed shaders removed from '{ctx.Name}'."
                    : $"Deployed {selected.Count} shader pack(s) to '{ctx.Name}'."));

                return CliOutput.Ok(command, new
                {
                    removed = selected.Count == 0,
                    packs = selected,
                    path = ctx.Path,
                });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown shaders verb. Try: list, install, remove.", ExitCodes.Usage);
        }
    }

    private static List<string> Resolve(Args args, SettingsViewModel settings)
    {
        var explicitPacks = args.Get("packs");
        if (!string.IsNullOrWhiteSpace(explicitPacks))
            return explicitPacks.ParsePacks();

        if (string.Equals(args.Get("shaders"), "off", StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        if (settings.GlobalShadersOff) return new List<string>();
        if (settings.UseCustomShaders) return new List<string> { ShaderPackService.CustomShaderSentinel };
        return settings.SelectedShaderPacks.ToList();
    }

    private static List<string> ParsePacks(this string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

internal static class AddonsCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"addons.{args.Verb}";
        var packs = new AddonPackService(sp.GetRequiredService<HttpClient>());

        try { await packs.EnsureLatestAsync(); }
        catch (Exception ex) { CliOutput.Warn($"addon catalogue refresh failed: {ex.Message}"); }

        switch (args.Verb)
        {
            case "list":
            {
                var rows = packs.AvailablePacks
                    .Where(p => !p.HideFromPicker)
                    .OrderBy(p => p.SectionId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.PackageName, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new
                    {
                        section = p.SectionId,
                        package = p.PackageName,
                        description = p.PackageDescription,
                        downloaded = packs.IsDownloaded(p.PackageName),
                        version = packs.GetVersionLabel(p.SectionId),
                    })
                    .ToList();

                CliOutput.Human(() =>
                {
                    CliOutput.Table(new[] { "SECTION", "PACKAGE", "VERSION", "CACHED" },
                        rows.Select(r => new[]
                        {
                            r.section,
                            r.package,
                            r.version ?? "",
                            r.downloaded ? "yes" : "no",
                        }));
                });

                return CliOutput.Ok(command, new { count = rows.Count, addons = rows });
            }

            case "install":
            case "remove":
            {
                if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
                    return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

                var name = args.Get("name") ?? args.Get("package") ?? args.Get("section");
                if (string.IsNullOrWhiteSpace(name))
                    return CliOutput.Fail(command, "MISSING_ARG",
                        "--name <package> (or --section <id>) is required.", ExitCodes.Usage);

                var entry = packs.AvailablePacks.FirstOrDefault(p =>
                    p.PackageName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    p.SectionId.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                    return CliOutput.Fail(command, "ADDON_NOT_FOUND",
                        $"No addon named '{name}'. Run 'rhictl addons list'.", ExitCodes.NotFound);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the game's ReShade addon set. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                CliState.EnsureInitialized(sp);
                var settings = sp.GetRequiredService<SettingsViewModel>();
                var names = sp.GetRequiredService<IGameNameService>();

                var selectMode = names.PerGameAddonMode.TryGetValue(ctx.Name, out var mode) &&
                                 mode.Equals("Select", StringComparison.OrdinalIgnoreCase);

                var selection = selectMode && names.PerGameAddonSelection.TryGetValue(ctx.Name, out var perGame)
                    ? perGame.ToList()
                    : settings.EnabledGlobalAddons.ToList();

                if (args.Verb == "install")
                {
                    if (!selection.Contains(entry.PackageName, StringComparer.OrdinalIgnoreCase))
                        selection.Add(entry.PackageName);
                }
                else
                {
                    selection.RemoveAll(x => x.Equals(entry.PackageName, StringComparison.OrdinalIgnoreCase));
                }

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine($"Dry run - nothing downloaded or deployed.");
                        Console.WriteLine($"  game:   {ctx.Name}");
                        Console.WriteLine($"  addon:  {entry.PackageName} ({entry.SectionId})");
                        Console.WriteLine($"  action: {args.Verb}");
                        Console.WriteLine($"  mode:   {(selectMode ? "Select" : "Global")}");
                    });
                    return CliOutput.Ok(command, new { dryRun = true, addon = entry.PackageName, action = args.Verb, selection });
                }

                if (args.Verb == "install")
                    await packs.DownloadAddonAsync(entry, new CliProgress(!CliOutput.Json));

                if (selectMode)
                {
                    names.PerGameAddonSelection[ctx.Name] = selection;
                    names.PerGameAddonMode[ctx.Name] = "Select";
                }
                else
                {
                    settings.EnabledGlobalAddons = selection;
                }

                packs.DeployAddonsForGame(ctx.Name, ctx.Path, ctx.Is32Bit,
                    useGlobalSet: !selectMode,
                    perGameSelection: selection);

                CliState.Save(sp);

                CliOutput.Human(() => Console.WriteLine(args.Verb == "install"
                    ? $"Addon '{entry.PackageName}' installed for '{ctx.Name}'."
                    : $"Addon '{entry.PackageName}' removed from '{ctx.Name}'."));

                return CliOutput.Ok(command, new
                {
                    addon = entry.PackageName,
                    section = entry.SectionId,
                    action = args.Verb,
                    mode = selectMode ? "Select" : "Global",
                    selection,
                });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown addons verb. Try: list, install, remove.", ExitCodes.Usage);
        }
    }
}

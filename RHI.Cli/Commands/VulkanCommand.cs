using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class VulkanCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"vulkan.{args.Verb}";

        switch (args.Verb)
        {
            case "status":
            {
                var installed = VulkanLayerService.IsLayerInstalled();
                var version = installed
                    ? AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName)
                    : null;

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"Vulkan ReShade layer: {(installed ? "installed" : "not installed")}");
                    Console.WriteLine($"  directory: {VulkanLayerService.LayerDirectory}");
                    Console.WriteLine($"  version:   {version ?? "-"}");
                    Console.WriteLine($"  elevated:  {(IsAdmin() ? "yes" : "no")}");
                });

                return CliOutput.Ok(command, new
                {
                    installed,
                    directory = VulkanLayerService.LayerDirectory,
                    version,
                    elevated = IsAdmin(),
                });
            }

            case "install":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Installing the Vulkan layer writes to C:\\ProgramData and HKLM. Re-run with --yes.", ExitCodes.Usage);

                if (!IsAdmin())
                    return CliOutput.Fail(command, "ELEVATION_REQUIRED",
                        "The Vulkan implicit layer needs administrator rights. Run rhictl from an elevated terminal.",
                        ExitCodes.Permission);

                var layerOnly = args.Has("layer-only");
                GameContext? ctx = null;

                if (!layerOnly || args.Get("game") != null || args.Get("path") != null)
                {
                    if (!GameResolver.TryResolve(args, sp, out var resolved, out var code, out var message))
                        return CliOutput.Fail(command, code, message, ExitCodes.Usage);
                    ctx = resolved;
                }

                if (args.DryRun)
                {
                    CliOutput.Human(() =>
                    {
                        Console.WriteLine("Dry run - nothing written.");
                        Console.WriteLine($"  would install Vulkan ReShade layer in {VulkanLayerService.LayerDirectory}");
                        if (ctx != null)
                        {
                            Console.WriteLine($"  would merge reshade.ini for: {ctx.Name}");
                            Console.WriteLine($"  would create Vulkan footprint in: {ctx.Path}");
                        }
                    });
                    return CliOutput.Ok(command, new { dryRun = true, layerOnly, game = ctx?.Name });
                }

                var reshade = sp.GetRequiredService<IReShadeUpdateService>();
                await reshade.EnsureLatestAsync(new CliProgress(!CliOutput.Json));

                VulkanLayerService.InstallLayer();

                if (ctx != null)
                {
                    var settings = sp.GetRequiredService<RenoDXCommander.ViewModels.SettingsViewModel>();
                    CliState.EnsureInitialized(sp);

                    AuxInstallService.MergeRsVulkanIni(
                        ctx.Path,
                        ctx.Name,
                        ScreenshotPath(settings, ctx.Name),
                        settings.OverlayHotkey,
                        settings.ScreenshotHotkey);

                    VulkanFootprintService.Create(ctx.Path);
                }

                CliOutput.Human(() => Console.WriteLine(ctx == null
                    ? "Vulkan ReShade layer installed."
                    : $"Vulkan ReShade layer installed; '{ctx.Name}' prepared for Vulkan ReShade."));

                return CliOutput.Ok(command, new
                {
                    installed = true,
                    game = ctx?.Name,
                    layer = VulkanLayerService.LayerDirectory,
                });
            }

            case "uninstall":
            {
                var layerOnly = args.Has("layer-only");
                GameContext? target = null;

                if (!layerOnly || args.Get("game") != null || args.Get("path") != null)
                {
                    if (!GameResolver.TryResolve(args, sp, out var resolved, out var code, out var message))
                        return CliOutput.Fail(command, code, message, ExitCodes.Usage);
                    target = resolved;
                }

                if (target == null && !layerOnly)
                    return CliOutput.Fail(command, "MISSING_ARG",
                        "Use --game/--path for per-game cleanup, or --layer-only for the global layer.",
                        ExitCodes.Usage);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Removing Vulkan ReShade deletes files. Re-run with --yes.", ExitCodes.Usage);

                if (layerOnly)
                {
                    if (!IsAdmin())
                        return CliOutput.Fail(command, "ELEVATION_REQUIRED",
                            "Removing the Vulkan implicit layer needs administrator rights.", ExitCodes.Permission);

                    if (args.DryRun)
                    {
                        CliOutput.Human(() => Console.WriteLine(
                            $"Would remove {VulkanLayerService.LayerDirectory}ReShade64.dll/.json and the HKLM layer entry."));
                    }
                    else
                    {
                        VulkanLayerService.UninstallLayer();
                    }
                }

                if (target != null)
                {
                    if (CliGuards.CheckMutation(args, target, out var guard))
                        return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                    if (args.DryRun)
                    {
                        CliOutput.Human(() => Console.WriteLine(
                            $"Would remove Vulkan ReShade from '{target.Name}' (reshade.ini, footprint, managed shaders/addons)."));
                    }
                    else
                    {
                        var iniPath = Path.Combine(target.Path, "reshade.ini");
                        if (File.Exists(iniPath)) File.Delete(iniPath);

                        VulkanFootprintService.Delete(target.Path);

                        var shaderPacks = sp.GetRequiredService<IShaderPackService>();
                        shaderPacks.RemoveFromGameFolder(target.Path);
                        shaderPacks.RestoreOriginalIfPresent(target.Path);

                        var addons = new AddonPackService(sp.GetRequiredService<HttpClient>());
                        addons.DeployAddonsForGame(target.Name, target.Path, target.Is32Bit,
                            useGlobalSet: true, perGameSelection: new List<string>());
                    }
                }

                if (!args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(target == null
                        ? "Vulkan ReShade layer removed."
                        : layerOnly
                            ? $"Vulkan layer removed and '{target.Name}' cleaned."
                            : $"Vulkan ReShade removed from '{target.Name}'."));
                }

                return CliOutput.Ok(command, new { removed = !args.DryRun, layer = layerOnly, game = target?.Name });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown vulkan verb. Try: status, install, uninstall.", ExitCodes.Usage);
        }
    }

    private static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string? ScreenshotPath(RenoDXCommander.ViewModels.SettingsViewModel settings, string gameName)
    {
        var basePath = settings.ScreenshotPath;
        if (string.IsNullOrEmpty(basePath)) return null;
        if (!settings.PerGameScreenshotFolders) return basePath;

        var sanitized = AuxInstallService.SanitizeDirectoryName(gameName);
        return string.IsNullOrEmpty(sanitized) ? basePath : basePath + @"\" + sanitized;
    }
}

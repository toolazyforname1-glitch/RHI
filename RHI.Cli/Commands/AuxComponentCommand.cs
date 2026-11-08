using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli.Commands;

internal static class AuxComponentCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"{args.Group}.{args.Verb}";

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        if (args.Verb is not ("install" or "uninstall" or "update"))
            return CliOutput.Fail(command, "UNKNOWN_VERB",
                $"Unknown verb '{args.Verb}'. Try: install, uninstall, update.", ExitCodes.Usage);

        if (!args.Yes && !args.DryRun)
            return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                $"This writes into the game folder. Re-run with --yes (or --dry-run).", ExitCodes.Usage);

        if (CliGuards.CheckMutation(args, ctx, out var guard))
            return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

        CliState.EnsureInitialized(sp);
        var settings = sp.GetRequiredService<SettingsViewModel>();
        var progress = new CliProgress(!CliOutput.Json);

        try
        {
            return args.Group switch
            {
                "reframework" => await RefFrameworkAsync(args, sp, ctx, command, progress),
                "dofix" => await DofFixAsync(args, sp, ctx, command, progress),
                "dxvk" => await DxvkAsync(args, sp, ctx, command, progress, settings),
                "optiscaler" => await OptiScalerAsync(args, sp, ctx, command, progress, settings),
                _ => CliOutput.Fail(command, "UNKNOWN_COMMAND", $"Unknown component '{args.Group}'.", ExitCodes.Usage),
            };
        }
        catch (Exception ex)
        {
            return CliOutput.Fail(command, "FAILED", ex.Message, ExitCodes.Failed);
        }
    }

    private static async Task<int> RefFrameworkAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var reff = sp.GetRequiredService<IREFrameworkService>();

        if (args.DryRun)
            return DryRun(command, ctx, "RE Framework", args.Verb);

        if (args.Verb == "uninstall")
        {
            reff.Uninstall(ctx.Name, ctx.Path);
            CliOutput.Human(() => Console.WriteLine($"RE Framework removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true });
        }

        var record = await reff.InstallAsync(ctx.Name, ctx.Path, progress, ctx.Store);
        CliOutput.Human(() => Console.WriteLine($"RE Framework installed for '{ctx.Name}' (v{record.InstalledVersion})."));
        return CliOutput.Ok(command, new
        {
            installed = true,
            version = record.InstalledVersion,
            path = record.InstallPath,
        });
    }

    private static async Task<int> DofFixAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress)
    {
        var dof = sp.GetRequiredService<DofFixService>();

        if (args.Verb == "uninstall")
        {
            if (args.DryRun) return DryRun(command, ctx, "DOF Fix", args.Verb);
            var removed = dof.Uninstall(ctx.Path);
            CliOutput.Human(() => Console.WriteLine(removed
                ? $"DOF Fix removed from '{ctx.Name}'."
                : $"DOF Fix was not installed in '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed });
        }

        if (!dof.IsGameEligible(ctx.EngineHint, ctx.Is32Bit, ctx.Name))
            return CliOutput.Fail(command, "NOT_ELIGIBLE",
                $"DOF Fix does not apply to '{ctx.Name}' (engine={ctx.EngineHint}, 32-bit={ctx.Is32Bit}).", ExitCodes.Usage);

        if (args.DryRun) return DryRun(command, ctx, "DOF Fix", args.Verb);

        await dof.EnsureStagingAsync(progress);
        var ok = await dof.InstallAsync(ctx.Path, progress);

        CliOutput.Human(() => Console.WriteLine(ok
            ? $"DOF Fix installed for '{ctx.Name}'."
            : $"DOF Fix install reported no change for '{ctx.Name}'."));
        return CliOutput.Ok(command, new { installed = ok });
    }

    private static async Task<int> DxvkAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress, SettingsViewModel settings)
    {
        var dxvk = sp.GetRequiredService<IDxvkService>();
        var card = BuildCard(ctx);

        if (args.Get("variant") is { } variant)
        {
            switch (variant.ToLowerInvariant())
            {
                case "development": case "dev": dxvk.SelectedVariant = DxvkVariant.Development; break;
                case "stable": dxvk.SelectedVariant = DxvkVariant.Stable; break;
                case "lilium": case "liliumhdr": case "hdr": dxvk.SelectedVariant = DxvkVariant.LiliumHdr; break;
                default:
                    return CliOutput.Fail(command, "BAD_VARIANT",
                        $"Unknown DXVK variant '{variant}'. Use development, stable or lilium.", ExitCodes.Usage);
            }
        }

        if (args.DryRun)
            return DryRun(command, ctx, $"DXVK ({dxvk.SelectedVariant})", args.Verb);

        if (args.Verb == "update")
        {
            if (args.DryRun) return DryRun(command, ctx, "DXVK", args.Verb);

            await dxvk.EnsureStagingAsync(progress);
            await dxvk.UpdateAsync(card, progress);

            CliOutput.Human(() => Console.WriteLine($"DXVK updated for '{ctx.Name}'."));
            return CliOutput.Ok(command, new { updated = true, variant = dxvk.SelectedVariant.ToString() });
        }

        if (args.Verb == "uninstall")
        {
            await dxvk.UninstallAsync(card);
            CliOutput.Human(() => Console.WriteLine($"DXVK removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true });
        }

        await dxvk.EnsureStagingAsync(progress);
        await dxvk.InstallAsync(card, progress, ScreenshotPath(settings, ctx.Name),
            settings.OverlayHotkey, settings.ScreenshotHotkey);

        CliOutput.Human(() => Console.WriteLine($"DXVK ({dxvk.SelectedVariant}) installed for '{ctx.Name}'."));
        return CliOutput.Ok(command, new { installed = true, variant = dxvk.SelectedVariant.ToString() });
    }

    private static async Task<int> OptiScalerAsync(
        Args args, IServiceProvider sp, GameContext ctx, string command,
        IProgress<(string message, double percent)> progress, SettingsViewModel settings)
    {
        var os = sp.GetRequiredService<IOptiScalerService>();
        var card = BuildCard(ctx);

        var gpu = (args.Get("gpu") ?? "NVIDIA").ToUpperInvariant() switch
        {
            "AMD" => "AMD",
            "INTEL" => "Intel",
            _ => "NVIDIA",
        };

        var variant = (args.Get("variant") ?? "stable").ToLowerInvariant() switch
        {
            "nightly" => "Nightly",
            "dlssnr" => "DlssNr",
            "f5" => "F5",
            _ => "Stable",
        };

        if (args.DryRun)
            return DryRun(command, ctx, $"OptiScaler ({variant}, {gpu})", args.Verb);

        if (args.Verb == "update")
        {
            if (args.DryRun) return DryRun(command, ctx, $"OptiScaler ({variant})", args.Verb);

            if (variant == "Nightly")
                await os.EnsureNightlyStagingAsync(progress);
            else
                await os.EnsureStagingAsync(progress);

            await os.UpdateAsync(card, progress, variantHint: variant);

            CliOutput.Human(() => Console.WriteLine($"OptiScaler updated for '{ctx.Name}'."));
            return CliOutput.Ok(command, new { updated = true, variant, gpu });
        }

        if (args.Verb == "uninstall")
        {
            os.Uninstall(card);
            CliOutput.Human(() => Console.WriteLine($"OptiScaler removed from '{ctx.Name}'."));
            return CliOutput.Ok(command, new { removed = true });
        }

        if (variant == "Nightly")
            await os.EnsureNightlyStagingAsync(progress);
        else
            await os.EnsureStagingAsync(progress);

        var record = await os.InstallAsync(card, progress, gpu, true,
            settings.OverlayHotkey, variant, args.Get("build"));

        CliOutput.Human(() => Console.WriteLine(record == null
            ? "OptiScaler install reported no change."
            : $"OptiScaler installed for '{ctx.Name}' as {record.InstalledAs}."));

        return CliOutput.Ok(command, new
        {
            installed = record != null,
            installedAs = record?.InstalledAs,
            variant,
            gpu,
        });
    }

    private static GameCardViewModel BuildCard(GameContext ctx) => new()
    {
        GameName = ctx.Name,
        InstallPath = ctx.Path,
        Source = ctx.Store,
        Is32Bit = ctx.Is32Bit,
        GraphicsApi = ctx.Api,
        DetectedApis = ctx.AllApis,
        DispatcherQueue = DispatcherQueue.Instance,
        VulkanRenderingPath = ctx.IsVulkanOnly ? "Vulkan" : "DirectX",
    };

    private static int DryRun(string command, GameContext ctx, string component, string verb)
    {
        CliOutput.Human(() =>
        {
            Console.WriteLine($"Dry run - nothing written.");
            Console.WriteLine($"  would {verb}: {component}");
            Console.WriteLine($"  game:        {ctx.Name}");
            Console.WriteLine($"  path:        {ctx.Path}");
        });
        return CliOutput.Ok(command, new
        {
            dryRun = true,
            action = verb,
            component,
            game = ctx.Name,
            path = ctx.Path,
        });
    }

    private static string? ScreenshotPath(SettingsViewModel settings, string gameName)
    {
        var basePath = settings.ScreenshotPath;
        if (string.IsNullOrEmpty(basePath)) return null;
        if (!settings.PerGameScreenshotFolders) return basePath;

        var sanitized = AuxInstallService.SanitizeDirectoryName(gameName);
        return string.IsNullOrEmpty(sanitized) ? basePath : basePath + @"\" + sanitized;
    }
}

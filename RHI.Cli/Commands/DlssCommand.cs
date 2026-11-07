using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class DlssCommand
{
    private static readonly string[] Components = { "sr", "rr", "fg", "nr", "streamline" };

    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"dlss.{args.Verb}";

        if (args.Verb == "scales")
        {
            var options = DlssPresetService.RenderScaleOptions;
            CliOutput.Human(() => CliOutput.Table(new[] { "NAME", "VALUE" },
                options.Select(o => new[] { o.Name, o.Value.ToString() })));
            return CliOutput.Ok(command, new
            {
                options = options.Select(o => new { o.Name, o.Value }),
            });
        }

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        var dlss = sp.GetRequiredService<IDlssStreamlineService>();

        var detection = dlss.Detect(ctx.Path);

        switch (args.Verb)
        {
            case "status":
            {
                CliOutput.Human(() =>
                {
                    Console.WriteLine($"{ctx.Name} — DLSS / Streamline");
                    Console.WriteLine($"  sr:         {detection.DlssVersion ?? "(not found)"}  {detection.DlssPath}");
                    Console.WriteLine($"  rr:         {detection.DlssdVersion ?? "(not found)"}  {detection.DlssdPath}");
                    Console.WriteLine($"  fg:         {detection.DlssgVersion ?? "(not found)"}  {detection.DlssgPath}");
                    Console.WriteLine($"  nr:         {detection.DlssnrVersion ?? "(not found)"}  {detection.DlssnrPath}");
                    Console.WriteLine($"  streamline: {detection.StreamlineVersion ?? "(not found)"}  {detection.StreamlineFolder}");
                });

                return CliOutput.Ok(command, new
                {
                    game = ctx.Name,
                    sr = detection.DlssVersion,
                    rr = detection.DlssdVersion,
                    fg = detection.DlssgVersion,
                    nr = detection.DlssnrVersion,
                    streamline = detection.StreamlineVersion,
                    paths = new
                    {
                        sr = detection.DlssPath,
                        rr = detection.DlssdPath,
                        fg = detection.DlssgPath,
                        nr = detection.DlssnrPath,
                        streamline = detection.StreamlineFolder,
                    },
                });
            }

            case "versions":
            {
                var component = NormalizeComponent(args.Get("component") ?? "sr");
                if (component == null)
                    return CliOutput.Fail(command, "BAD_COMPONENT", ComponentHelp, ExitCodes.Usage);

                await dlss.FetchManifestAsync();
                var versions = VersionsFor(dlss, component);

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"{component} versions:");
                    foreach (var v in versions) Console.WriteLine($"  {v}");
                });

                return CliOutput.Ok(command, new { component, versions });
            }

            case "set":
            {
                var component = NormalizeComponent(args.Get("component") ?? "");
                var version = args.Get("version");

                if (component == null)
                    return CliOutput.Fail(command, "BAD_COMPONENT", ComponentHelp, ExitCodes.Usage);
                if (string.IsNullOrWhiteSpace(version))
                    return CliOutput.Fail(command, "MISSING_ARG", "--version <x> is required.", ExitCodes.Usage);

                await dlss.FetchManifestAsync();
                var versions = VersionsFor(dlss, component);
                if (versions.Count > 0 && !versions.Contains(version, StringComparer.OrdinalIgnoreCase))
                    CliOutput.Warn($"'{version}' is not in the known {component} list; passing it through anyway.");

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Swapping a DLL modifies the game folder. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                var target = TargetFor(detection, component);
                if (target == null)
                    return CliOutput.Fail(command, "NOT_FOUND",
                        $"No {component} DLL found in '{ctx.Name}'.", ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would swap {component} ({version}) at {target}."));
                    return CliOutput.Ok(command, new { dryRun = true, component, version, target });
                }

                await SwapAsync(dlss, component, target, version, ctx);

                CliOutput.Human(() => Console.WriteLine($"{component} set to {version} in '{ctx.Name}'."));
                return CliOutput.Ok(command, new { component, version, target });
            }

            case "restore":
            {
                var component = NormalizeComponent(args.Get("component") ?? "");

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "Restoring modifies the game folder. Re-run with --yes.", ExitCodes.Usage);

                if (CliGuards.CheckMutation(args, ctx, out var guard))
                    return CliOutput.Fail(command, "BLOCKED", guard, ExitCodes.GameRunning);

                if (component == null)
                {
                    if (args.DryRun)
                    {
                        CliOutput.Human(() => Console.WriteLine("Would restore every DLSS/Streamline backup."));
                        return CliOutput.Ok(command, new { dryRun = true, component = "all" });
                    }

                    dlss.RestoreAll(detection);
                    CliOutput.Human(() => Console.WriteLine($"All DLSS/Streamline files restored for '{ctx.Name}'."));
                    return CliOutput.Ok(command, new { restored = "all" });
                }

                var target = TargetFor(detection, component);
                if (target == null)
                    return CliOutput.Fail(command, "NOT_FOUND",
                        $"No {component} DLL found in '{ctx.Name}'.", ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine($"Would restore {component} at {target}."));
                    return CliOutput.Ok(command, new { dryRun = true, component, target });
                }

                if (component == "streamline") dlss.RestoreStreamline(target);
                else dlss.Restore(target);

                CliOutput.Human(() => Console.WriteLine($"{component} restored in '{ctx.Name}'."));
                return CliOutput.Ok(command, new { restored = component, target });
            }

            case "render-scale":
            {
                var presets = sp.GetRequiredService<DlssPresetService>();
                presets.Initialize();
                if (!presets.IsSupported)
                    return CliOutput.Fail(command, "NVAPI_UNAVAILABLE", "NVAPI is not available.", ExitCodes.Failed);

                var component = (args.Get("component") ?? "sr").ToLowerInvariant();
                if (component is not ("sr" or "rr"))
                    return CliOutput.Fail(command, "BAD_COMPONENT", "Use --component sr|rr.", ExitCodes.Usage);

                var value = args.GetInt("value");
                if (value == null)
                {
                    var current = component == "sr"
                        ? presets.GetSrRenderScale(ctx.Name, ctx.Path)
                        : presets.GetRrRenderScale(ctx.Name, ctx.Path);
                    CliOutput.Human(() => Console.WriteLine($"{component} render scale = {current}"));
                    return CliOutput.Ok(command, new { component, value = current });
                }

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the driver render-scale override. Re-run with --yes.", ExitCodes.Usage);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine($"Would set {component} render scale to {value}."));
                    return CliOutput.Ok(command, new { dryRun = true, component, value });
                }

                var ok = component == "sr"
                    ? presets.SetSrRenderScale(ctx.Name, ctx.Path, (uint)value)
                    : presets.SetRrRenderScale(ctx.Name, ctx.Path, (uint)value);

                if (!ok)
                    return CliOutput.Fail(command, "FAILED", "NVAPI rejected the render-scale change.", ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine($"{component} render scale set to {value}."));
                return CliOutput.Ok(command, new { component, value });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown dlss verb. Try: status, versions, set, restore, scales, render-scale.", ExitCodes.Usage);
        }
    }

    private const string ComponentHelp = "Use --component sr|rr|fg|nr|streamline.";

    private static string? NormalizeComponent(string value) => value.ToLowerInvariant() switch
    {
        "sr" or "dlss" or "superresolution" => "sr",
        "rr" or "dlssd" or "rayreconstruction" => "rr",
        "fg" or "dlssg" or "framegen" => "fg",
        "nr" or "dlssnr" or "neural" => "nr",
        "streamline" or "sl" => "streamline",
        _ => null,
    };

    private static List<string> VersionsFor(IDlssStreamlineService dlss, string component) => component switch
    {
        "sr" => dlss.DlssVersions.ToList(),
        "rr" => dlss.DlssdVersions.ToList(),
        "fg" => dlss.DlssgVersions.ToList(),
        "nr" => dlss.DlssnrVersions.ToList(),
        "streamline" => dlss.StreamlineVersions.ToList(),
        _ => new List<string>(),
    };

    private static string? TargetFor(DlssDetectionResult detection, string component) => component switch
    {
        "sr" => detection.DlssPath,
        "rr" => detection.DlssdPath,
        "fg" => detection.DlssgPath,
        "nr" => detection.DlssnrPath,
        "streamline" => detection.StreamlineFolder,
        _ => null,
    };

    private static Task SwapAsync(
        IDlssStreamlineService dlss, string component, string target, string version, GameContext ctx)
    {
        return component switch
        {
            "sr" => dlss.SwapDlssAsync(target, version),
            "rr" => dlss.SwapDlssdAsync(target, version),
            "fg" => dlss.SwapDlssgAsync(target, version),
            "nr" => dlss.SwapDlssnrAsync(target, version),
            "streamline" => dlss.SwapStreamlineAsync(target, version),
            _ => throw new InvalidOperationException($"Unsupported component '{component}'."),
        };
    }
}

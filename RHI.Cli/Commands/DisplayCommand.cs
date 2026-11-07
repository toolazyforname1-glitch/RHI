using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class DisplayCommand
{
    public static int Run(Args args, IServiceProvider sp)
    {
        var command = $"{args.Group}.{args.Verb}";

        switch (args.Group)
        {
            case "hdr":
                return Hdr(args, command);
            case "res":
                return Resolution(args, command);
            case "vibrance":
                return Vibrance(args, sp, command);
            default:
                return CliOutput.Fail(command, "UNKNOWN_COMMAND", "Unknown display command.", ExitCodes.Usage);
        }
    }

    private static int Hdr(Args args, string command)
    {
        switch (args.Verb)
        {
            case "status":
            {
                var enabled = HdrToggleService.IsHdrEnabled();
                CliOutput.Human(() => Console.WriteLine($"HDR: {(enabled ? "enabled" : "disabled")}"));
                return CliOutput.Ok(command, new { enabled });
            }

            case "on":
            case "off":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the system HDR state. Re-run with --yes.", ExitCodes.Usage);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine($"Would turn HDR {args.Verb}."));
                    return CliOutput.Ok(command, new { dryRun = true, target = args.Verb });
                }

                var ok = args.Verb == "on" ? HdrToggleService.EnableHdr() : HdrToggleService.DisableHdr();
                if (!ok)
                    return CliOutput.Fail(command, "FAILED", "Windows rejected the HDR state change.", ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine($"HDR turned {args.Verb}."));
                return CliOutput.Ok(command, new { enabled = args.Verb == "on" });
            }

            case "list":
            {
                var displays = HdrToggleService.GetAllDisplays();
                CliOutput.Human(() => CliOutput.Table(new[] { "ID", "NAME", "HDR SUPPORTED" },
                    displays.Select(d => new[] { d.TargetId.ToString(), d.Name, d.HdrSupported ? "yes" : "no" })));
                return CliOutput.Ok(command, new
                {
                    displays = displays.Select(d => new { id = d.TargetId, d.Name, hdrSupported = d.HdrSupported }),
                });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown hdr verb. Try: status, on, off, list.", ExitCodes.Usage);
        }
    }

    private static int Resolution(Args args, string command)
    {
        switch (args.Verb)
        {
            case "current":
            {
                var current = ResolutionToggleService.GetCurrentResolution();
                if (current == null)
                    return CliOutput.Fail(command, "NOT_AVAILABLE", "Could not read the current resolution.", ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine($"{current.Width}x{current.Height} @ {current.RefreshRate} Hz"));
                return CliOutput.Ok(command, new
                {
                    width = current.Width,
                    height = current.Height,
                    refreshRate = current.RefreshRate,
                });
            }

            case "list":
            {
                var modes = ResolutionToggleService.GetSupportedResolutions();
                CliOutput.Human(() => CliOutput.Table(new[] { "WIDTH", "HEIGHT", "HZ" },
                    modes.Select(m => new[] { m.Width.ToString(), m.Height.ToString(), m.RefreshRate.ToString() })));
                return CliOutput.Ok(command, new
                {
                    modes = modes.Select(m => new { m.Width, m.Height, m.RefreshRate }),
                });
            }

            case "set":
            {
                var width = (uint?)args.GetInt("width");
                var height = (uint?)args.GetInt("height");
                var hz = (uint?)args.GetInt("hz");

                if (width == null || height == null)
                    return CliOutput.Fail(command, "MISSING_ARG", "--width and --height are required.", ExitCodes.Usage);

                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the display mode. Re-run with --yes.", ExitCodes.Usage);

                var modes = ResolutionToggleService.GetSupportedResolutions();
                var match = modes.FirstOrDefault(m =>
                    m.Width == width && m.Height == height && (hz == null || m.RefreshRate == hz));

                if (match == null)
                    return CliOutput.Fail(command, "MODE_NOT_FOUND",
                        $"No supported mode for {width}x{height}{(hz == null ? "" : $"@{hz}")}.", ExitCodes.NotFound);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would switch to {match.Width}x{match.Height} @ {match.RefreshRate} Hz."));
                    return CliOutput.Ok(command, new { dryRun = true, mode = match });
                }

                var ok = ResolutionToggleService.SetResolution(match);
                if (!ok)
                    return CliOutput.Fail(command, "FAILED", "Windows rejected the mode change.", ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine($"Display set to {match.Width}x{match.Height} @ {match.RefreshRate} Hz."));
                return CliOutput.Ok(command, new { width = match.Width, height = match.Height, refreshRate = match.RefreshRate });
            }

            case "restore":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the display mode. Re-run with --yes.", ExitCodes.Usage);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine("Would restore the previous display mode."));
                    return CliOutput.Ok(command, new { dryRun = true });
                }

                var ok = ResolutionToggleService.RestoreResolution();
                CliOutput.Human(() => Console.WriteLine(ok ? "Previous display mode restored." : "No saved mode to restore."));
                return CliOutput.Ok(command, new { restored = ok });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown res verb. Try: current, list, set, restore.", ExitCodes.Usage);
        }
    }

    private static int Vibrance(Args args, IServiceProvider sp, string command)
    {
        if (!DigitalVibranceService.IsSupported)
            return CliOutput.Fail(command, "UNSUPPORTED", "Digital vibrance is not available on this GPU/driver.", ExitCodes.Failed);

        var displays = DigitalVibranceService.EnumerateDisplays();

        switch (args.Verb)
        {
            case "list":
            {
                CliOutput.Human(() => CliOutput.Table(new[] { "INDEX", "DISPLAY", "LEVEL" },
                    displays.Select(d => new[]
                    {
                        d.Index.ToString(), d.Name, SafeLevel(d.Index).ToString(),
                    })));
                return CliOutput.Ok(command, new
                {
                    displays = displays.Select(d => new { d.Index, d.Name, level = SafeLevel(d.Index) }),
                });
            }

            case "set":
            {
                var index = args.GetInt("display") ?? displays.FirstOrDefault().Index;
                var value = args.GetInt("value");

                if (value == null)
                    return CliOutput.Fail(command, "MISSING_ARG", "--value 0..100 is required.", ExitCodes.Usage);
                if (value is < 0 or > 100)
                    return CliOutput.Fail(command, "BAD_VALUE", "--value must be between 0 and 100.", ExitCodes.Usage);
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the display colour setting. Re-run with --yes.", ExitCodes.Usage);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine($"Would set vibrance to {value} on display {index}."));
                    return CliOutput.Ok(command, new { dryRun = true, display = index, value });
                }

                var ok = DigitalVibranceService.SetLevel(index, value.Value);
                if (!ok)
                    return CliOutput.Fail(command, "FAILED", "The driver rejected the vibrance change.", ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine($"Vibrance set to {value} on display {index}."));
                return CliOutput.Ok(command, new { display = index, value });
            }

            case "restore":
            {
                if (!args.Yes && !args.DryRun)
                    return CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
                        "This changes the display colour setting. Re-run with --yes.", ExitCodes.Usage);

                var settings = sp.GetRequiredService<RenoDXCommander.ViewModels.SettingsViewModel>();
                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would restore {settings.DigitalVibranceSettings.Count} saved vibrance value(s)."));
                    return CliOutput.Ok(command, new { dryRun = true, saved = settings.DigitalVibranceSettings.Count });
                }

                DigitalVibranceService.RestoreSavedLevels(settings.DigitalVibranceSettings);
                CliOutput.Human(() => Console.WriteLine("Saved vibrance levels restored."));
                return CliOutput.Ok(command, new { restored = settings.DigitalVibranceSettings.Count });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown vibrance verb. Try: list, set, restore.", ExitCodes.Usage);
        }
    }

    private static int SafeLevel(int index)
    {
        try { return DigitalVibranceService.GetLevel(index); }
        catch { return -1; }
    }
}

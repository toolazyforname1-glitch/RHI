using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.Cli.Commands;

internal static class NvidiaCommand
{
    public static async Task<int> RunAsync(Args args, IServiceProvider sp)
    {
        var command = $"nvidia.{args.Verb}";
        var presets = sp.GetRequiredService<DlssPresetService>();

        switch (args.Verb)
        {
            case "dldsr":
                return await DldsrAsync(args, sp, command);
            case "info":
            {
                presets.Initialize();
                CliOutput.Human(() =>
                {
                    Console.WriteLine($"NVAPI:        {(presets.IsSupported ? "available" : "unavailable")}");
                    Console.WriteLine($"Driver:       {presets.DriverVersionString}");
                });
                return CliOutput.Ok(command, new
                {
                    supported = presets.IsSupported,
                    driver = presets.DriverVersionString,
                });
            }
        }

        if (!GameResolver.TryResolve(args, sp, out var ctx, out var code, out var message))
            return CliOutput.Fail(command, code, message, code == "GAME_NOT_FOUND" ? ExitCodes.NotFound : ExitCodes.Usage);

        presets.Initialize();
        if (!presets.IsSupported)
            return CliOutput.Fail(command, "NVAPI_UNAVAILABLE",
                "NVAPI is not available (non-NVIDIA GPU or driver not exposing DRS).", ExitCodes.Failed);

        switch (args.Verb)
        {
            case "status":
            {
                var report = new
                {
                    game = ctx.Name,
                    driver = presets.DriverVersionString,
                    sr = new { preset = presets.GetSrPreset(ctx.Name, ctx.Path), overrideActive = presets.IsSrDriverOverrideActive(ctx.Name, ctx.Path) },
                    rr = new { preset = presets.GetRrPreset(ctx.Name, ctx.Path), overrideActive = presets.IsRrDriverOverrideActive(ctx.Name, ctx.Path) },
                    fg = new { preset = presets.GetFgPreset(ctx.Name, ctx.Path), overrideActive = presets.IsFgDriverOverrideActive(ctx.Name, ctx.Path) },
                    nr = new { preset = presets.GetNrPreset(ctx.Name, ctx.Path), overrideActive = presets.IsNrDriverOverrideActive(ctx.Name, ctx.Path) },
                    vsync = presets.GetVSyncMode(ctx.Name, ctx.Path),
                    power = presets.GetPowerManagementMode(ctx.Name, ctx.Path),
                    lowLatency = presets.GetLowLatencyMode(ctx.Name, ctx.Path),
                    smoothMotion = presets.GetSmoothMotionEnable(ctx.Name, ctx.Path),
                    fpsLimit = presets.GetPerGameFpsLimit(ctx.Name, ctx.Path),
                    gsync = presets.GetPerGameGSyncEnabled(ctx.Name, ctx.Path),
                };

                CliOutput.Human(() =>
                {
                    Console.WriteLine($"{ctx.Name} — NVIDIA driver profile");
                    Console.WriteLine($"  driver:        {report.driver}");
                    Console.WriteLine($"  SR preset:     {report.sr.preset}  override={report.sr.overrideActive}");
                    Console.WriteLine($"  RR preset:     {report.rr.preset}  override={report.rr.overrideActive}");
                    Console.WriteLine($"  FG preset:     {report.fg.preset}  override={report.fg.overrideActive}");
                    Console.WriteLine($"  NR preset:     {report.nr.preset}  override={report.nr.overrideActive}");
                    Console.WriteLine($"  vsync:         {report.vsync}");
                    Console.WriteLine($"  power:         {report.power}");
                    Console.WriteLine($"  low latency:   {report.lowLatency}");
                    Console.WriteLine($"  smooth motion: {report.smoothMotion}");
                    Console.WriteLine($"  fps limit:     {report.fpsLimit?.ToString() ?? "(none)"}");
                    Console.WriteLine($"  gsync:         {(report.gsync ? "on" : "off")}");
                });

                return CliOutput.Ok(command, report);
            }

            case "preset":
            {
                if (!RequireYes(args, command, out var confirm)) return confirm;

                var component = NormalizeComponent(args.Get("component") ?? "sr");
                var value = args.GetInt("value");

                if (component == null)
                    return CliOutput.Fail(command, "BAD_COMPONENT", "Use --component sr|rr|fg|nr.", ExitCodes.Usage);
                if (value == null || value < 0)
                    return CliOutput.Fail(command, "MISSING_ARG", "--value <n> is required (NVAPI preset index).", ExitCodes.Usage);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would set {component} preset to {value} for '{ctx.Name}'."));
                    return CliOutput.Ok(command, new { dryRun = true, component, value });
                }

                var ok = component switch
                {
                    "sr" => presets.SetSrPreset(ctx.Name, ctx.Path, (uint)value),
                    "rr" => presets.SetRrPreset(ctx.Name, ctx.Path, (uint)value),
                    "fg" => presets.SetFgPreset(ctx.Name, ctx.Path, (uint)value),
                    _ => false,
                };

                if (!ok)
                    return CliOutput.Fail(command, "FAILED", "NVAPI rejected the preset change.", ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine($"{component} preset set to {value} for '{ctx.Name}'."));
                return CliOutput.Ok(command, new { component, value });
            }

            case "override":
            {
                if (!RequireYes(args, command, out var confirm)) return confirm;

                var component = NormalizeComponent(args.Get("component") ?? "sr");
                var enable = !args.Has("off");

                if (component == null)
                    return CliOutput.Fail(command, "BAD_COMPONENT", "Use --component sr|rr|fg|nr.", ExitCodes.Usage);

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine(
                        $"Would {(enable ? "enable" : "disable")} the {component} driver override for '{ctx.Name}'."));
                    return CliOutput.Ok(command, new { dryRun = true, component, enabled = enable });
                }

                var ok = component switch
                {
                    "sr" => presets.SetSrDriverOverride(ctx.Name, ctx.Path, enable),
                    "rr" => presets.SetRrDriverOverride(ctx.Name, ctx.Path, enable),
                    "fg" => presets.SetFgDriverOverride(ctx.Name, ctx.Path, enable),
                    _ => false,
                };

                if (!ok)
                    return CliOutput.Fail(command, "FAILED",
                        component == "nr"
                            ? "NVAPI exposes no NR override setter in this build; only sr/rr/fg can be toggled."
                            : "NVAPI rejected the override change.",
                        ExitCodes.Failed);

                CliOutput.Human(() => Console.WriteLine(
                    $"{component} driver override {(enable ? "enabled" : "disabled")} for '{ctx.Name}'."));
                return CliOutput.Ok(command, new { component, enabled = enable });
            }

            case "get":
                return GetSet(args, presets, ctx, command, write: false);

            case "set":
                return GetSet(args, presets, ctx, command, write: true);

            case "global-get":
                return Global(args, presets, command, write: false);

            case "global-set":
                return Global(args, presets, command, write: true);

            case "backup":
            {
                var state = new
                {
                    game = ctx.Name,
                    sr = presets.GetSrPreset(ctx.Name, ctx.Path),
                    rr = presets.GetRrPreset(ctx.Name, ctx.Path),
                    fg = presets.GetFgPreset(ctx.Name, ctx.Path),
                    nr = presets.GetNrPreset(ctx.Name, ctx.Path),
                    vsync = presets.GetVSyncMode(ctx.Name, ctx.Path),
                    power = presets.GetPowerManagementMode(ctx.Name, ctx.Path),
                    lowLatency = presets.GetLowLatencyMode(ctx.Name, ctx.Path),
                    smoothMotion = presets.GetSmoothMotionEnable(ctx.Name, ctx.Path),
                    fpsLimit = presets.GetPerGameFpsLimit(ctx.Name, ctx.Path),
                };

                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RHI", "cli-backups");
                Directory.CreateDirectory(dir);

                var safeName = string.Concat(ctx.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var path = Path.Combine(dir, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}_nvidia.json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                CliOutput.Human(() => Console.WriteLine($"Driver profile snapshot written to {path}"));
                return CliOutput.Ok(command, new { backup = path });
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown nvidia verb. Try: info, status, preset, override, get, set, global-get, global-set, backup, dldsr.",
                    ExitCodes.Usage);
        }
    }

    private static int GetSet(Args args, DlssPresetService presets, GameContext ctx, string command, bool write)
    {
        var key = (args.Get("key") ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
            return CliOutput.Fail(command, "MISSING_ARG", "--key <name> is required.", ExitCodes.Usage);

        if (!write)
        {
            object? value = key switch
            {
                "vsync" => presets.GetVSyncMode(ctx.Name, ctx.Path),
                "vsync-tear" => presets.GetVSyncTearControl(ctx.Name, ctx.Path),
                "power" => presets.GetPowerManagementMode(ctx.Name, ctx.Path),
                "low-latency" => presets.GetLowLatencyMode(ctx.Name, ctx.Path),
                "smooth-motion" => presets.GetSmoothMotionEnable(ctx.Name, ctx.Path),
                "fps-limit" => presets.GetPerGameFpsLimit(ctx.Name, ctx.Path),
                "gsync" => presets.GetPerGameGSyncEnabled(ctx.Name, ctx.Path),
                _ => null,
            };

            if (value == null)
                return CliOutput.Fail(command, "BAD_KEY",
                    "Unknown key. Try: vsync, vsync-tear, power, low-latency, smooth-motion, fps-limit, gsync.", ExitCodes.Usage);

            CliOutput.Human(() => Console.WriteLine($"{key} = {value}"));
            return CliOutput.Ok(command, new { key, value });
        }

        if (!RequireYes(args, command, out var confirm)) return confirm;

        var raw = args.GetInt("value");
        if (raw == null)
            return CliOutput.Fail(command, "MISSING_ARG", "--value <n> is required.", ExitCodes.Usage);

        bool ok = key switch
        {
            "vsync" => presets.SetVSyncMode(ctx.Name, ctx.Path, (uint)raw),
            "vsync-tear" => presets.SetVSyncTearControl(ctx.Name, ctx.Path, (uint)raw),
            "power" => presets.SetPowerManagementMode(ctx.Name, ctx.Path, (uint)raw),
            "low-latency" => presets.SetLowLatencyMode(ctx.Name, ctx.Path, (uint)raw),
            "smooth-motion" => presets.SetSmoothMotionEnable(ctx.Name, ctx.Path, (uint)raw),
            "fps-limit" => raw <= 0
                ? presets.ClearPerGameFpsLimit(ctx.Name, ctx.Path)
                : presets.SetPerGameFpsLimit(ctx.Name, ctx.Path, (uint)raw),
            "gsync" => presets.SetPerGameGSyncEnabled(ctx.Name, ctx.Path, raw != 0),
            _ => false,
        };

        if (!ok && key is not ("vsync" or "vsync-tear" or "power" or "low-latency" or "smooth-motion" or "fps-limit" or "gsync"))
            return CliOutput.Fail(command, "BAD_KEY",
                "Unknown key. Try: vsync, vsync-tear, power, low-latency, smooth-motion, fps-limit, gsync.", ExitCodes.Usage);

        CliOutput.Human(() => Console.WriteLine(ok
            ? $"{key} set to {raw} for '{ctx.Name}'."
            : $"{key}: NVAPI rejected the change."));

        return ok
            ? CliOutput.Ok(command, new { key, value = raw })
            : CliOutput.Fail(command, "FAILED", "NVAPI rejected the change.", ExitCodes.Failed);
    }

    private static int Global(Args args, DlssPresetService presets, string command, bool write)
    {
        var key = (args.Get("key") ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
            return CliOutput.Fail(command, "MISSING_ARG", "--key <name> is required.", ExitCodes.Usage);

        if (!write)
        {
            object? value = key switch
            {
                "vsync" => presets.GetGlobalVSyncMode(),
                "power" => presets.GetGlobalPowerMode(),
                "shader-cache" => presets.GetShaderCacheSize(),
                "shader-precompile" => presets.GetShaderPrecompile(),
                "gsync-mode" => presets.GetGSyncMode(),
                "gsync-enabled" => presets.GetGlobalGSyncEnabled(),
                "fps-limit" => presets.GetGlobalFpsLimit(),
                "dmfg-frames" => presets.GetGlobalDmfgFrameCount(),
                "dmfg-target-fps" => presets.GetGlobalDmfgTargetFps(),
                "preferred-refresh" => presets.GetPreferredRefreshRate(),
                "rebar-mode" => presets.GetGlobalReBarEnableMode(),
                "rebar-size" => presets.GetGlobalReBarSizeLimit(),
                _ => null,
            };

            if (value == null)
                return CliOutput.Fail(command, "BAD_KEY", "Unknown global key. See docs/IN-GAME-UI.md.", ExitCodes.Usage);

            CliOutput.Human(() => Console.WriteLine($"{key} = {value}"));
            return CliOutput.Ok(command, new { key, value });
        }

        if (!RequireYes(args, command, out var confirm)) return confirm;

        var raw = args.Get("value");
        if (string.IsNullOrWhiteSpace(raw))
            return CliOutput.Fail(command, "MISSING_ARG", "--value <n> is required.", ExitCodes.Usage);

        var ok = key switch
        {
            "vsync" => uint.TryParse(raw, out var v) && presets.SetGlobalVSyncMode(v),
            "power" => uint.TryParse(raw, out var p) && presets.SetGlobalPowerMode(p),
            "shader-cache" => uint.TryParse(raw, out var sc) && presets.SetShaderCacheSize(sc),
            "shader-precompile" => uint.TryParse(raw, out var spc) && presets.SetShaderPrecompile(spc),
            "gsync-mode" => uint.TryParse(raw, out var gm) && presets.SetGSyncMode(gm),
            "gsync-enabled" => uint.TryParse(raw, out var ge) && presets.SetGlobalGSyncEnabled(ge),
            "fps-limit" => uint.TryParse(raw, out var fl) && presets.SetGlobalFpsLimit(fl),
            "dmfg-frames" => uint.TryParse(raw, out var df) && presets.SetGlobalDmfgFrameCount(df),
            "dmfg-target-fps" => uint.TryParse(raw, out var dt) && presets.SetGlobalDmfgTargetFps(dt),
            "preferred-refresh" => uint.TryParse(raw, out var pr) && presets.SetPreferredRefreshRate(pr),
            "rebar-mode" => uint.TryParse(raw, out var rm) && presets.SetGlobalReBarEnableMode(rm),
            "rebar-size" => ulong.TryParse(raw, out var rs) && presets.SetGlobalReBarSizeLimit(rs),
            _ => false,
        };

        CliOutput.Human(() => Console.WriteLine(ok
            ? $"{key} set to {raw}."
            : $"{key}: NVAPI rejected the change (or unknown key)."));

        return ok
            ? CliOutput.Ok(command, new { key, value = raw })
            : CliOutput.Fail(command, "FAILED", "NVAPI rejected the change (or unknown key).", ExitCodes.Failed);
    }

    private static async Task<int> DldsrAsync(Args args, IServiceProvider sp, string command)
    {
        var dldsr = sp.GetRequiredService<IDldsrService>();

        var verb = args.Positional.Count > 0 ? args.Positional[0].ToLowerInvariant() : "current";

        switch (verb)
        {
            case "current":
            {
                var state = dldsr.GetCurrentState();
                CliOutput.Human(() =>
                {
                    Console.WriteLine($"DLDSR smoothness: {dldsr.GetCurrentSmoothness()}%");
                    foreach (var s in state)
                        Console.WriteLine("  " + System.Text.Json.JsonSerializer.Serialize(s));
                });
                return CliOutput.Ok(command, new { smoothness = dldsr.GetCurrentSmoothness(), state });
            }

            case "captures":
            {
                var captures = dldsr.LoadCaptures();
                CliOutput.Human(() =>
                {
                    foreach (var c in captures)
                        Console.WriteLine("  " + System.Text.Json.JsonSerializer.Serialize(c));
                });
                return CliOutput.Ok(command, new { captures });
            }

            case "capture":
            {
                var label = args.Get("label") ?? $"cli-{DateTime.Now:yyyyMMdd-HHmmss}";
                if (!RequireYes(args, command, out var confirm)) return confirm;

                dldsr.CaptureCurrentState(label);
                CliOutput.Human(() => Console.WriteLine($"DLDSR state captured as '{label}'."));
                return CliOutput.Ok(command, new { captured = label });
            }

            case "apply":
            {
                var label = args.Get("label");
                if (string.IsNullOrWhiteSpace(label))
                    return CliOutput.Fail(command, "MISSING_ARG", "--label <capture> is required.", ExitCodes.Usage);
                if (!RequireYes(args, command, out var confirm)) return confirm;

                if (args.DryRun)
                {
                    CliOutput.Human(() => Console.WriteLine($"Would apply DLDSR capture '{label}'."));
                    return CliOutput.Ok(command, new { dryRun = true, label });
                }

                var ok = await dldsr.ApplyStateAsync(label, new CliTextProgress());
                CliOutput.Human(() => Console.WriteLine(ok ? $"Applied DLDSR capture '{label}'." : "DLDSR apply failed."));
                return ok ? CliOutput.Ok(command, new { applied = label }) : CliOutput.Fail(command, "FAILED", "DLDSR apply failed.", ExitCodes.Failed);
            }

            case "smoothness":
            {
                var value = args.GetInt("value");
                if (value == null) return CliOutput.Fail(command, "MISSING_ARG", "--value 0..100 is required.", ExitCodes.Usage);
                if (!RequireYes(args, command, out var confirm)) return confirm;

                var ok = await dldsr.SetSmoothnessAsync(value.Value, new CliTextProgress());
                CliOutput.Human(() => Console.WriteLine(ok ? $"DLDSR smoothness set to {value}%." : "DLDSR smoothness change failed."));
                return ok ? CliOutput.Ok(command, new { smoothness = value }) : CliOutput.Fail(command, "FAILED", "DLDSR smoothness change failed.", ExitCodes.Failed);
            }

            default:
                return CliOutput.Fail(command, "UNKNOWN_VERB",
                    "Unknown dldsr verb. Try: current, captures, capture, apply, smoothness.", ExitCodes.Usage);
        }
    }

    private static string? NormalizeComponent(string value) => value.ToLowerInvariant() switch
    {
        "sr" or "dlss" => "sr",
        "rr" or "dlssd" => "rr",
        "fg" or "dlssg" => "fg",
        "nr" or "dlssnr" => "nr",
        _ => null,
    };

    private static bool RequireYes(Args args, string command, out int exitCode)
    {
        if (args.Yes || args.DryRun) { exitCode = 0; return true; }
        exitCode = CliOutput.Fail(command, "CONFIRMATION_REQUIRED",
            "This changes the NVIDIA driver profile. Re-run with --yes (or --dry-run).", ExitCodes.Usage);
        return false;
    }
}

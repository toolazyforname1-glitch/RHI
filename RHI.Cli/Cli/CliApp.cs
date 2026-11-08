using RenoDXCommander.Cli.Commands;

namespace RenoDXCommander.Cli;

internal static class CliApp
{
    public static async Task<int> RunAsync(string[] raw)
    {
        var args = Args.Parse(raw);
        CliOutput.Json = args.Json;
        CliOutput.Verbose = args.Has("verbose");

        if (args.Group is "" or "help" || args.Has("help"))
        {
            PrintHelp();
            return ExitCodes.Ok;
        }

        using var services = CliServices.Build();

        if (args.Verb.StartsWith("ini-", StringComparison.OrdinalIgnoreCase) &&
            args.Group is "reshade" or "renodx" or "relimiter" or "display-commander" or "optiscaler")
        {
            return IniCommand.Run(args, services);
        }

        if (args.Group == "renodx" && args.Verb is "mode" or "peak-nits")
        {
            return await RenodxSettingsCommand.RunAsync(args, services);
        }

        try
        {
            return args.Group switch
            {
                "doctor" => DoctorCommand.Run(args),
                "paths" => PathsCommand.Run(args),
                "status" => StatusCommand.Run(args, services),
                "reshade" => await ReShadeCommand.RunAsync(args, services),
                "reframework" or "dofix" or "dxvk" or "optiscaler"
                    => await AuxComponentCommand.RunAsync(args, services),
                "dlss" => await DlssCommand.RunAsync(args, services),
                "renodx" => await RenodxCommand.RunAsync(args, services),
                "luma" => await LumaCommand.RunAsync(args, services),
                "shaders" => await ShadersCommand.RunAsync(args, services),
                "addons" => await AddonsCommand.RunAsync(args, services),
                "relimiter" or "display-commander" => await LimitersCommand.RunAsync(args, services),
                "asi" or "dlss-enabler" or "fg2030" or "rtx-encore" or "mfg40" or "dgvoodoo" or "ultraplus"
                    => await ExtrasCommand.RunAsync(args, services),
                "preset" => await PresetsCommand.RunAsync(args, services),
                "vulkan" => await VulkanCommand.RunAsync(args, services),
                "nvidia" => await NvidiaCommand.RunAsync(args, services),
                "hdr" or "res" or "vibrance" => DisplayCommand.Run(args, services),
                "update" => await UpdateAllCommand.RunAsync(args, services),
                "mcp" => await McpCommand.RunAsync(args),
                "library" => await LibraryCommand.RunAsync(args, services),
                "game" => await GameCommand.RunAsync(args, services),
                "config" => await ConfigCommand.RunAsync(args, services),
                _ => CliOutput.Fail($"{args.Group}.{args.Verb}",
                    "UNKNOWN_COMMAND", $"Unknown command '{args.Group}'. Run 'rhictl help'.", ExitCodes.Usage),
            };
        }
        catch (Exception ex)
        {
            return CliOutput.Fail($"{args.Group}.{args.Verb}", "UNHANDLED", ex.Message, ExitCodes.Failed);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            rhictl — headless RHI (ReShade HDR Installer)

            USAGE
              rhictl <group> <verb> [options]

            GLOBAL OPTIONS
              --json        machine-readable output
              --dry-run     resolve and report, change nothing
              --yes         required for mutating commands
              --force       override safety guards
              --game <n>    target game by name
              --path <dir>  target a game folder directly
              --verbose     per-step logging on stderr

            COMMANDS
              doctor                     environment and capability check
              paths                      show every state directory
              status --game <n>          installed components for one game
              reshade install --game <n> --channel stable|nightly [--dll dxgi.dll|d3d9.dll]
              reshade update --game <n>
              reshade uninstall --game <n>
              reframework|dofix|dxvk|optiscaler install|uninstall --game <n>
              dlss status|versions|set|restore --game <n>
              renodx install|update|uninstall --game <n>
              renodx mode --game <n> --value simple|intermediate|advanced
              renodx peak-nits --game <n> --value <nits>
              renodx ue-extended --game <n> --value on|off
              luma list|install|uninstall --game <n>
              shaders list | shaders install|remove --game <n> --packs a,b
              addons list | addons install|remove --game <n> --name <package>|--section <id>
              game launch --game <n> [--args "..."]
              relimiter | display-commander install|uninstall --game <n>
              asi | dlss-enabler | fg2030 | rtx-encore | mfg40 | dgvoodoo install|uninstall --game <n>
              ultraplus url --game <n> [--open]
              preset list | preset deploy --game <n> [--file a.ini,b.ini|all] [--with-shaders]
              vulkan status|install|uninstall [--game <n>] [--layer-only]
              nvidia info|status|preset|override|get|set|global-get|global-set|backup|dldsr
              hdr status|on|off|list | res current|list|set|restore | vibrance list|set|restore
              update all [--components reshade,renodx,...] [--game <n>]
              mcp serve
              library list               list the saved game library
              library list --favourites|--hidden|--all|--installed [--engine X] [--store Y]
              library refresh            rescan all storefronts
              library show --game <n>    details for one game
              game add --path <dir>      add a manual game
              game remove --game <n>     remove a game entry
              game favourite|unfavourite|hide|unhide --game <n>
              game set-path --game <n> --path <dir>
              game set-launch --game <n> [--exe <path>] [--args "..."]
              game reset-launch --game <n>
              game set-wiki-name --game <n> --name <wiki name>
              game reset-wiki-name --game <n>
              game set-name --game <n> --name <new display name>
              game set-dll --game <n> [--reshade <file.dll>] [--dc <file.dll>] [--os <file.dll>]
              game reset-dll --game <n>
              game set-override --game <n> --kind <k> --value <v>
              game clear-override --game <n> --kind <k>
              game reset-overrides --game <n>
              config list|get|set|unset --key <k> [--value <v>] [--show-secrets]
              reshade|renodx|relimiter|display-commander|optiscaler ini-list|ini-get|ini-set
                     --section <s> --key <k> [--value <v>]
            """);
    }
}

# rhictl — headless CLI for RHI

`rhictl` exposes RHI from a terminal. It runs RHI's **own service layer** — the same code the
WinUI app uses — without opening a window. The app is not modified; it is not required at
runtime.

- Shares the GUI's state: `%LOCALAPPDATA%\RHI` (`settings.json`, `game_library.json`, records,
  downloads).
- Writes the same files through the same services.
- JSON output, dry-run, and explicit confirmation for every mutation.

## Build

From the repository root:

```powershell
dotnet build RHI.Cli\RHI.Cli.csproj -c Release

dotnet publish RHI.Cli\RHI.Cli.csproj -c Release -r win-x64 --self-contained false -o dist
```

No extra arguments are needed in this repository: `RHI.Core` links
`..\RenoDXCommander\Services` and `..\RenoDXCommander\Models` by default. To build from a
different checkout, pass `-p:RhiSourceDir="C:\path\to\RenoDXCommander"`.

Run:

```powershell
.\dist\rhictl.exe doctor
.\dist\rhictl.exe library list
.\dist\rhictl.exe status --game "Cyberpunk 2077"
```

## Commands

```
doctor
paths
status
config list|get|set|unset --key <k> [--value <v>] [--show-secrets]
library list|show|refresh
game add|remove|launch|favourite|unfavourite|hide|unhide|set-path
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
reshade install|update|uninstall
reshade|renodx|relimiter|display-commander|optiscaler ini-list|ini-get|ini-set
shaders list|install|remove
addons list|install|remove
renodx install|update|uninstall|mode|peak-nits
renodx ue-extended --game <n> --value on|off
luma list|install|uninstall
dlss status|versions|set|restore|scales|render-scale
reframework install|uninstall
dofix install|uninstall
dxvk install|uninstall|update
optiscaler install|uninstall|update
asi install|uninstall
dlss-enabler install|uninstall
fg2030 install|uninstall
rtx-encore install|uninstall
mfg40 install|uninstall
dgvoodoo install|uninstall
ultraplus url
relimiter install|uninstall
display-commander install|uninstall
preset list|deploy
vulkan status|install|uninstall
nvidia info|status|preset|override|get|set|global-get|global-set|backup|dldsr
hdr status|on|off|list
res current|list|set|restore
vibrance list|set|restore
update all
mcp serve
```

Global flags: `--json`, `--dry-run`, `--yes`, `--force`, `--verbose`, `--game`, `--path`,
`--show-secrets`, `--help`.

## Architecture

`RHI.Core` compiles the app's real `Services/` + `Models/` source. Nothing is copied or moved;
files are linked at compile time. The WinUI project is not referenced and not modified.

- Five files are excluded (UI-only): `AddonInfoResolver`, `AutoUpdateService`,
  `ControlUePostInstallService`, `UpdateOrchestrationService`, `IUpdateOrchestrationService`.
- `RHI.Core/Shims/` provides headless stand-ins for `Microsoft.UI.Dispatching.DispatcherQueue`,
  `Microsoft.UI.Xaml.Application`, the `GameCardViewModel` members the services touch,
  `App.Services`, and two `MainViewModel` helpers.
- If a service starts using a new `GameCardViewModel` member, `RHI.Core` stops compiling — the
  intended early warning.
- `RHI.Cli/Cli/CliServices.cs` mirrors the app's DI graph minus UI registrations.

## Safety

- No mutation without `--yes`.
- `--dry-run` on every mutating verb; writes nothing, downloads nothing.
- Refuses a running game, and refuses to mutate while `RHI.exe` is running (shared
  `settings.json`). `--force` overrides.
- Refuses `C:\Program Files\WindowsApps`.
- Vulkan layer changes require an elevated terminal.
- Component installs use RHI's own backup logic; `ini-set` keeps a `.cli-backup`.

## Documentation

| File | Contents |
|---|---|
| `docs/EVERYTHING.md` | complete reference: architecture, every command, component internals, in-game menu, persistence map, safety, verification log, troubleshooting, source map |
| `docs/IN-GAME-MENU.md` | ReShade/RenoDX/ReLimiter in-game overlay reference |
| `docs/IN-GAME-UI.md` | what is file-backed vs in-game-only, with sources |
| `docs/GUI-INVENTORY.md` | RHI 2.8.9 GUI → CLI coverage matrix |
| `docs/PROBE.md` | universal window probe usage |
| `docs/in-game-captures/` | window-only overlay captures |

## License

GPL-3.0, same as RHI. See the repository `LICENSE`.

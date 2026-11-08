# rhictl — headless RHI CLI

**Goal:** every RHI action reachable from a terminal, JSON in/out, safe by default.

**Continuing this project? Read `docs/HANDOFF.md` first** — exact state, resume commands,
upstream-drift procedure and the prioritized next tasks.

## Current state (2026-11-08)

| Area | Status | What works |
|---|---|---|
| Core | **done** | `RHI.Core` compiles RHI's real service layer headlessly |
| Shell | **done** | parser, `--json`, `--dry-run`, `--yes`, `--force`, exit codes, stderr progress |
| Settings | **done** | `config list/get/set/unset` over `settings.json` (atomic `SettingsFile.Update`) |
| Library | **done** | `doctor`, `paths`, `library list/show/refresh`, `game add/remove`, `status` |
| Overrides | **done** | `game set-launch/reset-launch`, `set-wiki-name/reset-wiki-name`, `reset-overrides` |
| ReShade | **done** | install/update/uninstall, channel/dll/shaders; Vulkan layer flow |
| HDR mods | **done** | RenoDX (wiki + generic UE/Unity), UE-Extended toggle, Luma, RE Framework, DOF Fix |
| DLSS | **done** | status/versions/set/restore for sr/rr/fg/nr/streamline |
| Components | **done** | OptiScaler, DXVK, ASI, DLSS Enabler, 20/30 FG, RTX Encore, MFG Unlocker, dgVoodoo2, Ultra+ URL |
| Limiters | **done** | ReLimiter and Display Commander install/uninstall |
| Presets | **done** | `preset list`, `preset deploy` |
| NVIDIA | **done** | presets, overrides, per-game/global settings, profile backup, DLDSR |
| Display | **done** | HDR on/off/list, resolution current/list/set/restore, vibrance |
| Batch | **done** | `update all [--components] [--game]` |
| Docs | **done** | `docs/GUI-INVENTORY.md`, `docs/IN-GAME-UI.md`, `docs/IN-GAME-MENU.md`, `docs/EVERYTHING.md` + 10 live captures |
| MCP | **done** | `rhictl mcp serve` (stdio JSON-RPC, 11 tools) |

## Command surface

```
doctor | paths | status
config list|get|set|unset             --key <k> [--value <v>] [--show-secrets]
library list|show|refresh             --favourites --hidden --all --installed --engine --store
game add|remove|launch
game favourite|unfavourite|hide|unhide|set-path
game set-launch|reset-launch         [--exe <path>] [--args "..."]
game set-wiki-name|reset-wiki-name    --name <wiki name>
game set-name                        --name <new display name>
game set-dll|reset-dll               [--reshade <file.dll>] [--dc <file.dll>] [--os <file.dll>]
game reset-overrides
reshade|renodx|relimiter|display-commander|optiscaler ini-list|ini-get|ini-set
reshade install|update|uninstall      --channel --dll --shaders --packs --no-addons
shaders list|install|remove           --packs
addons list|install|remove            --name <package>|--section <id>
renodx install|update|uninstall
renodx mode --value simple|intermediate|advanced
renodx peak-nits --value <nits>
renodx ue-extended --value on|off
luma list|install|uninstall
dlss status|versions|set|restore|scales|render-scale
reframework|dofix|dxvk|optiscaler install|uninstall|update
asi | dlss-enabler | fg2030 | rtx-encore | mfg40 | dgvoodoo install|uninstall
relimiter | display-commander install|uninstall
ultraplus url                         --open
preset list|deploy                    --file a.ini,b.ini|all --with-shaders
vulkan status|install|uninstall       [--game <n>] [--layer-only]   (layer needs admin)
nvidia info|status|preset|override|get|set|global-get|global-set|backup
nvidia dldsr current|captures|capture|apply|smoothness
hdr status|on|off|list
res current|list|set|restore
vibrance list|set|restore
update all                            --components reshade,renodx,... --game <n>
mcp serve
```

All mutating verbs require `--yes`; all support `--dry-run`.

## Published build

`C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI\rhictl.exe` (framework-dependent, .NET 8 runtime).

```powershell
& "C:\Users\cattu\.dotnet\dotnet.exe" publish `
  "C:\Users\cattu\Documents\Directories\Denuvo\RHI\RHI.Cli\RHI.Cli.csproj" `
  -c Release -r win-x64 --self-contained false `
  -o "C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI"
```

## Architecture (do not change casually)

- `RHI.Core/RHI.Core.csproj` **links** `..\RenoDXCommander\Services\**\*.cs` and `Models\**\*.cs`.
  Nothing is moved or copied; the WinUI app project is untouched.
- Excluded from RHI.Core (pure-UI edge): `AddonInfoResolver`, `AutoUpdateService`,
  `ControlUePostInstallService`, `UpdateOrchestrationService`, `IUpdateOrchestrationService`.
- `RHI.Core/Shims/`: `Microsoft.UI.Dispatching.DispatcherQueue` (runs inline),
  `Microsoft.UI.Xaml.Application`, `GameCardViewModel` (subset used by services),
  `App.Services` locator, `MainViewModel` (static helpers + one instance member used by
  `DlssEnablerService`, which returns null in the CLI).
- If a service starts using a new `GameCardViewModel` member, **RHI.Core stops compiling** — the
  intended early warning. Add the member to the shim.
- `RHI.Cli` references `RHI.Core`; its DI graph mirrors `App.xaml.cs` minus UI registrations.
- State is shared with the GUI (`%LOCALAPPDATA%\RHI`).

## Decisions / pitfalls learned

- `PeHeaderService.FindGameExe` returns the **largest** exe → picks `UnityCrashHandler64.exe` on Unity
  games. CLI uses `ExeLocator` (ProductName scoring).
- Records must be matched by **install path**, not name: two copies of a game otherwise report each
  other's components.
- `GameLibraryService.Save` drops Steam/Epic/Xbox ids; always round-trip every `SavedGameLibrary` collection.
- `IReShadeUpdateService.EnsureLatestAsync` returns **false when already current**; verify the staged
  DLL exists instead of treating false as failure.
- `ShaderPackService.SyncGameFolder(null|empty)` **removes managed shaders**; resolve the real selection first.
- `IDofFixService` has a `static abstract` member → cannot be a DI generic argument; resolve `DofFixService`.
- `SettingsViewModel` is UI-free and linked; state loads via `IGameNameService.LoadNameMappings(...)`,
  which can migrate/write — only call for mutating commands (`CliState`).
- `Rtx40MfgService.EnsureStagingAsync()` takes no progress argument (unlike the other services).
- NVAPI exposes no **NR override setter** in this build; only sr/rr/fg can be toggled.
- ReLimiter creates no aux record (unlike Display Commander); status must detect it from the deployed
  `relimiter.addon64/32` file plus `ul_meta.json`.
- `DigitalVibranceService.EnumerateDisplays()` returns no displays on this machine/driver; `vibrance
  list` is empty and `vibrance set` cannot resolve an index. The code path is correct, the raw NVAPI
  display enumeration is the limitation.
- Executable is `rhictl.exe`, not `rhi.exe` (`RHI.exe` already exists; Windows is case-insensitive).

## Verified on hardware

`Death in the Water 2` (Unity, DX11, 64-bit):
- `tools\verify-rhictl.ps1` → **45/45** (config, reset-overrides and set-launch checks added 2026-11-08)
- ReShade 6.8.0 → `dxgi.dll`
- RenoDX generic Unity 26.1014.1445 → `renodx-unityengine.addon64`
- ReLimiter v3.3.5 → `relimiter.addon64`
- `update all --components reshade --dry-run` → "up to date"
- GUI relaunched afterwards, built the game's driver-profile section, closed cleanly.

## Known gaps

- In-game menu settings are file-backed and now documented in `docs/IN-GAME-MENU.md`; the CLI
  edits them through `reshade|renodx|relimiter|display-commander|optiscaler ini-set`.
- The only unconfirmed key is ReShade 6.8's screenshot *name* template (`[SCREENSHOT]`); path,
  format and overlay-shot toggle are confirmed.
- The ReLimiter template ships `[UltraLimiter]` while all writers target `[FrameLimiter]`.

- Vulkan ReShade install requires an elevated terminal (`vulkan install` refuses otherwise) and was
  verified only with `status`/dry-run.
- NVIDIA driver writes were verified read-only (`nvidia info/status`); `set`/`preset`/`override` use
  the same NVAPI service methods the GUI uses but were not exercised on hardware.
- `vibrance` display enumeration limitation above.
- MCP `tools/call` blocks for the duration of the operation (installs can take minutes).

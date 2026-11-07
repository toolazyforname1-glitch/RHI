# RHI + rhictl — Complete Project Reference

**Generated:** 2026-11-05
**Scope:** every change, every command, every verified behaviour, every known gap.
**Rule for this document:** claims are marked **[verified]**, **[high]**, **[likely]**, or **[undocumented]**. Nothing in the last two categories should be treated as fact.

---

## Table of contents

1. Executive summary
2. Repository map
3. Build environment
4. Architecture
5. Runtime state and shared files
6. CLI conventions (flags, JSON, exit codes)
7. Command reference (every group and verb)
8. Component technical reference
9. In-game menu reference
10. Persistence map (menu → file → CLI)
11. GUI → CLI coverage matrix
12. Safety model
13. Verification log
14. Known gaps and uncertainties
15. Troubleshooting and gotchas
16. Source map appendix
17. Glossary
18. Session changelog
19. Future work

---

## 1. Executive summary

RHI is a WinUI 3 desktop app ("ReShade HDR Installer", v2.8.9) that installs and configures
ReShade, RenoDX, Luma, OptiScaler, DXVK, DLL overrides, NVIDIA driver settings and related
components into game folders.

This project adds **`rhictl`**, a headless command-line front-end that reuses RHI's *actual
service layer* rather than reimplementing it. The WinUI app was not modified, not rebuilt, and
not replaced; the installed app at `C:\Program Files\RHI` is byte-for-byte the original.

What exists now:

- **`RHI.Core`** — a class library that compiles RHI's real `Services/` + `Models/` source
  (157 service files, ~45,000 LOC) into a UI-free assembly, with five small shims for the
  handful of UI-shaped types the services touch.
- **`RHI.Cli`** — a console app (`rhictl.exe`) with ~60 verbs covering library management,
  ReShade, RenoDX, Luma, DLSS/Streamline, OptiScaler, DXVK, RE Framework, DOF Fix, ASI Loader,
  DLSS Enabler, 20/30 FG unlock, RTX Encore, MFG Unlocker, dgVoodoo2, Ultra+, ReLimiter,
  Display Commander, shader packs, addon packs, presets, Vulkan ReShade, NVIDIA driver settings,
  DLDSR, HDR/resolution/vibrance, batch updates, raw ini editing, and an MCP server.
- **Documentation** — GUI inventory, in-game menu reference, in-game settings research, and
  this master document.
- **10 live in-game captures** of the ReShade/RenoDX/ReLimiter overlay.

**Design principle:** the CLI edits the same files, through the same code, that the GUI writes.
It is not a parallel implementation and cannot silently diverge in behaviour.

**Safety principle:** nothing mutating runs without `--yes`; every mutating verb supports
`--dry-run`; game-running and RHI-running guards are enforced unless `--force`.

---

## 2. Repository map

Root: `C:\Users\cattu\Documents\Directories\Denuvo\RHI`

### New this session (untracked in git, app source untouched)

| Path | Type | Purpose |
|---|---|---|
| `RHI.Core\RHI.Core.csproj` | project | links `..\RenoDXCommander\Services\**\*.cs` and `Models\**\*.cs`; excludes 5 UI-only files; adds shims |
| `RHI.Core\Shims\DispatcherQueueShim.cs` | source | headless `Microsoft.UI.Dispatching.DispatcherQueue` (runs callbacks inline) |
| `RHI.Core\Shims\XamlShim.cs` | source | `Microsoft.UI.Xaml.Application` + unhandled-exception types for `CrashReporter` |
| `RHI.Core\Shims\GameCardViewModelShim.cs` | source | the ~90 `GameCardViewModel` members the services actually use |
| `RHI.Core\Shims\AppShim.cs` | source | static `App.Services` service-locator used by some services |
| `RHI.Core\Shims\MainViewModelShim.cs` | source | static `MainViewModel` helpers + one instance member (`GetDeInstalledAs`) |
| `RHI.Cli\RHI.Cli.csproj` | project | console app, `AssemblyName=rhictl`, references `RHI.Core` |
| `RHI.Cli\Program.cs` | source | entry point |
| `RHI.Cli\Cli\Args.cs` | source | minimal parser (`group verb --flags`) |
| `RHI.Cli\Cli\CliApp.cs` | source | dispatch + help |
| `RHI.Cli\Cli\CliOutput.cs` | source | JSON envelope, tables, exit codes |
| `RHI.Cli\Cli\CliServices.cs` | source | DI graph mirroring `App.xaml.cs` minus UI |
| `RHI.Cli\Cli\CliState.cs` | source | lazy load/save of settings + name mappings |
| `RHI.Cli\Cli\CliGuards.cs` | source | game-running / RHI-running guards; progress reporters |
| `RHI.Cli\Cli\CliDownloads.cs` | source | HTTP download + GitHub release asset resolver + meta files |
| `RHI.Cli\Cli\ExeLocator.cs` | source | product-name-scored game exe finder |
| `RHI.Cli\Cli\GameResolver.cs` | source | `--game`/`--path` → name, path, engine, API, bitness, exe |
| `RHI.Cli\Cli\IniEditor.cs` | source | INI get/set/list with `.cli-backup` and atomic write |
| `RHI.Cli\Cli\LibraryStore.cs` | source | library load/save round-trip; dedupe vs composite keys |
| `RHI.Cli\Cli\ReShadeDllName.cs` | source | API → proxy DLL name (dxgi/d3d9/d3d8/opengl32) |
| `RHI.Cli\Commands\*.cs` | source | one file per command group (25 files) |
| `RHI.Cli\PROGRESS.md` | doc | status, pitfalls, next steps |
| `RHI-CLI\rhictl.exe` + deps | build | published framework-dependent Release build |
| `RHI-CLI\README.md` | doc | usage reference |
| `RHI-CLI\probe.ps1` | tool | window-message input + `PrintWindow` capture harness |
| `docs\GUI-INVENTORY.md` | doc | live UIA dump of RHI 2.8.9 → CLI coverage matrix |
| `docs\GUI-INVENTORY-raw.txt` | doc | raw 359-element automation tree |
| `docs\IN-GAME-UI.md` | doc | research: what is file-backed vs in-game-only |
| `docs\IN-GAME-MENU.md` | doc | live overlay capture → ini key reference |
| `docs\in-game-captures\*.png` | evidence | 10 window-only captures |
| `docs\EVERYTHING.md` | doc | this document |

### Deliberately not changed

- `RenoDXCommander\**` — zero edits. `git diff --name-only` is empty.
- `RHI-Setup.exe` — not re-run.
- `C:\Program Files\RHI\**` — untouched (172,154,424 bytes, 2026-11-03 00:58:30, v2.8.9.0).

---

## 3. Build environment

| Item | Value |
|---|---|
| .NET SDK | 8.0.427, installed **user-local** at `C:\Users\cattu\.dotnet` (no admin, no system change) |
| Runtime used by `rhictl` | .NET 8 desktop runtime 8.0.33 (already present) |
| Target framework | `net8.0-windows10.0.19041.0` (needed for WinRT projections used by Xbox/Store detection) |
| Platform | x64 (`Directory.Build.props` sets `Platform=x64`) |
| Build command | `& "C:\Users\cattu\.dotnet\dotnet.exe" build "…\RHI.Cli\RHI.Cli.csproj" -v q --nologo` |
| Publish command | `& "…\.dotnet\dotnet.exe" publish "…\RHI.Cli\RHI.Cli.csproj" -c Release -r win-x64 --self-contained false -o "C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI"` |
| Published exe | `C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI\rhictl.exe` |

No NuGet packages were added beyond those the app already uses:
`Microsoft.Extensions.DependencyInjection`, `CommunityToolkit.Mvvm`, `HtmlAgilityPack`,
`Microsoft.Win32.Registry`, `NvAPIWrapper.Net`, `SharpCompress`, `System.Management`.

Note: `SharpCompress 0.37.2` carries a known moderate-severity advisory inherited from the app;
the CLI uses the same version deliberately to stay behaviour-identical.

---

## 4. Architecture

### 4.1 Options considered

| Option | How | Verdict |
|---|---|---|
| **A. Reference the WinUI app assembly** | console `ProjectReference` → `RenoDXCommander.csproj` | Rejected: drags the entire WinUI payload, and constructing `GameCardViewModel` can trigger Windows App SDK activation in a non-GUI process |
| **B. Linked core library** | new csproj compiling the app's `Services/` + `Models/` source via `<Compile Include="..\...\**\*.cs" />` | **Chosen.** App project untouched, small binary, no WinUI at all |
| **C. `RHI.exe --cli` inside the app** | custom entry point + `AttachConsole` | Rejected for now: rewrites the app entry point, highest risk to the shipped GUI |

### 4.2 How the linked core works

`RHI.Core.csproj`:

```xml
<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
<Compile Include="..\RenoDXCommander\Services\**\*.cs" LinkBase="Services"
         Exclude="...5 files..." />
<Compile Include="..\RenoDXCommander\Models\**\*.cs" LinkBase="Models" />
<Compile Include="..\RenoDXCommander\ViewModels\SettingsViewModel.cs" LinkBase="ViewModels" />
<Compile Include="..\RenoDXCommander\HotkeyManager.cs" LinkBase="AppRoot" />
<Compile Include="..\RenoDXCommander\NativeInterop.cs" LinkBase="AppRoot" />
<Compile Include="Shims\**\*.cs" LinkBase="Shims" />
```

Files are **linked, not moved or copied**. The app's csproj is untouched, so the GUI build is
unaffected. `RHI.Core` is not referenced by the app; it is a second, independent compilation of
the same sources.

### 4.3 Excluded files (the UI-shaped edge)

| File | Why excluded |
|---|---|
| `AddonInfoResolver.cs` | builds detail-panel info rows from `GameCardViewModel` |
| `AutoUpdateService.cs` | background auto-update loop driven by `MainViewModel` |
| `ControlUePostInstallService.cs` | post-install dialog flow (`MainViewModel`) |
| `DlssEnablerService.cs` | initially excluded; later **included** after the `MainViewModel` shim gained `GetDeInstalledAs` |
| `UpdateOrchestrationService.cs` + `IUpdateOrchestrationService.cs` | UI "update all" with `DispatcherQueue` progress |
| `ForegroundActivation.cs` / `ForegroundActivationTarget.cs` | initially excluded, later re-included (they compile once `NativeInterop` is linked) |

The final exclusion set is five files:
`AddonInfoResolver`, `AutoUpdateService`, `ControlUePostInstallService`,
`UpdateOrchestrationService`, `IUpdateOrchestrationService`.

### 4.4 Shims

| Shim | What it provides | Why |
|---|---|---|
| `Microsoft.UI.Dispatching.DispatcherQueue` | `HasThreadAccess => true`, `TryEnqueue(action)` runs inline, `Instance` singleton | services marshal progress to "the UI thread"; in a console that is the current thread |
| `Microsoft.UI.Xaml.Application` + `UnhandledExceptionEventHandler`/`EventArgs` | minimal event surface | `CrashReporter.Register` must compile; the CLI never calls it |
| `GameCardViewModel` | ~90 properties/methods actually used by services (`GameName`, `InstallPath`, `RsStatus`, `DetectedApis`, `DispatcherQueue`, `NotifyAll()`, …) | services take `GameCardViewModel`; the real class is WinUI-bound |
| `App` (static) | `IServiceProvider Services` | services use `App.Services` as a static locator |
| `MainViewModel` | static `GetDcFileName`/`GetLegacyDcFileName`, instance `GetDeInstalledAs` | one service calls these helpers |

**Early-warning property:** if a service starts using a new `GameCardViewModel` member, `RHI.Core`
stops compiling. That is intentional — the failure names the exact member to add to the shim.

### 4.5 Service graph

`RHI.Cli\Cli\CliServices.cs` mirrors `App.xaml.cs` registrations minus UI types:

- shared `HttpClient` (HTTP/2, 10-minute timeout, GitHub token handler, `rhi-cli/0.1` UA)
- `GitHubETagCache`
- every service the app registers except `MainWindow`, `MainViewModel`,
  `FilterViewModel`, `AutoUpdateService`, `UpdateOrchestrationService`, `ControlUePostInstallService`
- `SettingsViewModel` (UI-free, linked from the app)
- `App.Services = provider` after building

The provider is lazy: building it constructs nothing. A command only pays for what it resolves.

### 4.6 Why behaviour cannot silently diverge

Every install/uninstall/update verb calls the same method the GUI calls:
`InstallReShadeAsync`, `InstallAsync` (mods), `LumaService.InstallAsync`,
`OptiScalerService.InstallAsync`, `DxvkService.InstallAsync`, `VulkanLayerService.InstallLayer`,
`SaveNameMappings`, etc. There is no second implementation of the file operations.

The CLI adds only:
- target resolution (`--game`/`--path` → context),
- guards (`--yes`, game/RHI running),
- output (JSON/human),
- orchestration the GUI does in its view model (e.g. which component to update).

---

## 5. Runtime state and shared files

`rhictl` shares `%LOCALAPPDATA%\RHI` with the GUI. This is deliberate: the CLI and the app must
see the same library, settings and install records. The consequence is that anything the CLI
does is visible to the GUI, and vice versa.

| File / folder | Owner | Contents |
|---|---|---|
| `settings.json` | GUI + CLI | global settings: hotkeys, screenshot path, shader selection, channel, UL/DC options, driver options |
| `game_library.json` | GUI + CLI | detected + manual games, caches (engine, bitness, resolved path, addon files), hidden/favourite sets (composite `Name|Store` keys), DXVK caches, DLSS path cache |
| `aux_installed.json` | service | auxiliary component records (ReShade, OptiScaler, Display Commander, ASI, ReLimiter…) |
| `installed.json` | service | RenoDX / mod install records |
| `manifest.json` | service | RHI component manifest (URLs, versions) |
| `dlss_manifest.json` | service | DLSS/Streamline version catalogue |
| `optiscaler_presets.json`, `optiscaler-nightly\available_builds.json` | service | OptiScaler metadata |
| `github_etag_cache.json` | service | ETag cache for GitHub API calls |
| `downloads\renodx\` | service | RenoDX addon cache (e.g. 106 MB Unity generic) |
| `downloads\framelimiter\` | service | ReLimiter/DC binaries + `ul_meta.json` / `dc_meta.json` |
| `reshade\` | service | staged ReShade builds (`ReShade64.dll`, `ReShade32.dll`, normal/no-addons variants) |
| `dlss\`, `streamline\`, `optiscaler\`, `dxvk\`, `luma\`, `rtx40mfg\`, `dlssg2030\`, `dlssnr-cost-scaler\` | service | staged component payloads |
| `logs\session_*.txt` | `CrashReporter` | session logs (CLI launches create these too) |
| `logs\crash_*.txt` | `CrashReporter` | caught/fatal exception reports |

**Important:** `settings.json` may be rewritten by the GUI on startup (migrations). The CLI only
loads it lazily for mutating commands (`CliState.EnsureInitialized`) because loading can trigger
migrations that write.

### 5.1 Why the CLI does not have its own state directory

An isolated `%LOCALAPPDATA%\RHI-CLI` was considered and rejected: the CLI would not see the
user's library, records or settings, would rescan everything, and would double-download caches.
If isolation is ever wanted, it requires an `RHI_STATE_DIR`-style override in `AppPaths` (not
implemented).

---

## 6. CLI conventions

### 6.1 Invocation

```
rhictl <group> <verb> [options]
```

Positional arguments after `group verb` are available to commands (`args.Positional`), but the
documented interface uses named options.

### 6.2 Global flags

| Flag | Meaning | Status |
|---|---|---|
| `--json` | machine-readable envelope; progress suppressed | implemented |
| `--dry-run` | resolve and report; writes nothing; skips downloads | implemented everywhere mutating |
| `--yes` | required for mutating commands | implemented |
| `--force` | bypass game-running / RHI-running guards, and some conflict checks | implemented |
| `--verbose` | per-step diagnostics on stderr | implemented |
| `--no-color` | parsed, currently no colour is emitted anyway | parsed only |
| `--help` | prints help | implemented |
| `--game <name>` | target a library game | implemented |
| `--path <dir>` | target a folder directly (bypasses library name) | implemented |
| `--store <s>` | parsed as an option; **not** used for disambiguation yet | parsed only |
| `--timeout <s>` | **not implemented** | — |

### 6.3 JSON envelope

Success:

```json
{
  "ok": true,
  "command": "reshade.install",
  "data": { "installed": true, "file": "D:\\Games\\X\\dxgi.dll", "version": "6.8.0" },
  "warnings": [],
  "elapsedMs": 1234
}
```

Failure:

```json
{
  "ok": false,
  "command": "renodx.install",
  "error": { "code": "NO_MOD", "message": "No RenoDX mod or generic fallback is available…" },
  "warnings": [],
  "elapsedMs": 87
}
```

Progress is written to **stderr** (throttled to 5% steps) and never pollutes stdout.

### 6.4 Exit codes

| Code | Name | Meaning |
|---|---|---|
| 0 | Ok | success |
| 2 | Usage | bad/missing argument, confirmation required |
| 3 | NotFound | game, file, key, mode or release asset not found |
| 4 | GameRunning | game or `RHI.exe` is running and `--force` was not given |
| 5 | Permission | elevation required (Vulkan layer) or access denied |
| 6 | Network | defined for network failures; most network errors currently surface as 7 |
| 7 | Failed | generic failure / install error |

### 6.5 Logging

Every run creates a `logs\session_*.txt` via the app's `CrashReporter` because services log
unconditionally. This is the same logging path the GUI uses; there is no separate CLI log file.

---

## 7. Command reference

Every command below was implemented. "Verified" means exercised on the test machine; otherwise
the behaviour is from source and dry-runs.

### 7.1 `doctor`

```
rhictl doctor [--json]
```

Checks: OS version, .NET runtime, process architecture, 7-Zip presence (`AppPaths.Find7Zip`),
RHI install (`C:\Program Files\RHI\RHI.exe` + version), `%LOCALAPPDATA%\RHI` existence,
`settings.json`, `game_library.json`, `Logs`, elevation, NVIDIA driver directory.

Human output is a table with `ok` / `MISSING`. JSON returns `{ appDir, checks[] }`.

### 7.2 `paths`

```
rhictl paths [--json]
```

Prints: `appDir`, `state`, `settings`, `library`, `logs`, `downloads`,
`downloads.renodx`, `downloads.shaders`, `downloads.luma`, `downloads.misc`,
`downloads.framelimiter`.

`appDir` is the process directory — which is why the published exe ships with `7z.exe`,
`ReShade.ini`, `ReShade.Vulkan.ini`, `ReShade64.json` next to it.

### 7.3 `status`

```
rhictl status --game "<name>" | --path "<dir>" [--json]
```

Resolves the game (library entry or folder), then reports:

| Field | Source |
|---|---|
| name, store, path, exe | library / `ExeLocator` |
| engine | `GameDetectionService.DetectEngineAndPath` |
| api | `GraphicsApiDetector.Detect`, fallback `DetectUnityFromBootConfig` |
| is32Bit | `PeHeaderService.DetectGameArchitecture == I386` |
| ReShade | aux record + `ReadInstalledVersion`; falls back to `reshade.ini`, shader folder, known DLLs |
| RenoDX | mod record + `AddonFileName` + PE version |
| Luma | `LumaService.GetRecordByPath` |
| OptiScaler | aux record |
| DXVK | record or `DetectInstallation` |
| RE Framework | `GetRecords()` |
| DOF Fix | `IsInstalledIn` |
| Display Commander | aux record **or** `zzz_display_commander*.addon64` file |
| ReLimiter | `relimiter.addon64/32` (or legacy `ultra_limiter.*`) **or** aux record; version from `ul_meta.json` |
| shader count / addon count | files under `reshade-shaders` + addon deploy path |
| DLSS | `IDlssStreamlineService.Detect` (sr/rr/fg/nr/streamline) |

**Verified** on two copies of the same game: records are matched by **install path**, so two
copies never report each other's components.

### 7.4 `library`

```
rhictl library list [--favourites] [--hidden] [--all] [--installed]
                    [--engine unreal|unity|other|<name>] [--store <name>] [--json]
rhictl library show --game "<name>" [--json]
rhictl library refresh [--yes|--dry-run] [--json]
```

- `list` default **excludes hidden games**; `--hidden` shows only hidden; `--all` shows both.
- `--favourites` filters the favourite set.
- `--installed` runs a component probe per game (records + file checks).
- `--engine` compares against `game_library.json`'s `EngineTypeCache`.
- `--store` compares the display store label (Manual/Steam/Epic/GOG/EA/Xbox/Ubisoft/Battle.net/Rockstar).
- `refresh` runs all eight storefront scans (`FindSteamGames` … `FindRockstarGames`), wraps each
  in try/catch (a failing store is a warning, not a failure), de-duplicates by name+path, and
  saves while round-tripping every existing collection.

**Composite-key detail:** favourite/hidden sets are keyed `"GameName|Store"` via
`GameKey.From(name, store).ToKey()`. Using plain names (an earlier bug) made filters miss.
The CLI also cleans up legacy bare-name entries when toggling.

### 7.5 `game`

```
rhictl game add --path "<dir>" [--name "<name>"] [--dry-run]
rhictl game remove --game "<name>" [--yes|--dry-run]
rhictl game launch --game "<name>" [--args "…"] [--dry-run]
rhictl game favourite|unfavourite|hide|unhide --game "<name>"
rhictl game set-path --game "<name>" --path "<dir>" [--yes|--dry-run]
```

- **add**: validates the folder, rejects `\WindowsApps\`, resolves the engine and real install
  path, finds the exe via product-name scoring, derives the name from `ProductName` →
  `FileDescription` → filename, replaces any entry with the same name or path, caches engine and
  resolved path.
- **remove**: removes from store and manual lists; requires `--yes`; reports counts.
- **launch**: uses `LaunchExeOverrides` / `LaunchArgsOverrides` if set, otherwise the detected
  exe; `--args` appends; refuses if the game is already running.
- **favourite/hide**: writes the composite key; save is atomic via the service.
- **set-path**: manual entries only (store entries are refused); clears stale engine/bitness/
  resolved-path cache entries for the old path.

### 7.6 `reshade`

```
rhictl reshade install --game "<n>" [--channel stable|nightly|custom|<version>]
                       [--dll dxgi|d3d9|d3d8|opengl32|<name>]
                       [--shaders off|custom | --packs a,b]
                       [--no-addons] [--yes|--dry-run]
rhictl reshade update  --game "<n>" [--yes|--dry-run]
rhictl reshade uninstall --game "<n>" [--yes|--dry-run]
```

Behaviour:

1. Resolve the game; refuse Vulkan-only games (`VULKAN_UNSUPPORTED` — use `vulkan install`).
2. Guard: game running / RHI running → blocked unless `--force`.
3. Load settings and per-game channel override (`GameNameService.ReShadeChannelOverrides`),
   falling back to the global `SettingsViewModel.ReShadeChannel`.
4. Resolve the proxy DLL: `--dll` wins; otherwise `ReShadeDllName.Resolve`:
   DX11/12 → `dxgi.dll`, DX9 → `d3d9.dll`, DX8 → `d3d8.dll`, OpenGL-only → `opengl32.dll`.
5. Resolve shader packs: `--packs` → explicit; `--shaders off` → empty; otherwise the global
   selection (`GlobalShadersOff`, `UseCustomShaders`, `SelectedShaderPacks`).
6. `--no-addons` (or `--channel no-addons`) selects the normal/no-addons ReShade build.
7. Ensure staging:
   - normal → `INormalReShadeUpdateService.EnsureLatestAsync`
   - stable/nightly → `IReShadeUpdateService.EnsureLatestAsync`
   - legacy version string → the install call downloads on demand
   - **important:** `EnsureLatestAsync` returns `false` when already current. The CLI therefore
     verifies the staged DLL exists instead of trusting the boolean.
8. `aux.InstallReShadeAsync(...)` with hotkeys/screenshot path from settings, `mergeIni: true`.
9. Report record path, `InstalledAs`, PE version, channel.

`update` finds the ReShade/ReShadeNormal record, calls `CheckForUpdateAsync`, and if an update is
available re-runs install with the record's DLL name. `uninstall` deletes the recorded file and
record through `aux.Uninstall(record)`.

**Verified:** test game now has `dxgi.dll` = ReShade 6.8.0 and `[INPUT] KeyOverlay=36,0,0,0`
written into its `reshade.ini`.

### 7.7 `shaders`

```
rhictl shaders list [--json]
rhictl shaders install --game "<n>" [--packs a,b] [--yes|--dry-run]
rhictl shaders remove  --game "<n>" [--yes|--dry-run]
```

- `list` shows the 50 built-in packs from `IShaderPackService.AvailablePacks` with
  id, display name, category, cached flag.
- `install` resolves packs (explicit or global selection), ensures them, computes per-pack
  exclusions, then `SyncGameFolder`.
- `remove` calls `SyncGameFolder(path, empty)` — **this removes managed shaders**.
- Never call `SyncGameFolder(null)` casually: null/empty means "remove managed shaders", not
  "leave alone".

### 7.8 `addons`

```
rhictl addons list [--json]
rhictl addons install --game "<n>" --name "<package>" | --section "<id>" [--yes|--dry-run]
rhictl addons remove  --game "<n>" --name "<package>" | --section "<id>" [--yes|--dry-run]
```

- Catalogue from `AddonPackService.EnsureLatestAsync()` (25 entries observed).
- Selection model: per-game "Select" mode (`GameNameService.PerGameAddonMode` /
  `PerGameAddonSelection`) or global (`SettingsViewModel.EnabledGlobalAddons`).
- Install downloads the package then `DeployAddonsForGame` with the updated selection.
- Remove drops it from the selection and redeploys; the cached package is not deleted.
- `HideFromPicker` entries are filtered from `list`.

### 7.9 `preset`

```
rhictl preset list [--json]
rhictl preset deploy --game "<n>" [--file a.ini,b.ini | all] [--with-shaders] [--yes|--dry-run]
```

- Presets live in `%LOCALAPPDATA%\RHI\inis\reshade-presets`.
- `deploy` copies `.ini` files into the game folder.
- `--with-shaders` parses `Techniques=` from the preset, uses `TechniquesParser.ExtractFxFiles`,
  builds a pack→files map from `GetPackShaderFiles`, resolves with `ShaderResolver.Resolve`,
  expands dependencies, ensures and syncs the packs. Unresolved `.fx` names are reported.
- Without `--with-shaders`, shaders are not installed (matching the GUI's separate flow).

### 7.10 `ini-*` (raw component config)

```
rhictl reshade|renodx|relimiter|display-commander|optiscaler ini-list
        --game "<n>" [--section "<s>"] [--json]
rhictl … ini-get  --game "<n>" --section "<s>" --key "<k>" [--json]
rhictl … ini-set  --game "<n>" --section "<s>" --key "<k>" --value "<v>" [--yes|--dry-run]
```

- File resolution:
  - `reshade`/`renodx` → `<game>\reshade.ini`
  - `relimiter` → `<addon deploy path>\relimiter.ini`, else `<game>\relimiter.ini`
  - `display-commander` → deploy path / game `DisplayCommander.ini`
  - `optiscaler` → `<game>\OptiScaler.ini`, `<game>\OptiScaler\OptiScaler.ini`,
    `<game>\plugins\OptiScaler.ini`
- `IniEditor` preserves line endings and untouched lines, inserts missing keys inside the right
  section, creates missing sections, keeps `<file>.cli-backup`, and writes via a temp file.
- Missing key on `ini-get` → exit 3 with `KEY_NOT_FOUND`.
- `ini-set` refuses while game/RHI is running.

### 7.11 `renodx`

```
rhictl renodx install --game "<n>" [--yes|--dry-run]
rhictl renodx update  --game "<n>" [--yes|--dry-run]
rhictl renodx uninstall --game "<n>" [--yes|--dry-run]
rhictl renodx mode --game "<n>" --value simple|intermediate|advanced [--yes|--dry-run]
rhictl renodx peak-nits --game "<n>" --value <nits> [--yes|--dry-run]
rhictl renodx ini-list|ini-get|ini-set …     (alias for reshade.ini)
```

Install pipeline:

1. Fetch the wiki mod list (`IWikiService.FetchAllAsync`).
2. Build a `DetectedGame` and call `IGameDetectionService.MatchGame(detected, wikiMods, nameMappings)`.
3. If no match, fall back by engine:
   - `Unreal` → `WikiService.GenericUnrealUrl`
   - `Unity` → `WikiService.GenericUnityUrl64` / `GenericUnityUrl32`
   - `UnrealLegacy` (UE3) → explicit `NO_MOD` (no addon system)
4. Pick the 64/32-bit URL, then `IModInstallService.InstallAsync(mod, path, progress, name, store)`.
5. Report mod name, maintainer, generic-vs-wiki, addon filename, PE version.

`update` checks the existing record with `CheckForUpdateAsync`; `uninstall` removes the record and
addon.

`mode` writes `[renodx] SettingsMode` 0/1/2. `peak-nits` writes `ToneMapPeakNits` (PascalCase)
into **every** `[renodx-presetN]` section — the in-game slider only writes the current preset.

**Verified:** generic Unity addon 26.1014.1445 (106 MB) installed and detected; `mode` and
`peak-nits` round-trips verified (1→0→1 and 800→1000).

### 7.12 `luma`

```
rhictl luma list [--json]
rhictl luma install --game "<n>" [--yes|--dry-run]
rhictl luma uninstall --game "<n>" [--yes|--dry-run]
```

- `FetchCompletedModsAsync` downloads the completed-mod list.
- Matching: name mapping first (`GameNameService.NameMappings`), then
  `IGameDetectionService.NormalizeName` equality.
- Install passes the global shader selection, screenshot path, hotkeys, game name, store.
- Uninstall resolves `LumaService.GetRecordByPath(path)` and removes recorded files.
- If no mod matches, `NO_MOD` (exit 3).

### 7.13 `dlss`

```
rhictl dlss status --game "<n>" [--json]
rhictl dlss versions [--component sr|rr|fg|nr|streamline] [--json]
rhictl dlss set --game "<n>" --component <c> --version "<v>" [--yes|--dry-run]
rhictl dlss restore --game "<n>" [--component <c>] [--yes|--dry-run]
rhictl dlss scales [--json]
rhictl dlss render-scale --game "<n>" --component sr|rr [--value <pct>] [--yes|--dry-run]
```

- `status`: `Detect(path)` → `DlssPath/Version`, `DlssdPath/Version`, `DlssgPath/Version`,
  `DlssnrPath/Version`, `StreamlineFolder/Version`.
- `versions`: `FetchManifestAsync` then the component's version list.
- `set`: validates the version against the list (warns if unknown but passes it through), then
  `SwapDlssAsync` / `SwapDlssdAsync` / `SwapDlssgAsync` / `SwapDlssnrAsync` /
  `SwapStreamlineAsync`.
- `restore`: `Restore(target)` or `RestoreStreamline(folder)`; no component = `RestoreAll`.
- `scales` / `render-scale`: NVAPI `Get/SetSrRenderScale` and `Get/SetRrRenderScale`;
  `DlssPresetService.RenderScaleOptions` lists the named options.

**Verified:** `status` on the test game reported SR 310.9.16.0 and NR 310.9.16.0 (Unity crack
bundle ships those DLLs), `scales` listed the full option table, `render-scale` read SR = 0.

### 7.14 `reframework` / `dofix` / `dxvk` / `optiscaler`

```
rhictl reframework install|uninstall --game "<n>" [--yes|--dry-run]
rhictl dofix install|uninstall --game "<n>" [--yes|--dry-run]
rhictl dxvk install|uninstall|update --game "<n>" [--variant development|stable|lilium] [--yes|--dry-run]
rhictl optiscaler install|uninstall|update --game "<n>" [--variant stable|nightly|dlssnr|f5]
                    [--gpu NVIDIA|AMD|Intel] [--build <hint>] [--yes|--dry-run]
```

| Component | Key behaviour |
|---|---|
| RE Framework | `InstallAsync(gameName, path, progress, store)`; uninstall by name+path |
| DOF Fix | eligibility gate `IsGameEligible(engine, is32bit, name)`; `EnsureStagingAsync` + `InstallAsync(path, progress)`; uninstall returns whether anything was removed |
| DXVK | variant enum `Development` / `Stable` / `LiliumHdr`; `EnsureStagingAsync` then `InstallAsync(card, progress, screenshotPath, hotkeys)`; `update` uses `UpdateAsync` |
| OptiScaler | variants `Stable` / `Nightly` / `DlssNr` / `F5`; nightly uses `EnsureNightlyStagingAsync`; install takes a `GameCardViewModel` + GPU type + `dlssInputs: true` + hotkey + variant + nightly build hint; uninstall is synchronous `Uninstall(card)` |

OptiScaler and DXVK construct a headless `GameCardViewModel` with
`DispatcherQueue = DispatcherQueue.Instance` (shim, runs inline).

**Verified:** dry-runs for all four; DXVK default variant resolved to `LiliumHdr` from the user's
persisted setting.

### 7.15 Extras components

```
rhictl asi install|uninstall --game "<n>" [--dll <proxy.dll>] [--yes|--dry-run]
rhictl dlss-enabler install|uninstall --game "<n>" [--dll <proxy.dll>] [--yes|--dry-run]
rhictl fg2030 install|uninstall --game "<n>" [--dll <proxy.dll>] [--yes|--dry-run]
rhictl rtx-encore install|uninstall --game "<n>" [--dll <proxy.dll>] [--nr] [--yes|--dry-run]
rhictl mfg40 install|uninstall --game "<n>" [--dll <proxy.dll>] [--yes|--dry-run]
rhictl dgvoodoo install|uninstall --game "<n>" [--version <v>] [--yes|--dry-run]
rhictl ultraplus url --game "<n>" [--open]
```

| Component | Default proxy | Service calls | Persistence |
|---|---|---|---|
| ASI Loader | `dinput8.dll` (`UltimateAsiLoaderService.DefaultDllName`) | `InstallAsync(card, dll, progress)` → `(success, hookedOriginal)`; `Uninstall(card)` | aux record `UltimateAsiLoader` |
| DLSS Enabler | `version.dll` | `EnsureStagingAsync`; `InstallStandaloneAsync(name, path, store, dll, current)`; `UninstallStandalone(path, dll)` | `GameNameService.DeInstalledAs` |
| 20/30 FG Unlock | `version.dll` | `EnsureStagingAsync`; `Install(path, dll)`; `Uninstall(path, installedAs)` | `Dlssg2030InstalledAs` |
| RTX Encore | `version.dll` | `EnsureStagingAsync`; `InstallAsync(path, dll, current)`; `Uninstall(path, dll, removeNrDll)`; optional `EnsureNrDllCachedAsync` + `DeployNrDll` | `RtxEncoreInstalledAs` |
| MFG Unlocker | `version.dll` | `EnsureStagingAsync()` (**no progress arg**); `EnsureXessStagingAsync()`; `Install(path, dll)`; `Uninstall(path, dll)` | `Rtx40MfgInstalledAs` |
| dgVoodoo2 | n/a | manifest `DgVoodooVersions`; `EnsureStagedAsync(version, url)`; `DeployToGame(path, version, is64Bit: !ctx.Is32Bit)`; `RemoveFromGame(path)` | `DgVoodooStandaloneGames` + `DgVoodooVersionOverride` |
| Ultra+ | n/a | `InitAsync()` then `ResolveUrl(name, manifest)`; `--open` launches the browser | none (link only) |

**Conflict handling:** before installing a proxy DLL, the CLI checks every known component's
installed-as name for this game (aux records + the per-game setting dictionaries). If another
component already owns that DLL it fails with `CONFLICT` unless `--force`. Reinstalling the same
component over its own DLL is allowed.

`SaveState` writes the modified `GameNameService` dictionaries back to `settings.json` via
`SaveNameMappings(...)` + `FlushPendingSave()`.

All seven were **dry-run verified**; none were installed live (to avoid unnecessary DLLs in the
test game).

### 7.16 Limiters

```
rhictl relimiter install|uninstall --game "<n>" [--fresh] [--yes|--dry-run]
rhictl display-commander install|uninstall --game "<n>" [--fresh] [--yes|--dry-run]
```

**ReLimiter**

- Release asset from `https://api.github.com/repos/RankFTW/ReLimiter/releases/latest`
  (`relimiter.addon64` / `relimiter.addon32`), cached in `downloads\framelimiter`.
- Deploy to `ModInstallService.GetAddonDeployPath(path)`.
- Legacy `ultra_limiter.addon64/32` files are removed on install.
- `ul_meta.json` records `InstalledVersion64/32` + timestamp for status/version reporting.
- Deploys `relimiter.ini` if absent, then applies settings: shared presets, DLSS hooks, target FPS.
- Calls `DlssPresetService.SetPerGameFpsLimit(..., 0)` to disable the driver-level limiter
  (mutual exclusion with DC).
- Uninstall deletes the addon, ini, legacy files, `relimiter*.log`, `relimiter*.csv`, and clears
  the driver FPS limit.
- **No aux record is created** — status detects it from the deployed file + meta.

**Display Commander**

- Asset from `https://api.github.com/repos/pmnoxx/display-commander/releases/tags/latest`
  (`zzz_display_commander.addon64/32`).
- Deploy + `DisplayCommander.ini` if absent.
- Creates an aux record `DisplayCommander` with `InstalledAs`.
- Disables the driver FPS limit on install, clears it on uninstall.
- Mutual exclusion: refuses if a ReLimiter addon is present (and vice versa).

**Verified:** ReLimiter 3.3.5 installed live on the test game and shown by `status`.

### 7.17 `vulkan`

```
rhictl vulkan status [--json]
rhictl vulkan install --game "<n>" [--layer-only] [--yes|--dry-run]   (admin)
rhictl vulkan uninstall [--game "<n>" | --layer-only] [--yes|--dry-run]
```

- `status`: `VulkanLayerService.IsLayerInstalled()` + version from
  `LayerDirectory\ReShade64.dll` + elevation state.
- `install`: requires elevation (HKLM + `C:\ProgramData\ReShade`). Ensures staged ReShade, calls
  `VulkanLayerService.InstallLayer()`, then for a game:
  `AuxInstallService.MergeRsVulkanIni(path, name, screenshotPath, hotkeys)` and
  `VulkanFootprintService.Create(path)`.
- `uninstall --layer-only`: removes the layer (admin).
- `uninstall --game`: deletes `reshade.ini`, `VulkanFootprintService.Delete`,
  `ShaderPackService.RemoveFromGameFolder` + `RestoreOriginalIfPresent`, and deploys an empty
  addon selection.
- Dry-run prints the plan and never writes.
- **Verified:** `status` only (layer not installed; install requires an elevated terminal).

### 7.18 `nvidia`

```
rhictl nvidia info [--json]
rhictl nvidia status --game "<n>" [--json]
rhictl nvidia preset --game "<n>" --component sr|rr|fg|nr --value <n> [--yes|--dry-run]
rhictl nvidia override --game "<n>" --component sr|rr|fg [--off] [--yes|--dry-run]
rhictl nvidia get|set --game "<n>" --key <k> [--value <n>] [--yes|--dry-run]
rhictl nvidia global-get|global-set --key <k> [--value <n>] [--yes|--dry-run]
rhictl nvidia backup --game "<n>"
rhictl nvidia dldsr current|captures|capture --label <l>|apply --label <l>|smoothness --value <n>
```

- `DlssPresetService.Initialize()` is called first; `IsSupported` gates everything.
- `status` reads SR/RR/FG/NR presets + override flags, vsync, power management, low-latency,
  smooth-motion, per-game FPS limit, per-game G-Sync.
- Per-game keys for `get/set`: `vsync`, `vsync-tear`, `power`, `low-latency`, `smooth-motion`,
  `fps-limit`, `gsync`.
- Global keys: `vsync`, `power`, `shader-cache`, `shader-precompile`, `gsync-mode`,
  `gsync-enabled`, `fps-limit`, `dmfg-frames`, `dmfg-target-fps`, `preferred-refresh`,
  `rebar-mode`, `rebar-size`.
- **NR override setter does not exist** in this build; the CLI reports that only sr/rr/fg can be
  toggled.
- `backup` writes a timestamped JSON snapshot of the per-game driver values to
  `%LOCALAPPDATA%\RHI\cli-backups`.
- DLDSR verbs map to `IDldsrService`: `GetCurrentState`, `LoadCaptures`, `CaptureCurrentState`,
  `ApplyStateAsync`, `GetCurrentSmoothness`, `SetSmoothnessAsync`.
- **Verified:** `info` (driver 621.01) and `status` on the test game; writes not exercised.

### 7.19 `hdr` / `res` / `vibrance`

```
rhictl hdr status|on|off|list
rhictl res current|list|set --width <w> --height <h> [--hz <hz>]|restore
rhictl vibrance list|set --display <i> --value <0-100>|restore
```

- `hdr`: `HdrToggleService.IsHdrEnabled/EnableHdr/DisableHdr/GetAllDisplays`.
- `res`: `ResolutionToggleService.GetCurrentResolution/GetSupportedResolutions/SetResolution/
  RestoreResolution`; `set` validates against the supported mode list.
- `vibrance`: `DigitalVibranceService.IsSupported/EnumerateDisplays/GetLevel/SetLevel/
  RestoreSavedLevels` with `SettingsViewModel.DigitalVibranceSettings`.
- **Verified:** `hdr status` (disabled), `res current` (2560×1440 @ 170 Hz), `res list`.
- **Known limitation:** `vibrance list` returned no displays on this machine/driver; the raw
  NVAPI display enumeration is the limiting factor, not the CLI.

### 7.20 `update all`

```
rhictl update all [--components reshade,renodx,luma,dxvk,optiscaler,reframework,dofix]
                  [--game "<n>"] [--yes|--dry-run]
```

- Iterates the library (or one game), skips games that are running.
- Determines installed components per game from records/files.
- Spawns `rhictl <component> update|install --path <game> --json [--dry-run|--yes]` as a child
  process and aggregates exit codes/details. This deliberately reuses the tested per-component
  flows instead of duplicating them.
- Default component set: `reshade, renodx, luma, dxvk, optiscaler, reframework, dofix`.
- **Verified:** `--components reshade --dry-run` reported “up to date”.

### 7.21 `mcp`

```
rhictl mcp serve
```

A stdio JSON-RPC MCP server. Methods: `initialize`, `notifications/initialized`, `ping`,
`tools/list`, `tools/call`, `shutdown`.

Tools (11 + escape hatch):

| Tool | Args |
|---|---|
| `rhi_doctor` | — |
| `rhi_library_list` | — |
| `rhi_status` | game / path |
| `rhi_game_add` | path, name |
| `rhi_reshade` | action, game/path, channel, dll, packs, noAddons |
| `rhi_renodx` | action, game/path |
| `rhi_luma` | action, game/path |
| `rhi_dlss` | action, game/path, component, version |
| `rhi_component` | component, action, game/path, dll, variant, gpu |
| `rhi_run` | raw args array |

Each tool shells out to the same executable with `--json`, so the MCP surface cannot drift from
the CLI surface. Mutating tools add `--yes` automatically unless `dryRun` is set.

**Verified:** full handshake, `tools/list`, a `tools/call` to `rhi_status`, and `shutdown`.

---

## 8. Component technical reference

### 8.1 ReShade

**Channels**

| Channel | Meaning |
|---|---|
| `Stable` | latest stable release from reshade.me |
| `Nightly` | nightly build |
| `Custom` | user-supplied DLL placed in the custom staging folder |
| any other string | treated as a legacy version tag (downloaded on demand) |
| No Addons | separate "normal" build staged from the app's bundled/templated source |

**Staging**

The app stages builds under `%LOCALAPPDATA%\RHI\reshade`:
`ReShade64.dll` / `ReShade32.dll` (full build) and the normal/no-addons variants.
`EnsureLatestAsync` refreshes them; the install call copies from staging into the game.
`rhictl` verifies the expected staged file exists rather than trusting the boolean return
(false means "already current", not "failed").

**Deployment**

`AuxInstallService.InstallReShadeAsync` copies the staged DLL to the requested proxy name,
writes the record, and merges the ini template. Proxy name resolution follows the game API
(see 7.6). The record contains `GameName`, `InstallPath`, `Store`, `AddonType` (ReShade /
ReShadeNormal), `InstalledAs`, `Channel`, `InstalledAt`.

**INI templates**

| Template | Used for |
|---|---|
| `ReShade.ini` | DX installs |
| `ReShade.Vulkan.ini` | Vulkan installs |
| `reshade.rdr2.ini` | RDR2 / Max Payne 3 (DX) |

Merge is template-driven: the shipped template's keys (`[ADDON] DisabledAddons`,
`[GENERAL] EffectSearchPaths/TextureSearchPaths`, `[OVERLAY] TutorialProgress/VariableListUseTabs`,
`[INPUT] KeyOverlay` for RDR2) are seeded into the game's file. `MergeRsVulkanIni` additionally
propagates `[ADDON]`, `[GENERAL]`, `[SCREENSHOT]` and `[INPUT] KeyOverlay/KeyScreenshot` into
numbered copies (`ReShade2.ini`, `ReShade3.ini`, …).

**Vulkan**

The Vulkan path is a global implicit layer in `C:\ProgramData\ReShade` plus per-game
`reshade.ini` + a footprint file. It requires admin. Per-game cleanup is available without admin.

### 8.2 RenoDX

**Where mods come from**

- Wiki mod list (`IWikiService.FetchAllAsync`) — per-game addons with snapshot URLs.
- Generic fallbacks for engines without a dedicated mod:
  - Unreal → `WikiService.GenericUnrealUrl`
  - Unity → `WikiService.GenericUnityUrl64` / `GenericUnityUrl32`
- UE3 (`UnrealLegacy`) has no addon system.

**Matching**

`IGameDetectionService.MatchGame(detectedGame, mods, nameMappings)` — handles normalisation and
user name mappings. The CLI creates a `DetectedGame` from the resolved context.

**Deployment**

`ModInstallService.InstallAsync(mod, path, progress, gameName, store)` downloads the addon and
writes it to `GetAddonDeployPath(path)` (root for most games; `…_Data\Plugins` for some Unity
layouts). Records live in `installed.json` and include the addon filename, version and source.

**The generic Unity addon is large** — 109,613,568 bytes in the test game. Downloads are cached
under `downloads\renodx`, so reinstalling does not re-download.

**Settings**

All live in the game's `reshade.ini`:
- `[renodx]` — framework flags (`SettingsMode`, `Blit_Copy_Hack`, `Use_Swapchain_Proxy`,
  `Upgrade_*`, …).
- `[renodx-presetN]` — tone mapping, color grading, effects (see the in-game menu section).

RHI itself only authors `ToneMapPeakNits` and `FgSlHdr10`; the rest are addon runtime keys.

### 8.3 Luma

- Completed-mod list via `FetchCompletedModsAsync`.
- Matching by name mapping then `NormalizeName`.
- Installs the Luma plugin files into the game folder; configuration is a `.toml` next to the
  plugin. HDR10 does not work with FSR frame generation (Luma falls back to scRGB).
- Record in `LumaService` storage; `GetRecordByPath` is a static lookup used by `status`.

### 8.4 OptiScaler

- Variants: `Stable`, `Nightly`, `DlssNr`, `F5`; per-GPU ini templates
  (`OptiScaler.nvidia.ini`, `.amd-dlss.ini`, `.amd-nodlss.ini`).
- Install: seed ini from the per-GPU template, enforce `LoadReshade=true` and
  `LoadAsiPlugins=true` (top-level lines, **no section header**), write `ShortcutKey` as a VK
  code (e.g. `0x2E`), then copy the DLLs.
- `SetOptiScalerIniValue` is the generic section-aware writer used by every cog setting.
- In-game menu is generated by OptiScaler itself; RHI does not author it.

### 8.5 DXVK

- Variants: `Development`, `Stable`, `LiliumHdr`.
- `DetectInstallation(path, api)` distinguishes a tracked install from a detected one.
- Records include version and API; `UninstallAsync(card)` removes tracked files.

### 8.6 RE Framework / DOF Fix

- RE Framework: versioned release, installed into the game root; records via `GetRecords()`.
- DOF Fix: engine-gated (`IsGameEligible`), staged, installed into the game root;
  `IsInstalledIn` is a simple file probe.

### 8.7 ASI Loader / DLSS Enabler / FG unlocks / Encore / MFG / dgVoodoo

These are proxy-DLL or ASI-plugin deployments:

- **ASI Loader** renames an existing proxy to `*.original` for chain-loading and reports the
  hooked name.
- **DLSS Enabler** is a standalone proxy install with a per-game setting.
- **20/30 FG** and **MFG** copy a proxy + companion payload; MFG also stages XeSS files.
- **RTX Encore** can optionally deploy the NR DLL (`--nr`).
- **dgVoodoo2** deploys a versioned set of DLLs; the version comes from the manifest.

### 8.8 DLSS / Streamline

- `IDlssStreamlineService.Detect(path)` finds `nvngx_dlss.dll`, `nvngx_dlssd.dll`,
  `nvngx_dlssg.dll`, `nvngx_dlssnr.dll`, and the Streamline folder.
- `FetchManifestAsync` loads the version catalogue; swap methods copy the selected version and
  keep a backup; `Restore`/`RestoreAll` put the backups back.
- Render scale is a driver setting via NVAPI, not a DLL swap.

### 8.9 NVIDIA driver settings

- `DlssPresetService` wraps NvAPIWrapper DRS: preset values are numeric NVAPI indices, overrides
  are per-game DRS flags.
- `DldsrService` captures/applies DLDSR state and smoothness.
- `DigitalVibranceService` uses raw NVAPI display APIs.
- The CLI gates writes behind `--yes` and provides `nvidia backup` for per-game values.

### 8.10 Shader packs / addon packs / presets

- Shader packs: 50 entries; categories; each pack has file lists, exclusions, dependencies, and
  an optional custom sentinel.
- Addon packs: catalogue of 25 entries with section/package IDs; global or per-game selection.
- Presets: `.ini` files under `%LOCALAPPDATA%\RHI\inis\reshade-presets`; deploy with optional
  technique-driven shader resolution.

---

## 9. In-game menu reference

Captured live on the test game (Unity, DX11, 64-bit) with ReShade 6.8.0, RenoDX generic Unity
26.1014.1445 and ReLimiter 3.3.5. Evidence: `docs/in-game-captures/` (10 window-only PNGs).

### 9.1 How it opens and how it was driven

- Default key: **Home** (`[INPUT] KeyOverlay=36,0,0,0`).
- The overlay is rendered inside the game's swapchain, so a window capture includes it.
- ReShade accepts window messages: `WM_KEYDOWN` (`0x0100`), `WM_LBUTTONDOWN/UP`
  (`0x0201`/`0x0202`), `WM_MOUSEWHEEL` (`0x020A`). This means the menu can be driven **without
  stealing focus** — the captures were made that way.
- Window capture used `PrintWindow` with `PW_RENDERFULLCONTENT` (`2`) on the game's client area;
  no desktop capture was used.

### 9.2 Tab inventory

| Tab | Exists when | Purpose |
|---|---|---|
| Home | always | preset bar, effect list, reload, performance mode |
| Add-ons | always (lists installed) | addon enable/disable |
| Settings | always | ReShade General / Screenshots / Overlay & Styling |
| Statistics | always | frame stats (not configuration) |
| Log | always | ReShade log |
| About | always | version/credits |
| ReLimiter | ReLimiter addon installed | limiter + OSD |
| RenoDX | RenoDX addon installed | tone mapping / grading / effects |

Missing tabs mean the addon is not installed, not that settings are hidden.

### 9.3 Home

- Preset selector + **Active to top** + **Auto Save** + folder/save/plus.
- Search box.
- Effect tree; when search paths are empty ReShade prints the exact “No effect files (.fx)
  found…” message and points at the configured paths.
- **Reload**, **Performance Mode**.
- FPS counter.
- Global preprocessor definitions editor.

### 9.4 Add-ons

- **Add-on search path: `.\`**
- Observed: **ReLimiter** ✅, **RenoDX** ✅, **Generic Depth** ☐, **Effect Runtime Sync** ☐.
- The two unchecked entries come from `[ADDON] DisabledAddons=Generic Depth,Effect Runtime Sync`.

### 9.5 Settings — General

| UI control | ini key | Value notes | Confidence |
|---|---|---|---|
| Input processing = "Block all input when overlay is visible" | `[INPUT] InputProcessing` | `0` pass all · `1` block over overlay · `2` block all when visible | high |
| Overlay key = Home | `[INPUT] KeyOverlay` | `36,0,0,0` | verified |
| Effect toggle / reload keys | `[INPUT] KeyEffects` / `KeyReload` | `vk,ctrl,shift,alt` | high |
| Previous / next preset keys | `[INPUT] KeyPreviousPreset` / `KeyNextPreset` | same format | high |
| Preset transition duration = 1000 | `[GENERAL] PresetTransitionDuration` | ms | high |
| Start-up preset | `[GENERAL] StartupPresetPath` | empty = last used | high |
| Effect search paths | `[GENERAL] EffectSearchPaths` | `.\reshade-shaders\Shaders\**` | verified |
| Texture search paths | `[GENERAL] TextureSearchPaths` | `.\reshade-shaders\Textures\**` | verified |
| Load only enabled effects | `[GENERAL] SkipLoadingDisabledEffects` | 0/1 | high |
| Clear effect cache | *(button, no key)* | deletes cache | high |

### 9.6 Settings — Screenshots

| UI control | ini key | Notes | Confidence |
|---|---|---|---|
| Screenshot key = Print Screen | `[INPUT] KeyScreenshot` | `44,0,0,0` | high |
| Screenshot path = `.\` | `[SCREENSHOT] SavePath` | directory | high |
| Screenshot name = `%AppName% %Date% %Time%_%Count%` | likely folded into `SavePath` in 6.x; separate name key **not confirmed** | macros | **likely** |
| Format = PNG | `[SCREENSHOT] FileFormat` | 0 BMP / 1 PNG / 2 JPEG | high |
| Save current preset | `[SCREENSHOT] SavePresetFile` | 0/1 | high |
| Save before/after | `[SCREENSHOT] SaveBeforeShot` | 0/1 | high |
| Save with overlay | `[SCREENSHOT] SaveOverlayShot` | 0/1 | high |
| Sound = sound.wav | `[SCREENSHOT] SoundPath` | path | high |
| Post-save command/args/dir/hide | `PostSaveCommand`, `PostSaveCommandArguments`, `PostSaveCommandWorkingDirectory`, `PostSaveCommandNoWindow` | path/string/path/0-1 | high |

### 9.7 Settings — Overlay & Styling

| UI control | ini key | Notes | Confidence |
|---|---|---|---|
| Language = System Default | likely `[OVERLAY] Language` | locale / empty | **likely** |
| Restart tutorial | `[OVERLAY] TutorialProgress` | set 0 | high |
| Show screenshot message | `[OVERLAY] ShowScreenshotMessage` | 0/1 | high |
| Show preset transition message | `[OVERLAY] ShowPresetTransitionMessage` | 0/1 | high |
| Group effects with tabs | `[OVERLAY] VariableListUseTabs` | 0/1 | verified |
| Global style = Default/Dark | `[STYLE] StyleIndex` | integer enum | **likely** |
| Text editor style = Dark | `[STYLE] EditorStyleIndex` | integer enum | **likely** |
| Fonts/sizes | `[STYLE] Font`, `EditorFont`, `FontSize`, `EditorFontSize` | `ProggyClean.ttf`, 13 | high |
| Global alpha = 1.00 | `[STYLE] Alpha` | 0–1 | high |
| HDR overlay brightness = 203 nits | `[STYLE] HdrOverlayBrightness` | float nits | high |
| Overlay color space = Auto | `[STYLE] HdrOverlayOverwriteColorSpaceTo` | 0 auto / 1 sRGB / 2 ext sRGB / 3 HDR10 PQ / 4 HLG | medium |
| Frame rounding = 0 | `[STYLE] FrameRounding` | float | high |
| Show clock/FPS/frame time | `[OVERLAY] ShowClock`, `ShowFPS`, `ShowFrameTime` | 0/1/2 | high |
| Show preset name | likely `[OVERLAY] ShowPresetName` | 0/1 | **likely** |
| FPS position | `[OVERLAY] FPSPosition` | 0–3 | high |

Per-game `reshade.ini` overrides the global `C:\ProgramData\ReShade\ReShade.ini` at section/key
level; the global file is the fallback for the Vulkan/global layer.

### 9.8 RenoDX tab

Persistence: `reshade.ini` → `[renodx]` (framework + `SettingsMode`) and `[renodx-presetN]`
(per-preset values).

**Modes**

| `SettingsMode` | UI label | Evidence |
|---|---|---|
| 0 | Simple | observed before switching |
| 1 | Intermediate | **verified** (UI label + file value) |
| 2 | Advanced | likely (next step of the same button; not captured) |

**Tone Mapping** (captured values in brackets)

| Key | UI label | Meaning | Confidence |
|---|---|---|---|
| `toneMapType` | Tone Mapper | selects the tonemap curve; **2 = Neutwo verified**; lower values are vanilla/SDR paths | verified for 2; others addon-specific |
| `toneMapPeakNits` | Peak Brightness | target peak white [1000] | high |
| `toneMapGameNits` | Game Brightness | reference white / SDR game brightness [203] | high |
| `toneMapUINits` | UI Brightness | UI luminance target [203] | high |
| `toneMapGammaCorrection` | (SDR EOTF area) | gamma/EOTF correction flag [1] | likely |
| `toneMapScaling` | (scaling) | tonemap input/output scaling [2] | **undocumented** |
| `toneMapSDRHue` | SDR Hue | hue handling for SDR→HDR [100] | likely |
| `toneMapSDRChrominance` | SDR Chrominance | chrominance handling [100] | likely |
| `ToneMapSDRClip` | SDR Source Clipping | clips SDR highlights before expansion [1.5] | likely |
| `rolloffUI` | (UI roll-off) | highlight roll-off for UI [1] | likely |

The UI showed **SDR EOTF Emulation = 2.2** as a separate row; no unambiguous single key was found
for it in the captured file (it may be derived from `toneMapGammaCorrection`/`toneMapScaling`).
Marked unmapped.

**Color Grading**

| Key | UI label | Captured | Meaning | Confidence |
|---|---|---|---|---|
| `colorGradeExposure` | Exposure | 1.00 | linear exposure | high |
| `colorGradeHighlights` | Highlights | 50 | highlight lift/compression | high |
| `colorGradeShadows` | Shadows | 50 | shadow lift/crush | high |
| `colorGradeContrast` | Contrast | 50 | contrast around mid-grey | high |
| `colorGradeSaturation` | Saturation | 50 | global saturation | high |
| `colorGradeDechroma` | Highlights Saturation | 0 | desaturate very bright pixels | likely |
| `colorGradeBlowout` | Blowout | 50 | highlight blowout to white | high |
| `colorGradeFlare` | Flare | 0 | highlight flare | high |
| `colorGradeClip` | Clipping | 100 | output clip point | high |
| `colorGradeInternalLUTScaling` | Internal LUT scaling | 0 | internal-LUT domain rescale | likely |
| `colorGradeInternalLUTShaper` | LUT Shaper | 1 | **0 = Vanilla** (confirmed by the warning text); 1/2 custom paths | 0 verified; 1/2 names undocumented |
| `colorGradeInternalLUTStrength` | Internal LUT Strength | 100 | blend strength | high |
| `colorGradeLUTSampling` | (LUT sampling) | 1 | sampling filter enum | **undocumented** |
| `colorGradeUserLUTScaling` | User LUT Scaling | 0 | user-LUT domain rescale | likely |
| `colorGradeUserLUTStrength` | User LUT Strength | 100 | user-LUT blend | high |
| `colorGradeGamutCompression` | LUT Gamut Conservation | 0 | compress instead of clipping | likely |
| `colorGradeColorSpace` | Output Color Space | 0 | working/output color space enum | **undocumented values** |

**Warning:** the test game had `colorGradeInternalLUTShaper=1` **and** the panel showed
“Missing output shader. Automatic shader dump requested. Set LUT Shaper to Vanilla for correct
output. (may clamp to SDR)”. Meaning: a non-Vanilla shaper needs a replacement output shader the
addon could not find; setting the shaper to `0` (Vanilla) restores the vanilla output path.

**Effects**

| Key | UI label | Captured | Meaning | Confidence |
|---|---|---|---|---|
| `fxBloom` | Bloom | 50 | bloom | high |
| `fxLens` | Lens Dirt/Flare | 50 | lens dirt/flare | high |
| `fxDoF` | Depth of Field | 100 | DoF | high |
| `fxCA` | Chromatic Aberration | 50 | chromatic aberration | high |
| `fxVignette` | Vignette | 50 | vignette | high |
| `fxNoise` | Dithering Noise | 50 | dithering/noise | high |
| `fxFilmGrain` | Film Grain | 50 | film grain | high |
| `fxFilmGrainType` | Film Grain Type | 0 | **0 = Vanilla verified**; others addon-specific | verified for 0 |
| `fxHdrBoost` | FakeHDR Boost | 0 | extra highlight expansion | high |

**Framework keys observed in `[renodx]`** (not in the in-game panel): `Blit_Copy_Hack`,
`ForceBorderless`, `Force_Pipeline_Cloning`, `PreventFullscreen`, `Swapchain_Encoding`,
`Scaling_Offset`, `Tonemap_Offset`, `Use_Resource_Cloning`, `Use_Swapchain_Proxy`, `Upgrade_*`.

### 9.9 ReLimiter tab

Persistence: `relimiter.ini` → `[FrameLimiter]`. **Trap:** the shipped template only contains
`[UltraLimiter]`, while every writer (RHI and the addon) uses `[FrameLimiter]`.

**Frame pacing**

| Key | Meaning | Values | Captured |
|---|---|---|---|
| `target_fps` | foreground cap | `0` off, 30–1000 | 0 |
| `background_fps` | cap when unfocused | `0` uncapped | 30 |
| `fg_off_fps` | cap while FG off | `0` disabled | 0 |
| `vsync_mode` | VSync override | `game` / `off` / `on` | game |
| `enforcement_marker` | where the limiter sleeps | `SimulationStart` / `RenderSubmitStart` / `Present` | SimulationStart |
| `initial_wake_guard_us` | wake guard (µs) | 0–10000 | 800 |
| `window_mode` | presentation | `default` / `borderless` / `fullscreen` | default |
| `fake_fullscreen` | convert exclusive FS | bool | false |
| `selected_monitor` | target monitor | index | 0 |
| `focus_lock` | keep foreground cap when unfocused (likely) | bool | false |

**OSD layout**

| Key | Meaning | Captured |
|---|---|---|
| `osd_enabled` | show OSD | false |
| `osd_x` / `osd_y` | position (fraction) | 0.005 / 0.005 |
| `osd_opacity` | background opacity | 0.60 |
| `osd_scale` | scale | 1.00 |
| `osd_drop_shadow` | text shadow | true |
| `osd_text_brightness` | text brightness | 1.00 |
| `osd_toggle_key` | toggle key | PageUp |

**OSD metric toggles** (all `false` in the capture; enable one per metric):

- Performance: `osd_show_fps`, `osd_show_frametime`, `osd_show_frametime_graph`,
  `osd_show_1pct_low`, `osd_show_0_1pct_low`
- Latency: `osd_show_cpu_latency`, `osd_show_gpu_render_time`, `osd_show_total_frame_cost`,
  `osd_show_fg_time`
- Quality: `osd_show_pqi`, `osd_show_pqi_breakdown`, `osd_show_smoothness`
- Pipeline: `osd_show_fg`, `osd_show_limiter`, `osd_show_adaptive_smoothing`
- System: `osd_show_gpu_temp`, `osd_show_gpu_clock`, `osd_show_gpu_usage`, `osd_show_gpu_power`,
  `osd_show_vram`, `osd_show_cpu_usage`, `osd_show_ram`
- DLSS: `osd_show_dlss_quality`, `osd_show_dlss_features`, `osd_show_dlss_resolution`,
  `osd_show_dlss_presets`, `osd_show_dlss_versions`

**Presets / screen / advanced**

| Key | Meaning | Values / captured |
|---|---|---|
| `shared_presets` | share OSD presets across games | bool, false |
| `osd_preset_prev_key` / `next_key` | cycle presets | key, empty |
| `osd_position_cycle_key` | cycle OSD position | key, empty (likely) |
| `blackout_key` | blackout other monitors | key, empty |
| `oled_care_key` | OLED care toggle | key, empty (likely) |
| `oled_care_all_monitors` | all monitors | bool, true (likely) |
| `oled_care_idle_minutes` | auto-activate idle | minutes, 0 off (likely) |
| `adaptive_smoothing` | P99 render-time smoothing | bool, false |
| `smoothing_percentile` | smoothing percentile | 0.50–0.999, 0.99 |
| `smoothing_window` | sample window | `medium` / `dual`, medium |
| `smoothing_bias_us` | extra headroom | µs, 0.0 |
| `dynamic_mfg_passthrough` | driver pacing under DLSS 4.5 Dynamic MFG | bool, false |
| `reflex_inject` | synthesize Reflex markers | bool, false |
| `flip_model_override` | force DX11 FLIP_DISCARD | bool, false |
| `log_level` | verbosity | `error`/`warn`/`info`/`debug`, warn |
| `csv_enabled` | per-frame CSV | bool, false |
| `dlss_info_hooks` | DLSS/NGX info hooks | bool, true (likely) |
| `streamline_compat` | Streamline FG detection | `true` / `force_false`, true |

Sources: ReLimiter `docs/USER_GUIDE.md`; unrecognised keys marked likely/undocumented.

---

## 10. Persistence map (menu → file → CLI)

| UI surface | File / section / key | RHI writer | CLI |
|---|---|---|---|
| ReShade overlay key | `reshade.ini [INPUT] KeyOverlay` | `ApplyOverlayHotkey` (`AuxInstallService.Ini.cs:1367`) | `rhictl reshade ini-set --section INPUT --key KeyOverlay --value 36,0,0,0` |
| ReShade screenshot key | `[INPUT] KeyScreenshot` | `ApplyScreenshotHotkey` (`:1388`) | `rhictl reshade ini-set … KeyScreenshot` |
| Screenshot folder | `[SCREENSHOT] SavePath` | `ApplyScreenshotPath` (`:1276`) | `rhictl reshade ini-set … SavePath` |
| Effect list tabs | `[OVERLAY] VariableListUseTabs` | `ApplyVariableListUseTabs` (`:1408`) | `rhictl reshade ini-set … VariableListUseTabs` |
| Addon enable | `[ADDON] DisabledAddons` | template merge (`MergeRsIni`) | `rhictl reshade ini-set … DisabledAddons` |
| RenoDX mode | `[renodx] SettingsMode` | `ApplyRenoDxNativeHdrSettings` (`:371–452`) + cog | `rhictl renodx mode --value …` |
| RenoDX peak nits | `[renodx-presetN] ToneMapPeakNits` | `ApplyPeakNits` (`:1296–1331`) | `rhictl renodx peak-nits --value …` |
| Any RenoDX preset key | `[renodx-presetN] …` | RenoDX addon at runtime | `rhictl renodx ini-set …` |
| ReLimiter cap | `relimiter.ini [FrameLimiter] target_fps` | `ApplyUlTargetFps` (`:1486`) | `rhictl relimiter ini-set …` |
| ReLimiter OSD key | `[FrameLimiter] osd_toggle_key` | `ApplyUlOsdHotkey` (`:1426`) | `rhictl relimiter ini-set …` |
| DC cap | `DisplayCommander.ini [DisplayCommander] fps_limit` | `DisplayCommander.cs:86` | `rhictl display-commander ini-set …` |
| OptiScaler FG | `OptiScaler.ini [FrameGen] Enabled` | `SetOptiScalerIniValue` (`OptiScalerService.Install.cs:2002`) | `rhictl optiscaler ini-set …` |
| UE HDR keys | `Engine.ini [SystemSettings]` | `ApplyEngineIniHdrSettings` (`:847–947`) | edit file (read-only lock) |

**Runtime ownership**

- ReShade owns `[GENERAL]`, `[INPUT]`, `[SCREENSHOT]`, `[OVERLAY]`, `[STYLE]` at runtime.
- RenoDX owns the preset section after a slider change.
- ReLimiter / Display Commander own their inis while the game runs.
- Engine.ini is set read-only by RHI; the game does not re-add keys.

**Write while the game is closed.** An in-game change overwrites a CLI edit made mid-session.

---

## 11. GUI → CLI coverage matrix

Source: live UI Automation dump of RHI 2.8.9 (359 elements). Full detail in
`docs/GUI-INVENTORY.md`; raw tree in `docs/GUI-INVENTORY-raw.txt`.

### Top bar

| GUI | AutomationId | CLI |
|---|---|---|
| Refresh | `RefreshBtn` | `library refresh --yes` |
| Shaders/Addons | `ShadersAddonsBtn` | `shaders list`, `addons list` |
| Update All | `UpdateBtn` | `update all --yes` |
| Settings | `SettingsBtn` | partial (`config` not implemented) |
| Search | `SearchBox` | `library list` + shell filtering |
| Add game | `AddGameBtn` | `game add --path` |
| Quick Start / Available Mods / Links / Help / Donate | — | docs / n/a |

### Filters

| GUI | CLI |
|---|---|
| All Games | `library list` |
| Installed | `library list --installed` |
| Favourites / Hidden | `--favourites` / `--hidden` |
| Unreal / Unity / Other | `--engine unreal|unity|other` |
| RenoDX / Luma | `status --json` filter |

### Detail header

| GUI | CLI |
|---|---|
| Launch | `game launch` |
| HDR toggle | `hdr on|off` |
| RES toggle | `res set` / `res restore` |
| Hide / Favourite | `game hide|unhide`, `game favourite|unfavourite` |
| Browse folder | `status` gives the path |
| Copy Report | `status --json` |

### ReShade / RenoDX

| GUI | CLI |
|---|---|
| ReShade install/reinstall/update | `reshade install|update --yes` |
| ReShade.ini cog | `reshade ini-list|ini-get|ini-set` |
| ReShade uninstall | `reshade uninstall --yes` |
| RenoDX install/reinstall/update | `renodx install|update --yes` |
| RenoDX uninstall | `renodx uninstall --yes` |
| RenoDX settings mode | `renodx mode` |
| RenoDX peak nits | `renodx peak-nits` |

### Components / extras

| GUI | CLI |
|---|---|
| ReLimiter install/uninstall/ini | `relimiter …`, `relimiter ini-*` |
| Display Commander install/uninstall/ini | `display-commander …`, `display-commander ini-*` |
| OptiScaler | `optiscaler install|uninstall|update` |
| DXVK | `dxvk install|uninstall|update` |
| DLSS/Streamline swap/restore | `dlss set|restore` |
| DLSS render scale | `dlss render-scale` |
| DOF Fix / RE Framework | `dofix …`, `reframework …` |
| ASI Loader | `asi install|uninstall` |
| DLSS Enabler | `dlss-enabler install|uninstall` |
| 20/30 FG | `fg2030 install|uninstall` |
| RTX Encore | `rtx-encore install|uninstall --nr` |
| Universal MFG | `mfg40 install|uninstall` |
| dgVoodoo2 | `dgvoodoo install|uninstall --version` |
| Ultra+ | `ultraplus url --open` |
| Deploy DLL / Quick Apply | per-component `--dll` |
| Change install folder | `game set-path` |
| Reset overrides | **not implemented** |

### Overrides / settings

| GUI | CLI |
|---|---|
| DLL name dropdowns | `--dll` on the component |
| Shader mode/packs | `reshade --shaders/--packs`, `shaders install` |
| Select ReShade preset | `preset deploy --file` (+ `--with-shaders`) |
| Launch exe/args | `game launch --args`, exe from settings |
| ReShade channel | `reshade --channel` |
| Keep ReShade.ini updated | always `mergeIni: true` |
| Settings pages | `config` **not implemented** (values are read through `SettingsViewModel` for installs) |

---

## 12. Safety model

1. **No mutation without `--yes`.** Every install/uninstall/update/swap/ini-set checks it.
2. **`--dry-run` on every mutating verb** — resolves targets, reports, writes nothing, skips
   downloads.
3. **Game-running guard** — compares the game exe process name; blocks unless `--force`.
4. **RHI-running guard** — the GUI and CLI share `settings.json`; mutating while `RHI.exe` runs is
   blocked unless `--force`.
5. **WindowsApps refusal** — Game Pass folders cannot be modified (`game add` rejects).
6. **Path matching** — component records are matched by normalized install path, never by name
   alone, so two copies of a game cannot report or uninstall each other's files.
7. **Service-level backups** — installs go through RHI's own logic (foreign DLL backups,
   `.original` sentinels, shader restore helpers). The CLI adds `IniEditor`'s `.cli-backup`.
8. **NVIDIA writes** are gated by `--yes` and have `nvidia backup` for a per-game snapshot.
9. **Vulkan layer** requires an elevated terminal; the CLI refuses with `ELEVATION_REQUIRED`
   otherwise.
10. **No unattended destructive ops**: `update all` skips running games and uses per-component
    commands that themselves enforce the guards.

---

## 13. Verification log

Everything below was executed on 2026-11-05 on the test machine.

| Check | Result |
|---|---|
| `rhictl doctor` | all core checks ok; RHI 2.8.9 present; NVAPI/7-Zip present; not elevated |
| `rhictl library list` | 19 games read from the GUI's existing library |
| `rhictl game add --path "…\Death In The water"` | resolved `…\death In The Water`, exe `Death in the Water 2.exe`, engine Unity |
| `rhictl status --game "Death in the Water 2"` | before installs: nothing but DLSS DLLs present; engine Unity, API DX11, 64-bit |
| `rhictl reshade install --yes` | installed 6.8.0 as `dxgi.dll`; `reshade.ini` written |
| `rhictl renodx install --yes` | installed generic Unity addon 26.1014.1445 (106 MB) |
| `rhictl relimiter install --yes` | installed 3.3.5 as `relimiter.addon64`; `relimiter.ini` 1584 bytes |
| `rhictl status` after | ReShade 6.8.0, RenoDX 26.1014.1445, ReLimiter 3.3.5 detected |
| `rhictl dlss status` | SR 310.9.16.0, NR 310.9.16.0 (crack bundle), no FG/RR/Streamline |
| `rhictl dlss scales` / `render-scale` | option table printed; SR render scale read = 0 |
| `rhictl nvidia info` | NVAPI available; driver 621.01 |
| `rhictl nvidia status` | presets/overrides/vsync/power/low-latency/smooth-motion/fps-limit/gsync read |
| `rhictl hdr status` / `res current` / `res list` | HDR disabled; 2560×1440 @ 170 Hz; modes listed |
| `rhictl vulkan status` | layer not installed; directory `C:\ProgramData\ReShade\` |
| `rhictl addons list` | 25 packs |
| `rhictl shaders list` | 50 packs |
| `rhictl update all --components reshade --dry-run` | “up to date” |
| `rhictl mcp serve` | initialize → tools/list → tools/call(`rhi_status`) → shutdown all correct |
| `rhictl renodx mode` | 1→0→1 round-trip verified |
| `rhictl renodx peak-nits` | 1000→800→1000 round-trip verified |
| `rhictl relimiter ini-set target_fps` | 0→60→0 round-trip verified |
| In-game overlay | Home, Add-ons, Settings, ReLimiter (all sections), RenoDX (Simple + Intermediate) captured |
| GUI compatibility | RHI relaunched after CLI installs, built the game's driver-profile section, closed cleanly; only a pre-existing WindowsApps access error and a caught cancellation from closing during startup scan |
| Published build | Release publish succeeded; spot checks passed |

Not executed live (deliberately): NVIDIA writes, HDR on/off, resolution change, vibrance set,
Vulkan layer install, and installation of the extras proxy DLLs. All have dry-run coverage.

---

## 14. Known gaps and uncertainties

### Functional gaps

1. **Reset overrides** (GUI) has no CLI equivalent.
2. **Settings pages** (GUI) are read-only from the CLI; there is no `config set` verb.
3. **Vulkan layer install** is implemented but untested (needs elevation).
4. **NVIDIA writes** are implemented but unexercised.
5. **`vibrance` display enumeration** returns no displays on this machine/driver.
6. **Ultra+** is link-only (the GUI is also link-only; the vendor site serves the file).
7. **`update all`** spawns child `rhictl` processes; it is slower than an in-process loop.
8. **MCP `tools/call` blocks** for the duration of the operation (installs can take minutes).

### Documentation uncertainties

1. ReShade 6.8 **screenshot name template** key is not confirmed; `SavePath` is confirmed.
2. `[STYLE] StyleIndex` / `EditorStyleIndex` numeric mapping is not confirmed.
3. RenoDX **Advanced** (third mode) was not captured.
4. `toneMapScaling`, `colorGradeLUTSampling`, `colorGradeColorSpace`, `fxFilmGrainType` (non-zero)
   enum names are addon-specific and marked undocumented.
5. `SDR EOTF Emulation` has no confirmed single key.
6. OptiScaler's and Display Commander's **in-game menus** were not captured on this game.

---

## 15. Troubleshooting and gotchas

### Exe detection

`PeHeaderService.FindGameExe` returns the **largest** exe, which on Unity titles is
`UnityCrashHandler64.exe`. The CLI uses `ExeLocator` (product-name scoring) instead. If a game
launches the wrong exe, use `--path` on the correct folder or set a launch override.

### Two copies of the same game

Records are path-keyed. If the library contains two copies, each shows its own components.
Never match by name alone (an earlier bug did, and the second copy reported the first's ReShade).

### Favourites/hidden

Stored as composite `"GameName|Store"` keys. Plain names will silently miss.

### `EnsureLatestAsync` returns false when current

Do not treat `false` as failure. The CLI checks the staged DLL exists.

### Shader removal

`ShaderPackService.SyncGameFolder(null|empty)` **removes managed shaders**. Resolve the real
selection before calling; `shaders remove` uses this deliberately.

### OptiScaler top-level keys

`ShortcutKey`, `LoadReshade`, `LoadAsiPlugins` are written **without a section header**. An
`ini-set` that adds a section for them will not match what OptiScaler expects.

### ReLimiter section mismatch

Template ships `[UltraLimiter]`; writers use `[FrameLimiter]`. Use `[FrameLimiter]` in the CLI.

### RenoDX LUT warning

`colorGradeInternalLUTShaper != 0` without the matching output shader produces the “Missing
output shader” warning. Set it to `0` (Vanilla).

### `IDofFixService` and static abstract members

The interface has a `static abstract` member, so it cannot be a DI generic argument. Resolve the
concrete `DofFixService`.

### `Rtx40MfgService.EnsureStagingAsync()`

Takes no progress argument (unlike the other services).

### NVAPI NR override

No setter exists in this build; only sr/rr/fg overrides can be toggled.

### Build pitfalls encountered

- No .NET SDK on the machine; installed user-locally.
- `Args.Options` needed a private setter (initial assignment).
- `MainViewModel` had to become a non-static class to satisfy `DlssEnablerService`.
- `IDofFixService` static abstract blocked `GetRequiredService<IDofFixService>()`.
- `PresetsCommand` had to become `async` when shader resolution was added.

### Version-specific

`SharpCompress 0.37.2` has a moderate advisory; it is inherited from the app and unchanged.

---

## 16. Source map appendix

Authoritative files for every ini write. Paths are relative to
`C:\Users\cattu\Documents\Directories\Denuvo\RHI\RenoDXCommander\`.

### `Services\AuxInstallService.Ini.cs`

| Method | Lines | Writes |
|---|---|---|
| `MergeRsIni` | 12 (merge 58–74) | whole `reshade.ini` template merge |
| `MergeRsVulkanIni` | 104 (145–160, numbered 201–229) | Vulkan ini + `[ADDON]/[GENERAL]/[SCREENSHOT]` propagation |
| `CopyRsIni` | 250–255 | full template overwrite |
| `CopyRsPresetIniIfPresent` | 261–273 | `ReShadePreset.ini` |
| `ApplyRenoDxNativeHdrSettings` | 371–452 | `[renodx]` seeding (`SettingsMode=2` at 388, `Set_Path` 389, upgrades 390–404) |
| `ApplyRenodxKeyPlaceholders` | 458–524 | empty `[renodx]` placeholders |
| `ApplyUnityRenodxUpgrades` | 534–565 | arbitrary `[renodx]` K/V |
| `RemoveRenoDxNativeHdrSettings` | 570–588 | removes `[renodx]` |
| `ApplyRenodxIniOverrides` | 1337–1359 | manifest overrides |
| `ApplyPeakNits` | 1296–1331 | `[renodx-presetN] ToneMapPeakNits` |
| `ApplyOverlayHotkey` | 1367–1382 | `[INPUT] KeyOverlay` |
| `ApplyScreenshotHotkey` | 1388–1402 | `[INPUT] KeyScreenshot` |
| `ApplyScreenshotPath` | 1276–1290 | `[SCREENSHOT] SavePath` |
| `ApplyVariableListUseTabs` | 1408–1420 | `[OVERLAY] VariableListUseTabs` |
| `DeployUlIniIfAbsent` / `CopyUlIni` | 279–302 / 336–342 | `relimiter.ini` |
| `ApplyUlTargetFps` | 1486–1503 | `[FrameLimiter] target_fps` |
| `ApplyUlOsdHotkey` | 1426–1440 | `[FrameLimiter] osd_toggle_key` |
| `ApplyUlSharedPresets` | 1446–1460 | `[FrameLimiter] shared_presets` |
| `ApplyUlDlssHooks` | 1466–1480 | `[FrameLimiter] dlss_info_hooks` |
| `DeployDcIniIfAbsent` / `CopyDcIni` | 308–331 / 347–353 | `DisplayCommander.ini` |
| `ApplyEngineIniHdrSettings` | 847–947 | `Engine.ini` HDR keys + read-only |
| `ApplyEngineIniLutSetting` | 1120–1180 | `r.LUT.UpdateEveryFrame=1` |
| `ApplyEngineIniCustomKeys` | 1021–1114 | arbitrary Engine.ini K/V |
| `RemoveEngineIniHdrSettings` | 595–676 | removes HDR keys |

### Other writers

| File | Lines | Writes |
|---|---|---|
| `MainWindow.Events.Components.Rdx.cs` | 506–528 | `WriteRenodxIniValue` (cog) |
| `MainWindow.Events.Components.Rdx.cs` | 106–145 | `ToneMapPeakNits` row (creates preset) |
| `MainWindow.Events.Components.Rdx.cs` | 316–337 | cog key list |
| `ControlUePostInstallService.cs` | 511–561 | `[renodx-preset1] FgSlHdr10` |
| `OptiScalerService.Install.cs` | 2002–2052 | `SetOptiScalerIniValue` (generic) |
| `OptiScalerService.Install.cs` | 1962–1996 | `ShortcutKey` (top level) |
| `OptiScalerService.Install.cs` | 1608–1637 / 1705–1733 | `LoadReshade=true` / `LoadAsiPlugins=true` |
| `OptiScalerService.Install.cs` | 301–339 / 1520–1549 | template seed / copy |
| `Dlssg20_30Service.cs` | 467 / 496 | generic `ReadIniValue` / `WriteIniValue` |
| `MainWindow.Events.Components.DisplayCommander.cs` | 17, 63, 86–130 | `[DisplayCommander]` keys |
| `SettingsHandler.Hotkeys.cs` | 199–261 | bulk ReLimiter hotkey writes (all games + `_storage_` + template) |
| `SettingsHandler.General.cs` | 190–285 | bulk ReShade screenshot/hotkey writes |
| `ViewModels\SettingsViewModel.cs` | 23–28, 236–255, 472–479 | hotkey/setting defaults, load, save |
| `ViewModels\MainViewModel.Settings.cs` | 1529–1531 / 1566–1574 | `[ADDON] LoadFromDllMain` add/remove |
| `DetailPanelBuilder.NeuralRendering.Feeder.cs` | 275–297 | `[GENERAL] PreprocessorDefinitions` |
| `Services\ModInstallService.cs` | 724–727 | `GetAddonDeployPath` |

### CLI source map

| Concern | File |
|---|---|
| DI graph | `RHI.Cli\Cli\CliServices.cs` |
| Dispatch/help | `RHI.Cli\Cli\CliApp.cs` |
| Guards/progress | `RHI.Cli\Cli\CliGuards.cs` |
| Downloads/meta | `RHI.Cli\Cli\CliDownloads.cs` |
| INI engine | `RHI.Cli\Cli\IniEditor.cs` |
| Library state | `RHI.Cli\Cli\LibraryStore.cs` |
| Game resolution | `RHI.Cli\Cli\GameResolver.cs` + `ExeLocator.cs` |
| Commands | `RHI.Cli\Commands\*.cs` |

---

## 17. Glossary

| Term | Meaning |
|---|---|
| **RHI** | ReShade HDR Installer, the WinUI app |
| **rhictl** | this CLI |
| **RHI.Core** | headless assembly compiling the app's services |
| **Addon deploy path** | folder where ReShade loads `.addon64/32` (game root or `_Data\Plugins`) |
| **Aux record** | entry in `aux_installed.json` for ReShade/DC/OptiScaler/ASI/ReLimiter |
| **Mod record** | entry in `installed.json` for RenoDX |
| **Composite key** | `"GameName|Store"` key used by favourites/hidden |
| **DLL override** | per-game proxy DLL selection |
| **Peak Nits** | target peak luminance in RenoDX |
| **PQI** | Pacing Quality Index (ReLimiter) |
| **MFG** | Multi Frame Generation |
| **DC** | Display Commander |
| **UL** | UltraLimiter / ReLimiter (legacy naming) |
| **NVAPI/DRS** | NVIDIA driver settings API/database |

---

## 18. Session changelog

1. **Discovery** — read `App.xaml.cs`, DI graph, service/interface files; established that
   `Services/` has zero `using Microsoft.UI`, that models are POCOs, and that state lives in
   `%LOCALAPPDATA%\RHI`.
2. **SDK** — installed .NET 8 SDK 8.0.427 user-locally (no admin).
3. **Feasibility spike** — linked `Services/` + `Models/` into `RHI.Core`; first build produced
   ~133 errors across 25 UI-coupled files. Added shims (DispatcherQueue, XAML,
   `GameCardViewModel`, `App`, `MainViewModel`) and the five-file exclusion set → **0 errors**.
4. **CLI shell** — parser, DI bootstrap, JSON envelope, exit codes, `doctor`, `paths`,
   `library`, `game`, `status`.
5. **Bug fixes** — `ExeLocator` (largest-exe → product-name scoring); path-keyed record matching
   (two copies of a game); `EnsureLatestAsync` false semantics; `SyncGameFolder` removal trap.
6. **ReShade** — install/update/uninstall with channel/DLL/shaders; real 6.8.0 install verified.
7. **HDR mods** — RenoDX (wiki + generic UE/Unity), Luma, RE Framework, DOF Fix.
8. **DLSS/Streamline** — status/versions/set/restore; render scale.
9. **Components** — OptiScaler, DXVK, ASI, DLSS Enabler, 20/30 FG, RTX Encore, MFG Unlocker,
   dgVoodoo2, Ultra+ URL.
10. **Limiters** — ReLimiter (real 3.3.5 install) and Display Commander.
11. **Presets/addons** — deploy with optional shader resolution; addon pack selection.
12. **Vulkan** — status/install/uninstall, per-game cleanup.
13. **NVIDIA/display** — info/status/preset/override/get/set/global/backup, DLDSR,
    HDR/resolution/vibrance.
14. **Batch/MCP** — `update all`, `mcp serve`.
15. **INI** — generic editor; `reshade|renodx|relimiter|display-commander|optiscaler ini-*`.
16. **Library UX** — filters, favourites/hidden, set-path.
17. **In-game exploration** — launched the test game, opened ReShade via `PostMessage` without
    focus, captured Home / Add-ons / Settings / ReLimiter (all sections) / RenoDX (Simple and
    Intermediate), documented every control → ini key, with confidence markers.
18. **In-game niceties** — `renodx mode`, `renodx peak-nits`, `renodx ini-*`.
19. **Docs** — GUI inventory, in-game settings research, in-game menu reference, this document.
20. **Publish** — Release build to `RHI-CLI`; GUI compatibility re-check.

---

## 19. Future work

### High value

1. `config get/set` for `settings.json` (hotkeys, screenshot path, shader selection, channel).
2. `reset overrides` (clear per-game name/DLL/channel/shader/preset/launch overrides).
3. In-process `update all` (avoid spawning `rhictl` per component).
4. Capture RenoDX **Advanced** mode and OptiScaler / Display Commander in-game menus on a DX12
   title, then raise their confidence markers.
5. `vulkan install` end-to-end test in an elevated terminal.
6. NVIDIA write tests on a disposable game profile, with `nvidia backup` / restore verification.

### Medium

7. `RHI_STATE_DIR` override for isolated CLI state.
8. Preset deploy validation (warn about missing shaders before copying).
9. MCP streaming/progress notifications for long installs.
10. `--store` disambiguation for games with the same name across stores.
11. `game launch` with HDR/resolution toggles matching the GUI's launch flow.

### Low

12. `ultraplus download` if a direct source ever exists.
13. Log/Statistics/About capture for completeness.
14. Shell completion (PowerShell) for `rhictl`.

---

## Appendix A — Command index

```
doctor
paths
status
library list|show|refresh
game add|remove|launch|favourite|unfavourite|hide|unhide|set-path
reshade install|update|uninstall
reshade|renodx|relimiter|display-commander|optiscaler ini-list|ini-get|ini-set
shaders list|install|remove
addons list|install|remove
renodx install|update|uninstall|mode|peak-nits
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
`--help`.

## Appendix B — Published artifacts

| Path | What |
|---|---|
| `C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI\rhictl.exe` | the CLI |
| `…\RHI-CLI\*.dll` | `RHI.Core.dll`, `Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`, packages |
| `…\RHI-CLI\7z.exe`, `7z.dll`, `ReShade*.ini/json` | files the service layer expects next to the exe |
| `…\RHI-CLI\README.md` | usage |
| `…\RHI-CLI\PROGRESS.md` (source copy: `RHI.Cli\PROGRESS.md`) | status/pitfalls |
| `…\RHI\docs\EVERYTHING.md` | this document |

## Appendix C — Test game state after this session

`C:\Users\cattu\Documents\Directories\Denuvo\Death In The water\death In The Water`:

| File | Source |
|---|---|
| `dxgi.dll` (ReShade 6.8.0) | `rhictl reshade install` |
| `renodx-unityengine.addon64` (26.1014.1445) | `rhictl renodx install` |
| `relimiter.addon64` (3.3.5) + `relimiter.ini` | `rhictl relimiter install` / addon |
| `reshade.ini` (+ `[renodx]`, `[renodx-preset1]`) | ReShade + RenoDX |
| `reshade.ini.cli-backup` | `rhictl` ini editor |
| `ReShade.log`, `relimiter_*.log`, screenshot PNG | ReShade/ReLimiter |

Original values restored after testing: `SettingsMode=1`, `target_fps=0`, `ToneMapPeakNits=1000`.

---

---

## Appendix D — Command examples with representative output

> Outputs below are trimmed from real runs on the test machine unless marked illustrative.

### doctor

```text
rhictl 1.0.0 — app dir C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI

CHECK        STATUS   DETAIL
-----------  -------  ------------------------------------------------------------
OS           ok       Microsoft Windows NT 10.0.26300.0
.NET runtime ok       8.0.33
Process arch ok       X64
7-Zip        ok       ...\RHI-CLI\7z.exe
RHI install  ok       C:\Program Files\RHI\RHI.exe (2.8.9.0)
State folder ok       C:\Users\cattu\AppData\Local\RHI
settings.json ok      ...\settings.json
game_library ok       ...\game_library.json
Logs         ok       ...\Logs
Elevated     ok       no
NVIDIA       ok       present
```

### status (excerpt)

```text
Death in the Water 2  [Manual]
  path:    C:\Users\cattu\Documents\Directories\Denuvo\Death In The water\death In The Water
  exe:     ...\Death in the Water 2.exe
  engine:  Unity   api: DirectX11   64-bit

COMPONENT           STATE          VERSION      FILE
------------------  -------------  -----------  -----------------------------
ReShade             installed      6.8.0        dxgi.dll
RenoDX              installed      26.1014.1445 renodx-unityengine.addon64
Luma                not installed
OptiScaler          not installed
DXVK                not installed
RE Framework        not installed
DOF Fix             not installed
Display Commander   not installed
ReLimiter           installed      3.3.5        relimiter.addon64

  reshade.ini: yes   shaders: 0   addons: 2
  DLSS:      sr 310.9.16.0  rr -  fg -  nr 310.9.16.0  streamline -
```

### library list --engine Unity (excerpt)

```text
NAME                       STORE   ENGINE  PATH
-------------------------  ------  ------  ------------------------------------------
CRACKED                    Steam   Unity   c:\program files (x86)\steam\steamapps\common\CRACKED
Green Hell                 Steam   Unity   c:\program files (x86)\steam\steamapps\common\Green Hell
Hidden Folks               Epic    Unity   C:\Program Files\Epic Games\HiddenFolks
…
Death in the Water 2       Manual  Unity   …\Denuvo\Death In The water\death In The Water

8 game(s). Last scanned: 2026-11-05 16:45
```

### game add (dry run)

```text
Dry run — nothing written.
  name:      Death in the Water 2
  install:   …\Death In The water\death In The Water
  exe:       …\Death in the Water 2.exe
  engine:    Unity
```

### reshade install (dry run)

```text
Dry run - nothing downloaded or written.
  game:       Death in the Water 2
  path:       …\death In The Water
  channel:    Stable
  dll:        dxgi.dll
  shaders:    (none)
  bitness:    64-bit
```

### renodx install (dry run)

```text
Dry run - nothing downloaded or written.
  game:     Death in the Water 2
  mod:      Generic Unity Engine (Voosh)
  source:   generic engine fallback
  url:      https://…/renodx-unityengine.addon64
  target:   …\death In The Water
```

### renodx mode / peak-nits

```text
> rhictl renodx mode --game "Death in the Water 2" --value intermediate --yes
RenoDX mode set to intermediate (SettingsMode=1) for 'Death in the Water 2'.

> rhictl renodx peak-nits --game "Death in the Water 2" --value 800 --yes
ToneMapPeakNits=800 applied to 1 preset(s) for 'Death in the Water 2': renodx-preset1
```

### relimiter ini round-trip

```text
> rhictl relimiter ini-get --game "Death in the Water 2" --section FrameLimiter --key target_fps
0
> rhictl relimiter ini-set --game "Death in the Water 2" --section FrameLimiter --key target_fps --value 60 --yes
[FrameLimiter] target_fps: 0 -> 60   (…\relimiter.ini)
> rhictl relimiter ini-get --game "Death in the Water 2" --section FrameLimiter --key target_fps
60
```

### dlss status (excerpt)

```text
Death in the Water 2 — DLSS / Streamline
  sr:         310.9.16.0  …\nvngx_dlss.dll
  rr:         (not found)  
  fg:         (not found)  
  nr:         310.9.16.0  …\nvngx_dlssnr.dll
  streamline: (not found)
```

### nvidia status (excerpt)

```text
Death in the Water 2 — NVIDIA driver profile
  driver:        621.01
  SR preset:     0  override=False
  RR preset:     0  override=False
  FG preset:     0  override=False
  NR preset:     0  override=False
  vsync:         0
  power:         0
  low latency:   0
  smooth motion: 0
  fps limit:     (none)
  gsync:         off
```

### update all (dry run, excerpt)

```text
GAME                   COMPONENT  EXIT  DETAIL
---------------------  ---------  ----  --------
Death in the Water 2   reshade    0     v6.8.0

1 update(s) attempted, 0 failed, 0 game(s) skipped (running).
```

### mcp tools/call response (excerpt)

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "result": {
    "content": [{ "type": "text", "text": "{\n  \"ok\": true,\n  \"command\": \"status\", … }" }],
    "isError": false
  }
}
```

---

## Appendix E — Service inventory

`RHI.Cli\Cli\CliServices.cs` registers the following (all lazy until resolved):

| Registration | Role |
|---|---|
| `HttpClient` | shared HTTP/2 client, GitHub token handler, `rhi-cli/0.1` UA, 10-min timeout |
| `GitHubETagCache` | ETag-aware GitHub API cache |
| `ModInstallService` | RenoDX/mod download + install + records |
| `AuxInstallService` | ReShade/DC/OptiScaler/ASI/ReLimiter install + ini engine |
| `WikiService` | wiki mod catalogue + generic addon URLs |
| `ManifestService` | component manifest (URLs/versions, dgVoodoo list) |
| `GameLibraryService` | `game_library.json` load/save |
| `ReShadeUpdateService` | stable/nightly ReShade staging |
| `NormalReShadeUpdateService` | no-addons ReShade staging |
| `ReShadeNightlyService` | nightly metadata |
| `UpdateService` | generic update checks |
| `LumaService` | Luma catalogue/install/records |
| `ShaderPackService` | 50 shader packs, file lists, deploy/remove |
| `GameDetectionService` | storefront scans, engine/path detection, name matching |
| `PeHeaderService` | PE architecture detection |
| `CrashReporterService` | session/crash logging |
| `REFrameworkService` | RE Framework install/records |
| `NexusModsService`, `NexusUpdateService`, `NexusDownloadService`, `NexusSsoService` | Nexus integration |
| `SteamAppIdResolver`, `PcgwService` | metadata lookups |
| `UltrawideFixService`, `UltraPlusService` | ultrawide/Ultra+ helpers |
| `SettingsViewModel` | global settings (linked app class) |
| `DllOverrideService` | per-game DLL overrides |
| `GameNameService` | name mappings, per-game settings, channel/override dictionaries |
| `GameInitializationService` | detection/dedup/manifest bootstrap |
| `ReShadeExtractor` (`ISevenZipExtractor`) | 7-Zip extraction |
| `OptiScalerService`, `OptiScalerWikiService` | OptiScaler install + metadata |
| `HdrDatabaseService` | per-game HDR notes |
| `DxvkService` | DXVK variants/records |
| `DlssStreamlineService`, `DlssPresetService` | DLSS/Streamline swaps + NVAPI presets/settings |
| `DofFixService` | DOF Fix staging/install |
| `UltimateAsiLoaderService` | ASI loader |
| `DlssNrCostScalerService` | NR cost scaler |
| `Rtx40MfgService`, `Dlssg20_30Service` | MFG unlock + 20/30 FG |
| `DlssEnablerService` | DLSS Enabler standalone |
| `DgVoodooService` | dgVoodoo2 deploy |
| `Renodx5AddonService` | DLSS5 feeder addon |
| `CustomReShadeHashService` | custom ReShade staging |
| `SeenWikiModsService`, `SeenUltraPlusModsService`, `SeenLumaModsService` | "new mod" markers |
| `UpdateLogService`, `RenoDXDbService` | update log + RenoDX DB |
| `GitHubAuthService` | GitHub auth/token |
| `DldsrService` | DLDSR capture/apply |
| `AutoHdrService` | Auto HDR helpers |
| `RtxEncoreService` | RTX Encore |
| `Lazy<IDxvkService>`, `Lazy<IDlssStreamlineService>` | cycle-breaking lazy wrappers |

---

---

## Appendix F — Building from a fresh clone

The repo does **not** contain the RHI app source. `RHI.Core` links it at compile time.

### Layout A — repo next to the RHI folder (works with no arguments)

```
Denuvo\
  RHI\                       ← the RHI fork (contains RenoDXCommander\)
  RHI-CLI-Repo\              ← this repo
    Directory.Build.props    ← sets RhiSourceDir = ..\RHI\RenoDXCommander
    RHI.Core\
    RHI.Cli\
```

### Layout B — anywhere else

```powershell
$dotnet = "C:\Users\cattu\.dotnet\dotnet.exe"
& $dotnet build ".\RHI.Cli\RHI.Cli.csproj" -c Release `
  -p:RhiSourceDir="C:\path\to\RHI\RenoDXCommander"

& $dotnet publish ".\RHI.Cli\RHI.Cli.csproj" -c Release -r win-x64 --self-contained false `
  -p:RhiSourceDir="C:\path\to\RHI\RenoDXCommander" `
  -o ".\dist"
```

### Requirements

- Windows 10 19041+ / Windows 11
- .NET 8 SDK
- the RHI source tree (for `Services\`, `Models\`, `SettingsViewModel.cs`, `HotkeyManager.cs`,
  `NativeInterop.cs`, `7z.exe`, `7z.dll`, ReShade templates)

### What is deliberately excluded from the repo

`bin\`, `obj\`, the published `dist\`, `.vs\`, user files, and the entire RHI app source.
Build outputs are reproducible.

---

## Appendix G — Security notes

- **No credentials are stored in this repo.** The GitHub token used by the app's HTTP handler
  comes from `DevUnlockService.GitHubApiToken` in the RHI source; the CLI never prints it.
- Do **not** commit `%LOCALAPPDATA%\RHI\settings.json` or `game_library.json` — they contain
  local paths and possibly tokens.
- Downloads come from GitHub release APIs and reshade.me, through the app's own services.
- Elevation is required only for the Vulkan implicit layer (HKLM + `C:\ProgramData`).
- `SharpCompress 0.37.2` carries a known moderate advisory (inherited from the app). Upgrading it
  requires re-testing the app's archive extraction.
- The MCP server is stdio-only and spawns the same executable; it does not open a network port.
- `ini-set` refuses to write while the game or RHI is running (shared-file corruption guard).

---

## Appendix H — Performance notes

| Operation | Cost |
|---|---|
| `doctor`, `paths` | instant (no service construction beyond paths) |
| `library list` | reads `game_library.json`; no network |
| `status` | record files + local file probes; no network except what the services cache |
| `game add` | engine/PE scan of one folder |
| `library refresh` | scans all eight storefronts; can take tens of seconds |
| `renodx install` | wiki fetch + 106 MB generic Unity download on first run; cached afterwards |
| `reshade install` | staged DLL copy (~5.5 MB); downloads only if staging is missing |
| `relimiter install` | ~470 KB download |
| `dlss versions/set` | manifest fetch (~7 KB) |
| `update all` | spawns one child process per game/component; slower than an in-process loop |
| `mcp tools/call` | blocks for the duration of the operation |

The DI container is lazy: a command only constructs the services it resolves. `rhictl` start-up
is dominated by loading `RHI.Core` + WPF-free WinRT assemblies.

---

## Appendix I — Symptom → cause → fix

| Symptom | Cause | Fix |
|---|---|---|
| `GAME_NOT_FOUND` | name differs from the library entry | `--path "<folder>"` instead |
| `PATH_NOT_FOUND` | install folder moved/renamed | `game set-path` (manual) or re-add |
| `BLOCKED` (exit 4) | game or `RHI.exe` is running | close it, or `--force` |
| `CONFIRMATION_REQUIRED` | missing `--yes` | add `--yes` (or `--dry-run`) |
| `KEY_NOT_FOUND` (exit 3) | key absent from the ini | `ini-set` adds it |
| `NO_INI` | component not installed / wrong group | install the component or use the right group |
| `CONFLICT` | proxy DLL already owned by another component | pick another `--dll`, or `--force` |
| `NO_MOD` | no wiki match and no generic fallback (e.g. UE3) | install manually or choose another game |
| `VULKAN_UNSUPPORTED` | Vulkan-only game on `reshade install` | `vulkan install` |
| `ELEVATION_REQUIRED` | Vulkan layer operation without admin | elevated terminal |
| `INSTALL_FAILED` | download/staging/copy failure | check network; re-run; inspect `%LOCALAPPDATA%\RHI\logs` |
| Wrong exe launched | `ExeLocator` picked another binary | `game launch` uses overrides; set them in the GUI, or `--path` |
| Two copies show each other's components | old name-based matching | fixed: records are path-keyed |
| Shaders vanished | `SyncGameFolder(null|empty)` | `shaders install --packs …` |
| ReShade says “No effect files” | shaders off for that game | `shaders install --packs …` |
| RenoDX “Missing output shader” | non-Vanilla LUT shaper | `renodx ini-set … colorGradeInternalLUTShaper 0` |
| CLI edit lost after gaming | runtime ownership | edit while the game is closed |
| `vibrance list` empty | NVAPI display enumeration | driver limitation; not fixable in the CLI |
| `nvidia override --component nr` fails | no NR setter in this NVAPI build | use sr/rr/fg |
| Game appears hidden | `HiddenGames` contains its composite key | `game unhide` |

---

## Appendix J — Design decision log

| Decision | Rationale |
|---|---|
| Link app sources instead of referencing the app | avoids WinUI payload/activation; app untouched |
| Shims instead of refactoring the app | zero risk to the shipped GUI; compile-time divergence alarm |
| Share `%LOCALAPPDATA%\RHI` | GUI and CLI must see the same state |
| `rhictl` (not `rhi`) | `RHI.exe` exists; Windows is case-insensitive |
| `--yes` on every mutation | prevents accidental writes from scripts/agents |
| `--dry-run` implemented per command | services have no built-in dry-run; the CLI resolves and stops |
| Path-keyed records | two copies of a game must not cross-contaminate |
| Composite `Name|Store` keys for favourites | matches the GUI's storage format |
| `update all` spawns child processes | reuses tested per-component flows instead of duplicating them |
| MCP shells out to `rhictl` | the tool surface cannot drift from the CLI |
| `IniEditor` with `.cli-backup` | safe edits with an undo path |
| No app source in the repo | the repo is the CLI, not a fork of RHI |

---

## Appendix K — Test matrix

| Area | Live | Dry-run | Read-only | Not tested |
|---|---|---|---|---|
| doctor / paths | ✅ | — | — | — |
| library list/show/refresh | ✅ (list/show) | ✅ (refresh) | — | full refresh live |
| game add/remove/launch/favourite | ✅ add/favourite/launch dry | ✅ remove | — | launch live |
| status | ✅ | — | — | — |
| ReShade install/update/uninstall | ✅ install | ✅ update | — | uninstall live |
| shaders list/install/remove | ✅ list | — | — | install/remove live |
| addons list | ✅ | — | — | install/remove live |
| renodx install | ✅ | ✅ | — | uninstall live |
| renodx mode/peak-nits | ✅ | ✅ | — | — |
| luma | — | ✅ | — | install/uninstall live |
| dlss status/scales/render-scale | ✅ | — | — | set/restore live |
| reframework/dofix/dxvk/optiscaler | — | ✅ | — | install live |
| asi/dlss-enabler/fg2030/rtx-encore/mfg40/dgvoodoo | — | ✅ | — | install live |
| relimiter | ✅ install | ✅ uninstall | — | uninstall live |
| display-commander | — | ✅ | — | install live |
| preset | ✅ list (empty) | — | — | deploy with a real preset |
| vulkan | ✅ status | ✅ install/uninstall | — | install live (admin) |
| nvidia | ✅ info/status/backup? | ✅ writes | ✅ | writes live |
| hdr/res/vibrance | ✅ status/current/list | ✅ writes | — | writes live |
| update all | — | ✅ | — | full live |
| mcp | ✅ handshake/tool call | — | — | long installs |
| ini verbs | ✅ get/set round-trip | ✅ | — | — |

---

## Appendix L — Upstream quirks and inherited issues

1. `PeHeaderService.FindGameExe` picks the largest exe (Unity crash handler).
2. `IReShadeUpdateService.EnsureLatestAsync` returns false when already current.
3. `ShaderPackService.SyncGameFolder(null|empty)` removes managed shaders.
4. `IDofFixService` has a `static abstract` member (cannot be a DI generic argument).
5. `Rtx40MfgService.EnsureStagingAsync()` takes no progress argument.
6. NVAPI has no NR driver-override setter in this build.
7. `relimiter.ini` template contains `[UltraLimiter]`, writers use `[FrameLimiter]`.
8. `GameLibraryService.Save` only persists `Name`/`InstallPath`/`Source` per game, so
   `SteamAppId`, `EpicCatalogNamespace`, `EpicAppName` and `XboxAumid` are dropped on any save
   (the GUI has the same limitation). The CLI round-trips every other collection.
9. `DigitalVibranceService.EnumerateDisplays()` returns empty on this driver.
10. `SharpCompress 0.37.2` advisory.

---

## Appendix M — Universal window probe (`probe.ps1`)

A dependency-free PowerShell tool for driving and capturing **any** Windows window. It is not
tied to the test game; it was only used on one because that was the target. Full guide:
`docs/PROBE.md`.

Key points:

- **Selection:** `-ProcessName`, `-WindowTitle` (regex), or `-Hwnd`; `-Action list` enumerates.
- **No-focus input:** `postkey`, `postclick`, `postwheel`, `postdrag`, `move` use `PostMessage`.
  `key`, `click`, `focus` use real input and steal focus.
- **Capture:** `shot` uses `PrintWindow(PW_RENDERFULLCONTENT)` on the client area, not
  `CopyFromScreen`. `-MaxWidth` downscales for vision models; the result reports `printWindowOk`
  and `black`.
- **JSON:** every action supports `-Json`.
- **Keys:** names (`Home`, `PageUp`, `PrintScreen`, `F1`…) or VK hex (`0x24`).
- **Known limits:** exclusive fullscreen can capture black; UIPI blocks elevated targets;
  raw-input apps ignore `post*`; anti-cheat games should not be driven synthetically.

Why it exists: `CopyFromScreen` captures the desktop compositor. For a fullscreen game that is
whatever window is on top (Discord/terminal), not the game. `PrintWindow` captures the window's
own composed surface, including the ReShade overlay, and does not disturb the user.

---

*End of document.*


















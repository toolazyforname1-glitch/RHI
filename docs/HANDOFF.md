# HANDOFF — RHI + rhictl

**Written:** 2026-11-08
**For:** whoever continues this project (human or fresh agent)
**Status at write time:** everything builds, **50/50 checks pass**, parity batch 1 (config, launch/wiki
overrides, reset-overrides, UE-Extended) is implemented and verified. Fork push pending.

Read this file first. It is deliberately operational: exact paths, exact commands, exact state,
and the traps that cost time. The deep reference is `docs/EVERYTHING.md`; this is the "resume
work" document.

---

## 0. The 60-second snapshot (verified, not remembered)

| Item | Value |
|---|---|
| Local source repo | `C:\Users\cattu\Documents\Directories\Denuvo\RHI` |
| Branch / HEAD | `main` / `50ba26e` (`Add rhictl banner to the front page`) |
| Working tree | clean except untracked `RHI-Setup.exe` |
| Published CLI | `C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI\rhictl.exe` (151,552 bytes, 2026-11-08 00:23) |
| GitHub fork | `toolazyforname1-glitch/RHI-CLI` (fork of `RankFTW/RHI`) |
| Fork `main` | `50ba26e` — identical to local |
| PR #1 | merged (`e551fbf` → merge `0b36b16`) |
| .NET SDK | 8.0.427 at `C:\Users\cattu\.dotnet\dotnet.exe` (user-local, no admin) |
| GitHub CLI | `gh` 2.95.0, account `toolazyforname1-glitch`, scopes `gist, read:org, repo` |
| Test game | removed 2026-11-08 (library cleaned); re-add any title with `game add --path <dir> --name <n>` |
| Shared app state | `%LOCALAPPDATA%\RHI` |
| Verify script | `RHI\tools\verify-rhictl.ps1` → **50/50 expected** |

If any of these differ, verify before continuing:

```powershell
cd "C:\Users\cattu\Documents\Directories\Denuvo\RHI"
git log --oneline -5
git status --short
git remote -v
git ls-remote fork refs/heads/main
```

---

## 1. What this project is

RHI is a WinUI 3 app (v2.8.9) that installs and configures ReShade, RenoDX, Luma, OptiScaler,
DXVK, DLSS/Streamline, NVIDIA driver settings and related components into game folders.

`rhictl` is a headless CLI for it. It does **not** reimplement RHI: `RHI.Core` compiles the
app's real `Services/` + `Models/` source into a UI-free assembly, and `RHI.Cli` is a console
front-end over that. Every install, update, ini merge, shader deploy and driver write goes
through the same service methods the GUI calls.

The WinUI app is **almost untouched**: one deliberate service fix (`DlssStreamlineService.Swap.cs`, DLSS download timeout 120 s → 10 min) so large NR archives can finish; every other app file matches upstream.
The installed app at `C:\Program Files\RHI` is the original binary (172,154,424 bytes,
2026-11-03, v2.8.9.0).

### The three local folders

| Folder | Role |
|---|---|
| `Denuvo\RHI` | **Source of truth.** The RHI fork + `RHI.Core`, `RHI.Cli`, `docs`, `tools`. Git repo with `origin` = upstream, `fork` = fork. |
| `Denuvo\RHI-CLI` | **Run/publish output.** `rhictl.exe` + dependent DLLs + `7z.exe`/`7z.dll` + ReShade templates + `README.md` + `probe.ps1`. Not a git repo. |
| `Denuvo\RHI-CLI-Repo` | **Standalone snapshot for a separate push.** Only CLI sources + docs + tools + LICENSE + build files. No app source, no binaries. Not a git repo. |

The standalone snapshot is **not linked** to the source. If `RHI.Core`/`RHI.Cli` change in
`Denuvo\RHI`, re-copy the changed files into `RHI-CLI-Repo` manually.

---

## 2. Architecture (the parts that matter when editing)

```
RHI.Core.csproj   links $(RhiSourceDir)\Services\**\*.cs and Models\**\*.cs
                  + SettingsViewModel.cs, HotkeyManager.cs, NativeInterop.cs
                  + Shims\**
RHI.Cli.csproj    references RHI.Core; AssemblyName=rhictl
```

- **`RhiSourceDir`** defaults to `..\RenoDXCommander` relative to each csproj, so building inside
  `Denuvo\RHI` works with no arguments. Override with `-p:RhiSourceDir="C:\...\RenoDXCommander"`.
- **Excluded (UI-only) service files:** `AddonInfoResolver`, `AutoUpdateService`,
  `ControlUePostInstallService`, `UpdateOrchestrationService`, `IUpdateOrchestrationService`.
- **Shims (6):** `Microsoft.UI.Dispatching.DispatcherQueue` (runs inline),
  `Microsoft.UI.Xaml.Application`, `GameCardViewModel` (the members services use),
  `App.Services` locator, `MainViewModel` (static helpers + one instance member),
  `DragDropHandler` (four pure static helpers; see §7).
- **DI:** `RHI.Cli/Cli/CliServices.cs` mirrors `App.xaml.cs` minus UI registrations, sets
  `App.Services`, and is lazy (nothing constructs until resolved).
- **Early warning:** if a service starts using a new `GameCardViewModel` member, `RHI.Core` stops
  compiling. Add the member to the shim — do not weaken the shim type.

---

## 3. Build, publish, run

```powershell
$dotnet = "C:\Users\cattu\.dotnet\dotnet.exe"
cd "C:\Users\cattu\Documents\Directories\Denuvo\RHI"

# build (Release)
& $dotnet build "RHI.Cli\RHI.Cli.csproj" -c Release

# publish the folder the CLI runs from (7z + templates are copied by the csproj)
& $dotnet publish "RHI.Cli\RHI.Cli.csproj" -c Release -r win-x64 --self-contained false `
    -o "C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI"

# run
& "C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI\rhictl.exe" doctor
```

Framework-dependent: the .NET 8 desktop runtime (8.0.33) is installed. `rhictl.exe` expects
`7z.exe`, `7z.dll`, `ReShade.ini`, `ReShade.Vulkan.ini`, `ReShade64.json` next to it — the csproj
copies them from `$(RhiSourceDir)`.

---

## 4. Resume in five minutes

```powershell
cd "C:\Users\cattu\Documents\Directories\Denuvo\RHI"

# 1. is upstream ahead?
git fetch origin
git log --oneline HEAD..origin/main      # anything here means §7 applies

# 2. build + publish
& "C:\Users\cattu\.dotnet\dotnet.exe" build "RHI.Cli\RHI.Cli.csproj" -c Release
& "C:\Users\cattu\.dotnet\dotnet.exe" publish "RHI.Cli\RHI.Cli.csproj" -c Release -r win-x64 `
    --self-contained false -o "C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI"

# 3. verify (expect TOTAL: 38  PASS: 38  FAIL: 0)
& ".\tools\verify-rhictl.ps1"

# 4. read the next-task list (§9) and continue
```

If the verify script reports a failure that is a *state* difference (e.g. no presets installed,
DOF not eligible), check the `-Expected`/`-ErrorCode` handling before assuming a product bug.

---

## 5. What the 50/50 verification actually covers

`RHI\tools\verify-rhictl.ps1` (parameters `-Exe`, `-Game`, defaults: the published exe and
"Death in the Water 2").

| Group | Checks |
|---|---|
| Diagnostics | `doctor --json`, `paths --json` |
| Settings | `config list --json`, `config set --dry-run --json` |
| Library | `library list --json`, `library refresh --dry-run --json` |
| Per-game | `status --json` |
| Overrides | `game reset-overrides --dry-run --json`, `game set-launch --dry-run --json`, `game set-name --dry-run --json`, `game set-dll --dry-run --json`, `game reset-dll --dry-run --json` |
| DLSS | `dlss status`, `dlss scales` |
| NVIDIA | `nvidia info`, `nvidia status` |
| Display | `hdr status`, `res current` |
| Vulkan | `vulkan status` |
| Packs | `shaders list`, `addons list`, `preset list` |
| Ini reads | `renodx ini-get`, `relimiter ini-get` |
| Dry-run mutations (18) | ReShade, RenoDX, ReLimiter, Display Commander, ASI, DLSS Enabler, 20/30 FG, RTX Encore, MFG, dgVoodoo, DXVK, OptiScaler, RE Framework, DOF Fix, preset deploy, RenoDX mode, RenoDX peak-nits, update all |
| Guards | mutation without `--yes` → exit 2; `vulkan install --layer-only` non-elevated → exit 5 |
| MCP | initialize → tools/list → tools/call `rhi_status` → shutdown |

Expected tail:

```
TOTAL: 50  PASS: 50  FAIL: 0
```

**Live-verified 2026-11-08 (multi-game):**
- ReShade 6.8.0 install/uninstall: 64-bit DX11 (Death in the Water 2, RE2), 32-bit DX11 (Dredge),
  OpenGL (Firewatch).
- RenoDX: Unity 26.1014.1445; generic Unreal 26.1104.1857 (Stray); UE-Extended 26.1105.1745 with
  the on/off flag.
- RE Framework `01438` (RE2), ReLimiter 3.3.5 + ini round-trip, Display Commander 1.0.6.3562,
  OptiScaler (`dxgi.dll`), ASI (`dinput8.dll`), DLSS Enabler, 20/30 FG, MFG40, RTX Encore,
  dgVoodoo 2.87.6 — all install + uninstall.
- DXVK v3.1.1 32-bit (Mini Motorways).
- Shaders: Lilium (46 files) install/remove; addons: AutoReload install/remove; preset deploy.
- HDR on/off; NVIDIA `fps-limit` set/get/restore, `sr` preset write, override off/on.
- `game set-name` round-trip; `set-dll`/`reset-dll` (`dxgi.dll`↔`d3d11.dll`); `set-override`
  channel/bitness/wiki/update-exclusion + clear; `reset-overrides`.

**2026-11-08 second matrix (targeted 4):**
- **Portal with RTX** — `dlss status` sees SR/RR/FG/NR; `set`+`restore` proven for RR
  (`310.9.1`↔`310.9.16`), FG (`310.9.16`↔`310.9.1`) and NR (`310.9.16`↔`310.8.2`).
- **Jusant** (UE5.2) — generic Unreal RenoDX `26.1104.1857`; DOF Fix install/uninstall; DLSS SR
  swap `3.1.13`↔`310.9.16` + restore; Feeder `v1.19.0` install/uninstall (addon, DLSS5 Tool, NR
  `310.9.16`, SR `310.9.16`, `DLSS5_Feed.fx` + `lumenite_Kernel.fx`) with a clean shader-folder
  removal.
- **Thumper** (DX9) — ReShade 6.8.0 `d3d9.dll`; Luma build 810 (48 files); dgVoodoo 2.87.6.
- **Quake II RTX** (Vulkan) — `vulkan status` works; elevated `vulkan install --layer-only` created the layer, and
  `vulkan uninstall` removed it cleanly.

Two checks (`renodx ini-get`, `relimiter ini-list`) **skip with a pass** when the component is not
installed in the target game, so a cleaned game still reports a meaningful green. Three checks are
**state-dependent** and legitimately return non-zero:

- `display-commander dry` → exit 2 `CONFLICT` when ReLimiter is installed for the game.
- `dofix dry` → exit 2 `NOT_ELIGIBLE` on Unity/32-bit games.
- `preset deploy dry` → exit 3 `NO_PRESETS` when no preset files exist.

The script accepts those codes only when the error code matches.

### Checks not in the script (do them after a push)

```powershell
# fresh clone must build and run
git clone --depth 1 https://github.com/toolazyforname1-glitch/RHI-CLI.git "$env:TEMP\rhi-clone"
& "C:\Users\cattu\.dotnet\dotnet.exe" build "$env:TEMP\rhi-clone\RHI.Cli\RHI.Cli.csproj" -c Release
& (Get-ChildItem "$env:TEMP\rhi-clone\RHI.Cli\bin" -Filter rhictl.exe -Recurse | Select-Object -First 1).FullName doctor
Remove-Item "$env:TEMP\rhi-clone" -Recurse -Force
```

Real installs (only when needed): no test game is currently registered — the 24 manual test
entries were removed on 2026-11-08. Add one with `game add --path <dir> --name <n>` and uninstall/
reinstall one component at a time; do not batch untested mutations.

---

## 6. Git workflow

### Remotes

```
origin  https://github.com/RankFTW/RHI.git                  (upstream, read-only for us)
fork    https://github.com/toolazyforname1-glitch/RHI-CLI.git
```

### Current history on `main`

```
50ba26e  Add rhictl banner to the front page
bf33a4c  Document the fork on the front page
20f544f  Fix RHI.Core against upstream service changes
0b36b16  Merge pull request #1 from toolazyforname1-glitch/rhictl
e551fbf  Add rhictl: headless CLI for RHI
```

### Making a change

```powershell
git checkout -b feature/short-name
# edit, build, publish, verify
git add <explicit paths>          # never `git add -A`; RHI-Setup.exe must stay out
git commit -m "..." -m "details" -m "Verified: build 0 errors; 38/38."
git -c http.version=HTTP/1.1 -c http.postBuffer=524288000 push fork feature/short-name
gh pr create --repo toolazyforname1-glitch/RHI-CLI --base main --head feature/short-name `
  --title "..." --body-file <notes>
gh pr merge <n> --repo toolazyforname1-glitch/RHI-CLI --merge
```

The HTTP/1.1 + 500 MB buffer flags are not optional folklore: a plain `git push` over HTTP/2
failed mid-pack on the 36 MB of PNG captures (`curl 55 Recv failure`). With those flags it
succeeded.

### Never

- push to `origin` (upstream);
- commit `RHI-Setup.exe`;
- commit `bin/`, `obj/`, `dist/`, or `%LOCALAPPDATA%\RHI` state;
- force-push `main` (the fork is public).

---

## 7. Upstream drift — the most important operational section

The fork's `main` is a moving upstream. When this project was pushed, the fork's `main` was
**newer** than the local clone (`76c4d8c` vs `c87766b`). Merging exposed two real breaks, both
fixed in `20f544f`:

1. **New UI type used inside `Services`.** Upstream moved game-root/name inference into
   `RenoDXCommander.DragDropHandler`, which is UI-bound (`MainWindow`, `ContentDialog`).
   Fix: `RHI.Core/Shims/DragDropHandlerShim.cs` compiles the four pure static helpers
   (`LooksLikeGameRoot`, `InferGameName`, `CleanUnrealExeName`, `CleanFolderName`).
   **This shim is a manual copy; the build cannot detect behavioural drift. If upstream changes
   those helpers, update the shim by hand.**
2. **Changed service signatures.**
   - `Rtx40MfgService.RemoveEncoreXessFiles(modDir)` → `RemoveEncoreIntelFiles(installPath, modDir)`.
   - `RtxEncoreService.DeployNrDll(installPath)` → `DeployNrDll(installPath, installedAs)`.

### Procedure when upstream advances

```powershell
cd "C:\Users\cattu\Documents\Directories\Denuvo\RHI"
git fetch origin
git log --oneline HEAD..origin/main        # what changed
git merge origin/main                      # or rebase, but merge matches the fork history

& "C:\Users\cattu\.dotnet\dotnet.exe" build "RHI.Cli\RHI.Cli.csproj" -c Release
# fix errors:
#   - missing type belongs to UI  -> add a shim (copy the pure logic, keep it tiny)
#   - missing type is UI-free     -> link the file in RHI.Core.csproj
#   - signature changed           -> update the command call sites
& "C:\Users\cattu\.dotnet\dotnet.exe" publish ... -o "...\RHI-CLI"
& ".\tools\verify-rhictl.ps1"              # must be 38/38

git add RHI.Core RHI.Cli
git commit -m "Fix RHI.Core against upstream service changes" -m "<what and why>" -m "Verified: build 0 errors; 38/38."
git -c http.version=HTTP/1.1 -c http.postBuffer=524288000 push fork main:main
```

**A pushed `main` must always build.** The original push was verified before the merge, then the
merge invalidated it; the fix commit exists because of that lesson.

---

## 8. Known gaps (exact, with impact)

| # | Gap | Impact | Notes |
|---|---|---|---|
| 1 | ~~No `config get/set` for `settings.json`~~ **done 2026-11-08** | `config list/get/set/unset`; atomic `SettingsFile.Update`; `--show-secrets`; RHI-running guard | settings are now writable |
| 2 | ~~No `reset overrides`~~ **done 2026-11-08** | `game reset-overrides`; 45 groups; DLL override reverted on disk; component files left in place | GUI parity for the data layer |
| 3 | RenoDX **Advanced** (third mode) not captured | doc marks it "likely" | needs one more click on the mode button |
| 4 | OptiScaler / Display Commander in-game menus not captured | their UI is undocumented; keys come from source only | needs a DX12 title with them installed |
| 5 | `vulkan install` not tested end-to-end | only `status` verified | requires an elevated terminal |
| 6 | NVIDIA writes not tested | `set/preset/override` unexercised | use `nvidia backup` first |
| 7 | `vibrance` returns no displays here | `vibrance set` cannot resolve an index | raw NVAPI enumeration limitation |
| 8 | MCP `tools/call` blocks | long installs hold the call | needs progress notifications |
| 9 | `update all` spawns child processes | slower than in-process | reuses tested flows deliberately |
| 10 | No isolated state directory | CLI and GUI share `%LOCALAPPDATA%\RHI` | by design; `RHI_STATE_DIR` not implemented |
| 11 | ReShade 6.8 screenshot *name* key unconfirmed | one doc line marked likely | path/format/overlay toggle confirmed |
| 12 | Ultra+ is link-only | no download | same as the GUI |
| 13 | ~~Vulkan install/uninstall not live-tested~~ **done** | elevated install created ReShade64.dll/ReShade64.json + the HKLM implicit-layer entry; uninstall removed both and status returned false | Quake II RTX |
| 14 | ~~DLSS `set`/`restore` not live-tested~~ **done** | RR/FG/NR/SR swaps + restores verified | Portal RTX, Jusant |
| 15 | ~~Luma not live-tested~~ **done** | install/uninstall verified | Thumper, build 810 |
| 16 | ~~DOF Fix not live-tested~~ **done** | install/uninstall verified | Jusant (UE5.2), after the engine-hint fix |
| 17 | FG / MFG effect not visually verified | file swaps verified; game not launched | visual proof only |
| 18 | Per-game resolution toggle is system-wide | `res set` changes the display, not a game override; `ResToggleOverrides` is set via `game set-override --kind res-toggle` | by design |
| 19 | ~~Feeder addon has no CLI verb~~ **done** | `feeder status/install/uninstall`; deploys Feeder addon, DLSS5 Tool, NR/SR DLLs and the two shaders | Jusant v1.19.0; 64-bit only (32-bit host64 still GUI) |

---

## 9. Next tasks, in priority order, with acceptance criteria

### 9.1 `config get/set` for `settings.json` — **done 2026-11-08**

Implemented as `config list|get|set|unset` in `RHI.Cli/Commands/ConfigCommand.cs`; four checks in
`tools/verify-rhictl.ps1`. Kept below as the original specification.

Why: the CLI reads settings for installs but cannot change hotkeys, screenshot path, shader
selection or channel defaults.

Files:
- new `RHI.Cli/Commands/ConfigCommand.cs`
- wire in `RHI.Cli/Cli/CliApp.cs` (`"config" => ConfigCommand.Run(args, services)`), help text
- add checks to `tools/verify-rhictl.ps1`

APIs (verified names):
- `SettingsFile.Load()` → `Dictionary<string,string>`
- `SettingsFile.Update(Action<Dictionary<string,string>> mutate, string callerTag)` → safe atomic write
- `SettingsViewModel.LoadSettingsFromDict(Dictionary<string,string>)`
- `SettingsViewModel.SaveSettingsToDict(Dictionary<string,string>)`

Behaviour:
- `config list [--json]` → all keys/values (redact anything that looks like a token).
- `config get --key <k> [--json]` → value or exit 3.
- `config set --key <k> --value <v> [--yes|--dry-run]` → `SettingsFile.Update`, then reload the
  `SettingsViewModel` so the process sees it.
- Mutating guard: refuse while RHI.exe is running (shared file) unless `--force`.

Acceptance:
- `config get --key ReShadeChannel` returns the current value.
- set → get round-trip for a harmless key (e.g. `UlTargetFps`), then restore.
- `--dry-run` writes nothing; missing `--yes` exits 2.
- `config list --json` parses and contains at least the keys used by `status`/installs.

### 9.2 `game reset-overrides` — **done 2026-11-08**

Implemented in `RHI.Cli/Commands/GameOverridesReset.cs`; clears 45 groups by name and by
`Name|Store`, removes the name mapping, reverts the DLL override on disk, and leaves component
files in place. Two checks in the verify script.

Why: the GUI can reset per-game overrides; the CLI cannot.

Files: `RHI.Cli/Commands/GameCommand.cs` (new verb), help, verify check.

Key APIs: `IGameNameService` (see `Services/IGameNameService.cs`). The per-game dictionaries are
keyed **either by game name or by composite `"GameName|Store"`** — check each property's XML
comment. The curated reset set should cover:
- identity/paths: `NameMappings`, `GameRenames`, `OriginalDetectedNames`, `FolderOverrides`,
  `BitnessOverrides`, `ApiOverrides`, `VulkanRenderingPaths`
- ReShade: `ReShadeChannelOverrides`, `CustomReShadeSelection`, `NormalReShadeGames`,
  `RsAsiGames`, `RsIniLockedGames`, `PerGameShaderMode`, `PerGameShaderSelection`
- RenoDX: `UeExtendedGames`, `UeExtendedOptOutGames`
- OptiScaler: `OsVariantOverrides`, `OsNightlyBuild`, `OsNrRuntime`, `NrMethodOverrides`,
  `NrAddonVersion`, `NrDllVersion`, `NrPackVersion`, `OsFgInput`, `OsFgOutput`,
  `OsFgNvngxReplacement`, `OsStreamlineVersion`, `OsFsrCrashFix`, `OsDeployStreamline`,
  `OsDeployDlssEnabler`, `OsDilatedMotionVectorsOff`, `OsFsrFgSwapchain`, `OsUpscalerPlugin`
- extras: `UalInstalledAs`, `DeInstalledAs`, `Rtx40MfgInstalledAs`, `Dlssg2030InstalledAs`,
  `RtxEncoreInstalledAs`, `RtxEncoreNrDllDeployed`, `DgVoodooStandaloneGames`,
  `DgVoodooVersionOverride`, `PerGameAddonMode`, `PerGameAddonSelection`
- display: `HdrToggleOverrides`, `ResToggleOverrides`
- launch: `LaunchExeOverrides`, `LaunchArgsOverrides`, `EngineVersionOverrides`

Do **not** clear `HiddenGames`, `FavouriteGames`, `RemovedGames`, `WikiExclusions`,
`UpdateAllExcluded*` — those are library preferences, not overrides.

Acceptance: `--dry-run` lists exactly which keys would be removed; `--yes` clears only existing
entries; `status` unchanged; GUI shows defaults after restart.

### 9.3 Capture RenoDX **Advanced** (third mode)

Recipe (no desktop capture, no focus steal):
```powershell
$p = "C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI\rhictl.exe"
& $p game launch --game "Death in the Water 2"
Start-Sleep 20
$probe = "C:\Users\cattu\Documents\Directories\ss tool\probe.ps1"
& $probe -Action postkey -ProcessName "Death in the Water 2" -Key Home
& $probe -Action postclick -ProcessName "Death in the Water 2" -X 636 -Y 21   # RenoDX tab
& $probe -Action postclick -ProcessName "Death in the Water 2" -X 252 -Y 80   # mode button
& $probe -Action postclick -ProcessName "Death in the Water 2" -X 252 -Y 80   # -> Advanced
& $probe -Action shot -ProcessName "Death in the Water 2" -Path .\renodx-advanced.png -MaxWidth 1600
```
Coordinates are client pixels for a 2560×1440 client; re-derive from a capture if the layout
differs. Then update `docs/IN-GAME-MENU.md` §RenoDX and `docs/EVERYTHING.md` §9.8, and mark the
new keys **verified**.

Acceptance: a capture showing the Advanced panel; doc lists any keys not present in Intermediate;
`SettingsMode=2` observed in `reshade.ini`.

### 9.4 Capture OptiScaler / Display Commander in-game menus

Needs a DX12 game with those installed. Do not force-install them into the Unity test game.
Capture their tabs with the same `probe.ps1` flow, add a section to `IN-GAME-MENU.md`.
Acceptance: at least one capture per addon; key tables mapped to their ini files.

### 9.5 Vulkan install end-to-end (elevated)

```powershell
# elevated terminal
.\rhictl.exe vulkan install --layer-only --yes
.\rhictl.exe vulkan status --json
.\rhictl.exe vulkan uninstall --layer-only --yes
```
Acceptance: `LayerDirectory` + HKLM layer entry created and removed; `status` flips; no leftovers
in `C:\ProgramData\ReShade`.

### 9.6 NVIDIA write test (+ add `nvidia restore`)

There is **no `nvidia restore` verb yet** — `nvidia backup` writes a JSON snapshot but nothing
reads it back. Implement `nvidia restore --backup <file> --game <n> --yes` (read the JSON, set
each value, clear when null). Then test on the test game:
```powershell
.\rhictl.exe nvidia backup --game "Death in the Water 2" --json
.\rhictl.exe nvidia set --game "Death in the Water 2" --key fps-limit --value 60 --yes
.\rhictl.exe nvidia status --game "Death in the Water 2" --json
.\rhictl.exe nvidia set --game "Death in the Water 2" --key fps-limit --value 0 --yes   # 0 clears
```
Acceptance: round-trip, profile not corrupted, backup restores.

### 9.7 In-process `update all`

Replace the child-process calls in `RHI.Cli/Commands/UpdateAllCommand.cs` with direct service
calls. Factor the per-component update body out of each command into an internal method that takes
`(IServiceProvider, GameContext, args, progress)`.

Acceptance: identical results to the current child-process version, no spawned `rhictl.exe`,
noticeably faster on a 19-game library.

### 9.8 MCP progress

Add `notifications/progress` for long installs (initialize must advertise
`capabilities.tools` + progress token handling). Acceptance: a client sees periodic progress
during `rhi_reshade` install instead of one blocking call.

### 9.9 `--store` disambiguation

`GameResolver` currently ignores `--store`; `Args` parses it. Use it when several library entries
share a name. Acceptance: two entries with the same name, different stores; `--store` selects the
right one; without it, fail with a clear ambiguity error (do not silently pick the first).

### 9.10 `RHI_STATE_DIR` isolation

`AppPaths` computes `%LOCALAPPDATA%\RHI`. Add an override (env var or CLI flag) read once at
startup, defaulting to the current path. Acceptance: `paths --json` reflects the override; the GUI
is unaffected; default unchanged.

---

## 10. Traps that cost time (read before editing)

1. **`PeHeaderService.FindGameExe` returns the largest exe** → `UnityCrashHandler64.exe` on Unity
   games. The CLI uses `ExeLocator` (product-name scoring). Do not swap it back.
2. **Records must be matched by install path**, never by name. Two copies of a game otherwise
   report/uninstall each other's components.
3. **Favourites/hidden use composite `"GameName|Store"` keys**, not plain names.
4. **`IReShadeUpdateService.EnsureLatestAsync` returns `false` when already current.** Verify the
   staged DLL exists; do not treat `false` as failure.
5. **`ShaderPackService.SyncGameFolder(null|empty)` removes managed shaders.** Never pass null
   casually.
6. **`IDofFixService` has a `static abstract` member** → cannot be a DI generic argument. Resolve
   concrete `DofFixService`.
7. **`Rtx40MfgService.EnsureStagingAsync()` takes no progress argument.**
8. **NVAPI has no NR driver-override setter**; only sr/rr/fg.
9. **`relimiter.ini` template ships `[UltraLimiter]`; all writers use `[FrameLimiter]`.** CLI install renames the section.
10. **RenoDX LUT warning**: `colorGradeInternalLUTShaper != 0` without the output shader produces
    "Missing output shader"; set it to `0`.
11. **OptiScaler top-level keys** (`ShortcutKey`, `LoadReshade`, `LoadAsiPlugins`) have **no
    section header**.
12. **`GameLibraryService.Save` drops `SteamAppId`/`Epic*`/`XboxAumid`** — the model only stores
    name/path/source. The GUI has the same limitation; the CLI round-trips every other
    collection.
13. **Upstream drift**: a merge can add UI types to `Services` (see `DragDropHandler`) or change
    service signatures. Always rebuild + verify after merging.
14. **Desktop capture is wrong for fullscreen games**: `CopyFromScreen` returns the compositor.
    Use `probe.ps1 -Action shot` (`PrintWindow`).
15. **Coordinates are client pixels**; multiply by `clientWidth / imageWidth` when using
    `-MaxWidth`.
16. **UIPI**: a non-elevated probe cannot send messages to an elevated window.
17. **UE games store the project root.** `game add` must keep `<Project>` (parent of
    `Binaries`), not `Binaries\Win64`; `GameResolver` walks up from `Binaries` to heal old
    entries. Addons deploy to the binaries folder, resolved by `ModInstallService`.
18. **`nvidia get` returns 0 for an unset key**, not `BAD_KEY`; only unknown key names are errors.
19. **RenoDX falls back to the generic engine addon** when the wiki match has no snapshot URL
    (Nexus-only entries). Do not treat that as `NO_MOD`.
20. **`reshade --dll` accepts a stem** (`dxgi`) and normalizes it to `dxgi.dll`; a raw stem used to
    create an extensionless file.
21. **ReLimiter install normalizes** the template's `[UltraLimiter]` to `[FrameLimiter]`; a fresh
    template has no keys, so `ini-get target_fps` is legitimately `KEY_NOT_FOUND` until set.
22. **NR versions carry a GPU suffix** in the manifest (`310.8.2 (20/30/40/50)`); resolve the base
    version to the display entry before swapping, or `FindDlssnrEntry` misses and the swap no-ops.
23. **The shared DLSS download helper capped every archive at 120 s.** NR zips are ~250 MB and died
    at ~95 MB; the cap is now 10 minutes (`DlssStreamlineService.Swap.cs`). Verify the post-swap
    version with the numeric `FileMajorPart/Minor/Build/Private` parts — one NR build reports
    `310.8.SF.0` as its version string while the numeric parts are correct.
24. **DOF Fix needs a versioned engine hint** (`"Unreal Engine 5.2"`), not bare `"Unreal"`.
    `GameContext.EngineHint` now carries `DetectUeVersion()` output.

---

## 11. File map (what is where)

```
Denuvo\RHI\
  README.md                  upstream README + fork banner (GitHub front page)
  RHICTL.md                  stock-vs-fork overview
  LICENSE                    GPL-3.0 (upstream)
  RHI.Core\
    RHI.Core.csproj          links $(RhiSourceDir)\Services + Models
    Shims\
      DispatcherQueueShim.cs
      XamlShim.cs
      GameCardViewModelShim.cs
      AppShim.cs
      MainViewModelShim.cs
      DragDropHandlerShim.cs       (manual copy — keep in sync)
  RHI.Cli\
    RHI.Cli.csproj
    Program.cs
    README.md
    PROGRESS.md
    Cli\                     Args, CliApp, CliServices, CliOutput, CliGuards,
                             CliDownloads, CliState, ExeLocator, GameResolver,
                             IniEditor, LibraryStore, ReShadeDllName
    Commands\                Addons, AuxComponent, Display, Dlss, Doctor, Extras,
                             Game, Ini, Library, Limiters, Luma, Mcp, Nvidia,
                             Presets, Renodx, RenodxSettings, ReShade, Shaders,
                             Status, UpdateAll, Vulkan
  tools\
    verify-rhictl.ps1        the 38-check suite (run this)
  docs\
    HANDOFF.md               this file
    EVERYTHING.md            deep reference (~100 KB)
    IN-GAME-MENU.md          ReShade/RenoDX/ReLimiter overlay reference
    IN-GAME-UI.md            file-backed vs in-game-only research
    GUI-INVENTORY.md         GUI -> CLI coverage matrix
    GUI-INVENTORY-raw.txt    raw 359-element accessibility dump
    PROBE.md                 window probe guide
    in-game-captures\        10 window-only PNGs (09–19, no 15)
```

Other locations:

```
Denuvo\RHI-CLI\               published build (rhictl.exe + deps + 7z + templates + probe.ps1)
Denuvo\RHI-CLI-Repo\          standalone snapshot for a separate push (no app source)
"C:\Users\cattu\Documents\Directories\ss tool\"
    probe.ps1                 universal window probe
    PROBE.md                  its guide
%LOCALAPPDATA%\RHI\           shared state with the GUI
```

---

## 12. Which document to read, in what order

1. **`docs/HANDOFF.md`** (this) — state, resume, next tasks.
2. **`RHI.Cli/PROGRESS.md`** — short status + pitfall list.
3. **`RHI.Cli/README.md`** — build/usage/command list.
4. **`docs/EVERYTHING.md`** — everything: architecture, every command, component internals,
   in-game menu, persistence map, safety, verification log, source map (appendices A–M).
5. **`docs/IN-GAME-MENU.md`** — overlay tabs, keys, confidence markers.
6. **`docs/GUI-INVENTORY.md`** — every GUI control → CLI verb or `todo`.
7. **`docs/PROBE.md`** — driving/capturing any window.

Do not re-derive what these already state. If you change behaviour, update the relevant doc in the
same commit.

---

## 13. What not to do

- **Do not edit `RenoDXCommander/`** unless a feature genuinely requires it. The fork's value is
  that the app stays untouched except the documented DLSS timeout fix; every other app edit widens the diff and risks the GUI.
- **Do not commit** `RHI-Setup.exe`, `bin/`, `obj/`, `dist/`, `%LOCALAPPDATA%\RHI`, or anything
  containing tokens/paths from the user's machine beyond what the docs already contain.
- **Do not push to `origin`** (upstream). Push only to `fork`.
- **Do not force-push `main`** — the fork is public.
- **Do not use `CopyFromScreen`** for game captures; use `probe.ps1 -Action shot`.
- **Do not run mutating commands without `--dry-run` first** on a new game; the test game is
  disposable, the user's real library is not.
- **Do not let a subagent touch the game/window** while another process is driving it; the probe
  is single-operator.
- **Do not treat the standalone `RHI-CLI-Repo` as live** — it is a snapshot; sync it manually.

---

## 14. Current uncommitted / untracked state at handoff

| Path | State |
|---|---|
| `RHI-Setup.exe` | untracked, pre-existing, **never commit** |
| `docs/HANDOFF.md` | committed 2026-11-08 |
| `tools/verify-rhictl.ps1` | committed 2026-11-08 |
| `RHI.Cli/Commands/ConfigCommand.cs` | committed 2026-11-08 |
| `RHI.Cli/Commands/GameOverridesReset.cs` | committed 2026-11-08 |
| `RHI.Cli/Commands/GameOverridesSet.cs` | committed 2026-11-08 |
| `RHI.Cli/Commands/FeederCommand.cs` | committed 2026-11-08 |
| `RenoDXCommander/Services/DlssStreamlineService.Swap.cs` | **modified** — 120 s → 10 min DLSS download cap |
| `RHI-CLI-Repo` | snapshot updated with `DragDropHandlerShim.cs` + `ExtrasCommand.cs`; not a git repo |
| `ss tool\probe.ps1`, `ss tool\PROBE.md` | current universal versions |
| `%LOCALAPPDATA%\RHI` | shared state; do not commit |

The parity batch is committed locally (2026-11-08). Pushing is the remaining step:

```powershell
git -c http.version=HTTP/1.1 -c http.postBuffer=524288000 push fork main:main
```

---

## Appendix A — Command surface (85 verbs, 21 groups)

```
doctor
paths
status
config list|get|set|unset
library list|show|refresh
game add|remove|launch|favourite|unfavourite|hide|unhide|set-path
game set-launch|reset-launch|set-wiki-name|reset-wiki-name|set-name|set-dll|reset-dll|set-override|clear-override|reset-overrides
reshade install|update|uninstall
reshade|renodx|relimiter|display-commander|optiscaler ini-list|ini-get|ini-set
shaders list|install|remove
addons list|install|remove
renodx install|update|uninstall|mode|peak-nits|ue-extended
feeder status|install|uninstall
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

Exit codes: `0` ok · `2` usage · `3` not found · `4` game/RHI running · `5` permission ·
`7` failed (`6` reserved for network).

---

## Appendix B — Git commits at handoff

```
50ba26e  Add rhictl banner to the front page
bf33a4c  Document the fork on the front page
20f544f  Fix RHI.Core against upstream service changes
0b36b16  Merge pull request #1 from toolazyforname1-glitch/rhictl
e551fbf  Add rhictl: headless CLI for RHI
```

Fork: `https://github.com/toolazyforname1-glitch/RHI-CLI`
Upstream: `https://github.com/RankFTW/RHI`

---

## Appendix C — Test game state (historical; test games removed 2026-11-08)

No test game is registered or installed any more. The table below records the state used for the
2026-11-08 verification runs. Re-add a title with `game add --path <dir> --name <n>` before rerunning
the live parts of this document.

| File | Source |
|---|---|
| `dxgi.dll` (ReShade 6.8.0) | `rhictl reshade install` |
| `renodx-unityengine.addon64` (26.1014.1445) | `rhictl renodx install` |
| `relimiter.addon64` (3.3.5) + `relimiter.ini` | `rhictl relimiter install` / addon |
| `reshade.ini` (+ `[renodx]`, `[renodx-preset1]`) | ReShade + RenoDX |
| `reshade.ini.cli-backup` | `IniEditor` |
| `ReShade.log`, `relimiter_*.log`, screenshot PNG | ReShade/ReLimiter |

Restored values after testing: `SettingsMode=1`, `target_fps=0`, `ToneMapPeakNits=1000`.

The game's Unity prefs live at `HKCU\Software\Lighthouse Games Studio\Death in the Water 2`
(`Screenmanager Fullscreen mode=1`, borderless) — relevant to `PrintWindow` capture.

---

## Appendix D — Service APIs you will need (verified names)

| Need | API |
|---|---|
| Read settings | `SettingsFile.Load()` / `SettingsFile.Snapshot()` |
| Write settings safely | `SettingsFile.Update(Action<Dictionary<string,string>>, string callerTag)` |
| Load into VM | `SettingsViewModel.LoadSettingsFromDict(Dictionary<string,string>)` |
| Save from VM | `SettingsViewModel.SaveSettingsToDict(Dictionary<string,string>)` |
| Load all per-game state | `IGameNameService.LoadNameMappings(...)` |
| Persist per-game state | `IGameNameService.SaveNameMappings(...)` + `FlushPendingSave()` |
| Game library | `IGameLibraryService.Load()` / `Save(...)` |
| ReShade record | `IAuxInstallService.FindRecord` / `LoadAll` / `CheckForUpdateAsync` |
| RenoDX record | `IModInstallService.FindRecord` / `LoadAll` / `CheckForUpdateAsync` |
| Addon deploy path | `ModInstallService.GetAddonDeployPath(path)` |
| Shader packs | `IShaderPackService.EnsurePacksAsync` / `SyncGameFolder` / `RemoveFromGameFolder` |
| DLSS/Streamline | `IDlssStreamlineService.Detect` / `FetchManifestAsync` / `Swap*` / `Restore*` |
| NVIDIA | `DlssPresetService.Initialize` + getters/setters; `IDldsrService` |
| Display | `HdrToggleService`, `ResolutionToggleService`, `DigitalVibranceService` |
| Vulkan | `VulkanLayerService.IsLayerInstalled/InstallLayer/UninstallLayer`, `VulkanFootprintService` |

---

## Appendix E — Definition of done for the next change

- [ ] `dotnet build RHI.Cli\RHI.Cli.csproj -c Release` → 0 errors
- [ ] `dotnet publish ...` → 0 errors
- [ ] `tools\verify-rhictl.ps1` → 50/50 (or the new count, with new checks added)
- [ ] no `bin/`/`obj/`/state/`RHI-Setup.exe` staged
- [ ] docs updated (`HANDOFF`, `EVERYTHING`, `GUI-INVENTORY` if a GUI item moved)
- [ ] commit + push to `fork main` (HTTP/1.1 flags)
- [ ] optional: fresh clone of the fork builds

---

*End of handoff.*




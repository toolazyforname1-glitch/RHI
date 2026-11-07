# In-game settings reference (what is file-backed, what is not)

Purpose: decide what rhictl can configure from disk, and what only exists inside the game's
ReShade overlay. Researched 2026-11-05. This is why some GUI/in-game options will never have a
CLI verb.

## ReShade — `reshade.ini`

Per-instance config lives next to the ReShade DLL; the global (Vulkan/global) copy lives in
`C:\ProgramData\ReShade\ReShade.ini`. RHI already reads/writes both.

| Section | Keys worth exposing | Notes |
|---|---|---|
| `[GENERAL]` | `EffectSearchPaths`, `TextureSearchPaths`, `PerformanceMode`, `PreprocessorDefinitions`, `PresetPath`, `StartupPresetPath`, `SkipLoadingDisabledEffects`, `IntermediateCachePath`, `NoEffectCache`, `NoReloadOnInit`, `PresetTransitionDuration` | comma-separated lists; literal comma is escaped as `,,` |
| `[INPUT]` | `KeyOverlay`, `KeyScreenshot`, `KeyEffects`, `KeyReload`, `KeyPerformanceMode`, `KeyNextPreset`, `KeyPreviousPreset`, `ForceShortcutModifiers`, `InputProcessing`, `GamepadNavigation` | value format `<vk>,<ctrl>,<shift>,<alt>`, e.g. `36,0,0,0` = Home |
| `[SCREENSHOT]` | `SavePath`, `FileFormat` (0 BMP / 1 PNG / 2 JPEG), `JPEGQuality`, `ClearAlpha`, `SaveBeforeShot`, `SavePresetFile`, `SaveOverlayShot`, `SoundPath`, `PostSaveCommand*` | |
| `[OVERLAY]` | `ShowClock`, `ShowFPS`, `ShowFrameTime`, `FPSPosition`, `ClockFormat`, `NoFontScaling`, `TutorialProgress`, `AutoSavePreset` | pure overlay UI |
| `[STYLE]` | `Alpha`, `ColFPSText`, `Font`, `FontSize`, `EditorFontSize`, `HdrOverlayBrightness`, `HdrOverlayOverwriteColorSpaceTo` | overlay look only |

Consequence: hotkeys, screenshot folder/format and performance mode are **file-backed** → CLI-able.
Overlay layout/theme is also file-backed but cosmetic; low value for a CLI.

Sources: ReShade forum key reference (reshade.me/forum/general-discussion/9075),
crosire/reshade configuration docs (deepwiki.com/crosire/reshade/2.2-configuration-management).

## RenoDX — addon with an in-game menu

- Install = `renodx-<game>.addon64` (or generic `renodx-unityengine.addon64` /
  `renodx-unrealengine.addon64`) next to the game exe, loaded by ReShade as an addon.
- Settings are exposed in the **ReShade overlay** (Home) on a RenoDX tab. Documented controls:
  Peak Brightness, Game Brightness (paper white), SDR EOTF Emulation, Tone Mapper
  (PsychoV variants / RenoDRT / ACES / Vanilla), Exposure, Highlights, Shadows, Dynamic Exposure,
  film grain, saturation.
- There is **no documented standalone RenoDX ini**. Persistence is through the ReShade preset
  (`*.ini` containing `Techniques`), which is why RHI writes `ToneMapPeakNits` into RenoDX presets.
- UE-Extended additionally needs `Engine.ini`:

```ini
[SystemSettings]
r.AllowHDR=1
r.HDR.EnableHDROutput=1
r.HDR.Display.OutputDevice=3
r.HDR.Display.ColorGamut=2
r.HDR.UI.CompositeMode=1

[/Script/Engine.RendererSettings]
r.LUT.UpdateEveryFrame=1
```

  and the file is usually made **read-only** so the game cannot overwrite it. This is file-backed →
  CLI-able (the GUI already does it, and RHI's manifest can override the engine-ini path).
- Compatibility keys (`[renodx]` section: swapchain proxy, encoding, resource upgrades) are written
  into `reshade.ini` and only appear after the game has run once with RenoDX loaded.

Consequence: peak nits / tone mapping / exposure are **in-game only** unless written into a preset
file. RHI's approach (write `ToneMapPeakNits` to presets) is the file-backed path; the CLI can do the
same. Per-scene tuning stays manual.

Sources: hdrgameanalysis.com RenoDX UE-Extended guide, clshortfuse/renodx discussions,
RHI `docs/DETAILED_GUIDE.md`.

## Luma — `.toml` next to the plugin

- Luma is a ReShade-addon framework (DX11). Files go in the game exe folder.
- Settings are exposed **in the game's display menu** and in the ReShade overlay widget.
- Configuration is a **`.toml` file next to the plugin**; deleting it resets to defaults.
  Example keys documented: `UpgradeExtraRenderTargets` (NVIDIA, more colour-buffer quality).
- HDR10 does not work with FSR frame generation; Luma falls back to scRGB automatically.

Consequence: the `.toml` is file-backed and CLI-editable; the in-game menu is not. RHI does not
currently expose Luma toml editing, so this is a possible CLI-only advantage later.

Sources: Filoppi/Luma-Framework GitHub + releases, Starfield Nexus Luma docs page.

## OptiScaler — `OptiScaler.ini`

Large, well-documented ini. Sections seen in the upstream file:

| Section | Key examples |
|---|---|
| `[Upscalers]` | `Dx11Upscaler`, `Dx12Upscaler`, `VulkanUpscaler` (`auto`, `dlss`, `fsr21/22/31`, `ffx`, `xess`, `xess_12`, `fsr22_12`, custom folder name) |
| `[FrameGen]` | `Enabled`, `FGInput` (`dlssg`, `nvngxfg`, `fsrfg`, `upscaler`, `fsrfg30`, `xefg`, `nofg`), `FGOutput`, `FGNvngxReplacement` (`None`, `Nukems`, `Arturs`, `FFX`, `Combo`), `FTInput`, `DebugView` |
| `[DLSSG]` | `InterpolationCount` (1=2x … 5=6x), `UseGamesReflexMarkers`, `OverrideInterpolationCount`, `OverrideForceDMFG`, `ForceDMFG`, `FramerateTargetDMFG` |
| `[Framerate]` | `FramerateLimit` (uses Reflex when available) |
| other | `ForceReflex`, `LowLatencyInput`/`Output`, `ForceXeLL`, `LatencyFlexMode`, `UpscalerIndex` |

Important: **FG changes require Save INI + game restart**; they do not apply live.
`InterpolationCount` + `FramerateTargetDMFG` are the file-backed equivalent of RHI's
Fixed/Dynamic MFG controls.

Sources: optiscaler/OptiScaler `OptiScaler.ini` upstream, OptiScaler wiki (Frame Generation Options,
OptiFG), ContextQMD OptiScaler config page.

## DLSS presets and versions

- Preset letters are driver-profile settings keyed per game and per component (SR/RR/FG),
  with transformer presets up to **L** and **M** on DLSS 4.5. RHI writes them through NVAPI
  (`DlssPresetService`), not through game files.
- Version swaps are plain DLL replacement: `nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll`,
  `nvngx_dlssnr.dll`, plus `sl.*.dll` for Streamline. Fully CLI-able (implemented in `rhictl dlss`).

## What will always stay manual

- ReShade overlay navigation itself (mouse/keyboard inside the running game).
- RenoDX in-game tone mapping / exposure / grading sliders (unless mirrored into a preset file).
- Luma in-game display menu (unless mirrored into its `.toml`).
- OptiScaler's live overlay tuning; only saved ini changes persist.
- Any setting the game itself owns (HDR toggle, resolution, DLSS quality) — RHI can change
  Windows/driver state around the game, not the game's own menu.

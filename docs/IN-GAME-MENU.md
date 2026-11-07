# In-game menu reference — ReShade 6.8 + RenoDX + ReLimiter

Captured live on **Death in the Water 2** (Unity, DX11, 64-bit), 2026-11-05, with
ReShade 6.8.0 + RenoDX generic Unity 26.1014.1445 + ReLimiter 3.3.5.

Screenshots: `docs/in-game-captures/` (01–19). All captures are window-specific
(`PrintWindow`), no desktop capture.

## Opening the overlay

- Default key: **Home** (`[INPUT] KeyOverlay=36,0,0,0`).
- The overlay is rendered by ReShade inside the game's swapchain, so a window capture
  includes it.
- Automation note: ReShade's input path accepts **window messages**, so the overlay can be
  driven with `WM_KEYDOWN` / `WM_LBUTTONDOWN` sent to the game window **without stealing
  focus**. That is how these captures were made. Useful for any future headless UI test.

## Tab inventory

| Tab | Purpose |
|---|---|
| Home | Preset bar, effect search, effect list, Reload, Performance Mode |
| Add-ons | Loaded addons and their enable checkboxes |
| Settings | ReShade's own settings: General, Screenshots, Overlay & Styling |
| Statistics | Runtime frame statistics (not captured; not configuration) |
| Log | ReShade log (not captured) |
| About | Version/credits (not captured) |
| ReLimiter | ReLimiter frame limiter + OSD + screen + advanced |
| RenoDX | RenoDX tone mapping, color grading, effects, presets |

The last two tabs only exist because those addons are installed. With neither installed
they are absent; with only one, only that tab appears.

## Home

Captured: `09-postkey.png`.

- Preset selector (`ReShadePreset`), **Active to top**, **Auto Save: On**, folder/save/plus buttons.
- Search box.
- Effect tree (empty on this game because global shaders were off: "No effect files (.fx)
  found in the configured effect search paths…").
- **Reload**, **Performance Mode**.
- FPS counter top-right.
- Global preprocessor definitions editor at the bottom.

## Add-ons

Captured: `14-addons.png`.

- **Add-on search path: `.\`**
- Search box.
- Addons shown: **ReLimiter** (checked), **RenoDX** (checked), **Generic Depth** (crosire,
  unchecked), **Effect Runtime Sync** (crosire, unchecked).
- The unchecked two are listed because the template writes
  `[ADDON] DisabledAddons=Generic Depth,Effect Runtime Sync` into `reshade.ini`.
- "Open developer documentation" link at the bottom.

## Settings — General / Screenshots / Overlay & Styling

Captured: `13-settings.png`. Mapping below combines what the UI showed with the ReShade
config reference. **Confidence** marks how sure the ini key name is.

### General

| UI control | ini key | Value / notes | Confidence |
|---|---|---|---|
| Input processing = "Block all input when overlay is visible" | `[INPUT] InputProcessing` | `0` pass all · `1` block while cursor over overlay · `2` block all when visible | high |
| Overlay key = Home | `[INPUT] KeyOverlay` | `36,0,0,0` (VK,ctrl,shift,alt) | verified |
| Effect toggle key | `[INPUT] KeyEffects` | same 4-int form | high |
| Effect reload key | `[INPUT] KeyReload` | same 4-int form | high |
| Previous / Next preset key | `[INPUT] KeyPreviousPreset` / `KeyNextPreset` | same 4-int form | high |
| Preset transition duration = 1000 | `[GENERAL] PresetTransitionDuration` | ms | high |
| Start-up preset = (none) | `[GENERAL] StartupPresetPath` | path; empty = last used | high |
| Effect search paths = `.\reshade-shaders\Shaders\**` | `[GENERAL] EffectSearchPaths` | comma-separated globs | verified in file |
| Texture search paths = `.\reshade-shaders\Textures\**` | `[GENERAL] TextureSearchPaths` | comma-separated globs | verified in file |
| Load only enabled effects | `[GENERAL] SkipLoadingDisabledEffects` | 0/1 | high |
| Clear effect cache (button) | *(no key)* | deletes the effect cache | high |

### Screenshots

| UI control | ini key | Value / notes | Confidence |
|---|---|---|---|
| Screenshot key = Print Screen | `[INPUT] KeyScreenshot` | `44,0,0,0` | high |
| Screenshot path = `.\` | `[SCREENSHOT] SavePath` | directory | high |
| Screenshot name = `%AppName% %Date% %Time%_%Count%` | likely folded into `[SCREENSHOT] SavePath` in 6.x, or a separate name key | macros `%AppName% %Date% %Time% %Count%` | **likely / undocumented** |
| Screenshot format = PNG | `[SCREENSHOT] FileFormat` | `0` BMP · `1` PNG · `2` JPEG | high |
| Save current preset file | `[SCREENSHOT] SavePresetFile` | 0/1 | high |
| Save before and after images | `[SCREENSHOT] SaveBeforeShot` | 0/1 | high |
| Save separate image with the overlay visible | `[SCREENSHOT] SaveOverlayShot` | 0/1 | high |
| Screenshot sound = sound.wav | `[SCREENSHOT] SoundPath` | path | high |
| Post-save command / arguments / working dir / hide window | `[SCREENSHOT] PostSaveCommand`, `PostSaveCommandArguments`, `PostSaveCommandWorkingDirectory`, `PostSaveCommandNoWindow` | path / string / path / 0/1 | high |

### Overlay & Styling

| UI control | ini key | Value / notes | Confidence |
|---|---|---|---|
| Language = System Default | likely `[OVERLAY] Language` | locale string / empty | **likely** |
| Restart tutorial (button) | `[OVERLAY] TutorialProgress` | set `0` to restart | high |
| Show screenshot message | `[OVERLAY] ShowScreenshotMessage` | 0/1 | high |
| Show preset transition message | `[OVERLAY] ShowPresetTransitionMessage` | 0/1 | high |
| Group effect files with tabs instead of a tree | `[OVERLAY] VariableListUseTabs` | 0/1 (template sets 1) | verified |
| Global style = Default / Dark | `[STYLE] StyleIndex` | integer enum; exact index map undocumented | **likely** |
| Text editor style = Dark | `[STYLE] EditorStyleIndex` | integer enum | **likely** |
| Global font / size | `[STYLE] Font`, `FontSize` | `ProggyClean.ttf`, `13` | high |
| Text editor font / size | `[STYLE] EditorFont`, `EditorFontSize` | `ProggyClean.ttf`, `13` | high |
| Global alpha = 1.00 | `[STYLE] Alpha` | 0.0–1.0 | high |
| HDR overlay brightness = 203 nits | `[STYLE] HdrOverlayBrightness` | float nits | high |
| Overlay color space = Auto | `[STYLE] HdrOverlayOverwriteColorSpaceTo` | `0` auto · `1` sRGB · `2` extended sRGB · `3` HDR10 ST2084 · `4` HLG | medium |
| Frame rounding = 0 | `[STYLE] FrameRounding` | float | high |
| Show clock / FPS / frame time | `[OVERLAY] ShowClock`, `ShowFPS`, `ShowFrameTime` | 0/1/2 | high |
| Show preset name | likely `[OVERLAY] ShowPresetName` | 0/1 | **likely** |
| FPS position | `[OVERLAY] FPSPosition` | 0–3 | high |

Per-game `reshade.ini` **overrides** the global `C:\ProgramData\ReShade\ReShade.ini`
(section/key level); the global copy is the fallback used by Vulkan/global layer installs.

Confidence: **verified** = read back from the file written during capture; **high** =
documented ReShade key; **likely/undocumented** = not confirmed for 6.8, do not treat as
authoritative.

---

# RenoDX tab

Captured: `11-renodx.png` (Simple mode) and `12-renodx-advanced.png` (Intermediate mode —
the mode button reads **Intermediate**; "Advanced" is a third step not captured).

Persistence: **`reshade.ini`**, sections `[renodx]` (framework + `SettingsMode`) and
`[renodx-preset1]` (all per-preset values). This is the same file ReShade uses, which is why
`rhictl renodx ini-*` points at `reshade.ini`.

## Modes

| `SettingsMode` | UI label | Evidence |
|---|---|---|
| 0 | Simple | observed before switching |
| 1 | Intermediate | **verified**: UI showed Intermediate and the file wrote `SettingsMode=1` |
| 2 | Advanced | likely (next step of the same button) |

## Tone Mapping (captured values in brackets)

| Key | UI label | Meaning | Confidence |
|---|---|---|---|
| `toneMapType` | Tone Mapper | Selects the tonemap curve. Value **2 = "Neutwo"**, verified by the UI dropdown + file. Lower values are vanilla/SDR paths. | **verified for 2**; other values addon-specific |
| `toneMapPeakNits` | Peak Brightness | Target peak white luminance [1000] | high |
| `toneMapGameNits` | Game Brightness | Reference white / SDR game brightness [203] | high |
| `toneMapUINits` | UI Brightness | Luminance the UI is mapped to [203] | high |
| `toneMapGammaCorrection` | (SDR EOTF area) | Gamma/EOTF correction flag [1] | likely |
| `toneMapScaling` | (Tone Map scaling) | Tonemap input/output scaling mode [2] | **undocumented** |
| `toneMapSDRHue` | SDR Hue | Hue handling for SDR→HDR [100] | likely |
| `toneMapSDRChrominance` | SDR Chrominance | Chrominance/saturation handling [100] | likely |
| `ToneMapSDRClip` | SDR Source Clipping | Clips SDR highlights before expansion [1.5] | likely |
| `rolloffUI` | (UI roll-off) | Highlight roll-off applied to UI [1] | likely |

The menu also showed **SDR EOTF Emulation = 2.2** as a separate row; the captured file has
no obvious `sdrEotf` key, so it is either derived from `toneMapGammaCorrection`/`toneMapScaling`
or stored elsewhere. Treat as **unmapped** for the CLI.

## Color Grading

| Key | UI label | Captured | Meaning / range | Confidence |
|---|---|---|---|---|
| `colorGradeExposure` | Exposure | 1.00 | linear exposure | high |
| `colorGradeHighlights` | Highlights | 50 | highlight lift/compression | high |
| `colorGradeShadows` | Shadows | 50 | shadow lift/crush | high |
| `colorGradeContrast` | Contrast | 50 | contrast around mid-grey | high |
| `colorGradeSaturation` | Saturation | 50 | global saturation | high |
| `colorGradeDechroma` | Highlights Saturation | 0 | de-saturates very bright pixels | likely |
| `colorGradeBlowout` | Blowout | 50 | highlight blowout to white | high |
| `colorGradeFlare` | Flare | 0 | highlight flare | high |
| `colorGradeClip` | Clipping | 100 | output clip point | high |
| `colorGradeInternalLUTScaling` | Internal LUT Strength (scaling) | 0 | internal-LUT domain rescale | likely |
| `colorGradeInternalLUTShaper` | LUT Shaper | 1 | 0 = **Vanilla** (see warning); 1/2 = custom shaper paths | **0 = Vanilla verified by warning**; 1/2 names undocumented |
| `colorGradeInternalLUTStrength` | Internal LUT Strength | 100 | blend strength | high |
| `colorGradeLUTSampling` | (LUT Sampling) | 1 | sampling filter enum | **undocumented** |
| `colorGradeUserLUTScaling` | User LUT Scaling | 0 | user-LUT domain rescale | likely |
| `colorGradeUserLUTStrength` | User LUT Strength | 100 | user-LUT blend | high |
| `colorGradeGamutCompression` | LUT Gamut Conservation | 0 | compress out-of-gamut instead of clipping | likely |
| `colorGradeColorSpace` | Output Color Space | 0 | working/output color-space enum | **undocumented values** |

**"Missing output shader — set LUT Shaper to Vanilla"**: the addon could not find/replace the
game's output shader that applies the internal LUT, so a non-Vanilla shaper has no shader to
run. The test game had `colorGradeInternalLUTShaper=1` **and** the warning visible. Setting it
to `0` (Vanilla) silences the warning and restores the vanilla output path.

## Effects

| Key | UI label | Captured | Meaning | Confidence |
|---|---|---|---|---|
| `fxBloom` | Bloom | 50 | bloom toggle/strength | high |
| `fxLens` | Lens Dirt/Flare | 50 | lens dirt/flare | high |
| `fxDoF` | Depth of Field | 100 | DoF toggle/strength | high |
| `fxCA` | Chromatic Aberration | 50 | chromatic aberration | high |
| `fxVignette` | Vignette | 50 | vignette | high |
| `fxNoise` | Dithering Noise | 50 | dithering/noise | high |
| `fxFilmGrain` | Film Grain | 50 | film-grain strength | high |
| `fxFilmGrainType` | Film Grain Type | 0 | **0 = "Vanilla"**, verified by the UI dropdown + file; other values addon-specific | **verified for 0** |
| `fxHdrBoost` | FakeHDR Boost | 0 | extra highlight expansion | high |

## Other `[renodx]` framework keys captured (not exposed in the menu)

`Blit_Copy_Hack`, `ForceBorderless`, `Force_Pipeline_Cloning`, `PreventFullscreen`,
`Swapchain_Encoding`, `Scaling_Offset`, `Tonemap_Offset`, `Use_Resource_Cloning`,
`Use_Swapchain_Proxy`, `Upgrade_*` — written by RHI at install (see the persistence map);
exposed in RHI's RenoDX cog, not in the in-game panel.

---

# ReLimiter tab

Captured: `10-relimiter-main.png` (main), `16-relimiter-osd.png`, `17-relimiter-screen.png`,
`19-relimiter-advanced2.png`.

Persistence: **`relimiter.ini`**, section **`[FrameLimiter]`** — note the shipped template only
contains `[UltraLimiter]`, but RHI and the addon write `[FrameLimiter]`.

## Frame pacing

| Key | Meaning | Values | Captured |
|---|---|---|---|
| `target_fps` | Foreground FPS cap | `0` = off, 30–1000 | 0 |
| `background_fps` | Cap when unfocused | `0` = uncapped | 30 |
| `fg_off_fps` | Cap while FG is off | `0` = disabled | 0 |
| `vsync_mode` | VSync override | `game` / `off` / `on` | game |
| `enforcement_marker` | Where the limiter sleeps | `SimulationStart` / `RenderSubmitStart` / `Present` | SimulationStart |
| `initial_wake_guard_us` | Wake guard (µs) | 0–10000 | 800 |
| `window_mode` | Window presentation | `default` / `borderless` / `fullscreen` | default |
| `fake_fullscreen` | Convert exclusive fullscreen | bool | false |
| `selected_monitor` | Target monitor | index | 0 |
| `focus_lock` | Keep foreground cap when unfocused (likely) | bool | false |

## OSD layout

| Key | Meaning | Captured |
|---|---|---|
| `osd_enabled` | Show OSD | false |
| `osd_x` / `osd_y` | Position (fraction of screen) | 0.005 / 0.005 |
| `osd_opacity` | Background opacity | 0.60 |
| `osd_scale` | Scale | 1.00 |
| `osd_drop_shadow` | Text shadow | true |
| `osd_text_brightness` | Text brightness | 1.00 |
| `osd_toggle_key` | Show/hide key | PageUp |

## OSD content toggles

All `false` by default in the capture; each enables one metric group:

- **Performance**: `osd_show_fps`, `osd_show_frametime`, `osd_show_frametime_graph`, `osd_show_1pct_low`, `osd_show_0_1pct_low`
- **Latency**: `osd_show_cpu_latency`, `osd_show_gpu_render_time`, `osd_show_total_frame_cost`, `osd_show_fg_time`
- **Quality**: `osd_show_pqi`, `osd_show_pqi_breakdown`, `osd_show_smoothness`
- **Pipeline**: `osd_show_fg`, `osd_show_limiter`, `osd_show_adaptive_smoothing`
- **System**: `osd_show_gpu_temp`, `osd_show_gpu_clock`, `osd_show_gpu_usage`, `osd_show_gpu_power`, `osd_show_vram`, `osd_show_cpu_usage`, `osd_show_ram`
- **DLSS**: `osd_show_dlss_quality`, `osd_show_dlss_features`, `osd_show_dlss_resolution`, `osd_show_dlss_presets`, `osd_show_dlss_versions`

## Presets / screen / advanced

| Key | Meaning | Values / captured |
|---|---|---|
| `shared_presets` | Store OSD presets across games | bool, false |
| `osd_preset_prev_key` / `osd_preset_next_key` | Cycle presets | key name, empty |
| `osd_position_cycle_key` | Cycle OSD position | key name, empty (likely) |
| `blackout_key` | Toggle blackout of other monitors | key name, empty |
| `oled_care_key` | Toggle OLED Care | key name, empty (likely) |
| `oled_care_all_monitors` | Apply to all monitors | bool, true (likely) |
| `oled_care_idle_minutes` | Auto-activate after idle | minutes, 0 = off (likely) |
| `adaptive_smoothing` | P99 render-time smoothing | bool, false |
| `smoothing_percentile` | Smoothing percentile | 0.50–0.999, 0.99 |
| `smoothing_window` | Sample window | `medium` (256f) / `dual` (64+512), medium |
| `smoothing_bias_us` | Extra headroom (µs) | 0–1000, 0.0 |
| `dynamic_mfg_passthrough` | Hand pacing to driver under DLSS 4.5 Dynamic MFG | bool, false |
| `reflex_inject` | Synthesize Reflex markers when the game lacks Reflex | bool, false |
| `flip_model_override` | Force DX11 FLIP_DISCARD (needs restart) | bool, false |
| `log_level` | Log verbosity | `error` / `warn` / `info` / `debug`, warn |
| `csv_enabled` | Per-frame CSV telemetry | bool, false |
| `dlss_info_hooks` | DLSS/NGX info hooks (disable if a game crashes) | bool, true (likely) |
| `streamline_compat` | Streamline FG detection; `force_false` disables proactive hooks | `true` / `force_false`, true |

Sources: ReLimiter `docs/USER_GUIDE.md`; keys not covered there are marked likely/undocumented.

---

# Persistence map — menu → file → CLI

All writes go through RHI's service layer; the CLI edits the same files.

| UI surface | File / section | RHI writer (file:line) | CLI |
|---|---|---|---|
| ReShade overlay key | `reshade.ini` `[INPUT] KeyOverlay` | `AuxInstallService.ApplyOverlayHotkey` (`AuxInstallService.Ini.cs:1367`) | `rhictl reshade ini-set --section INPUT --key KeyOverlay --value 36,0,0,0` |
| ReShade screenshot key | `[INPUT] KeyScreenshot` | `ApplyScreenshotHotkey` (`:1388`) | `rhictl reshade ini-set --section INPUT --key KeyScreenshot --value 44,0,0,0` |
| Screenshot folder | `[SCREENSHOT] SavePath` | `ApplyScreenshotPath` (`:1276`) | `rhictl reshade ini-set --section SCREENSHOT --key SavePath --value "D:\Shots"` |
| Effect list tabs | `[OVERLAY] VariableListUseTabs` | `ApplyVariableListUseTabs` (`:1408`) | `rhictl reshade ini-set --section OVERLAY --key VariableListUseTabs --value 1` |
| Addon enable/disable | `[ADDON] DisabledAddons` | template merge (`MergeRsIni`, `:12–74`) | `rhictl reshade ini-set --section ADDON --key DisabledAddons --value "..."` |
| RenoDX mode | `[renodx] SettingsMode` | `ApplyRenoDxNativeHdrSettings` (`:371–452`, value `2`) + cog | `rhictl renodx ini-set --section renodx --key SettingsMode --value 1` |
| RenoDX peak nits | `[renodx-preset1] toneMapPeakNits` | `ApplyPeakNits` (`:1296–1331`) | `rhictl renodx ini-set --section renodx-preset1 --key toneMapPeakNits --value 800` |
| RenoDX any preset key | `[renodx-preset1] …` | RenoDX addon at runtime (RHI only writes nits) | `rhictl renodx ini-set --section renodx-preset1 --key colorGradeSaturation --value 55` |
| ReLimiter cap | `relimiter.ini` `[FrameLimiter] target_fps` | `ApplyUlTargetFps` (`:1486`) | `rhictl relimiter ini-set --section FrameLimiter --key target_fps --value 60` |
| ReLimiter OSD key | `[FrameLimiter] osd_toggle_key` | `ApplyUlOsdHotkey` (`:1426`) | `rhictl relimiter ini-set --section FrameLimiter --key osd_toggle_key --value PageUp` |
| Display Commander cap | `DisplayCommander.ini` `[DisplayCommander] fps_limit` | `DisplayCommander.cs:86` | `rhictl display-commander ini-set --section DisplayCommander --key fps_limit --value 60` |
| OptiScaler FG | `OptiScaler.ini` `[FrameGen] Enabled` | `SetOptiScalerIniValue` (`OptiScalerService.Install.cs:2002`) | `rhictl optiscaler ini-set --section FrameGen --key Enabled --value true` |
| UE HDR keys | `Engine.ini` `[SystemSettings]` | `ApplyEngineIniHdrSettings` (`:847–947`) | file is read-only; edit with `Set-ItemProperty`/attrib first |

All `ini-set` verbs require `--yes` (or `--dry-run`) and refuse while the game or RHI is
running unless `--force`.

## Runtime ownership — what overwrites what

- **ReShade**: owns `[GENERAL]`, `[INPUT]`, `[SCREENSHOT]`, `[OVERLAY]`, `[STYLE]` at runtime.
  Values set by the CLI are seeds; changing them in the overlay overwrites them.
- **RenoDX**: reads `[renodx]` and `[renodx-preset*]`; writes the preset section when the user
  changes a slider. CLI writes persist until the addon saves over them.
- **ReLimiter / Display Commander**: own their ini files while the game runs. Write them
  **before** launching, or the addon will overwrite with its in-memory state.
- **Engine.ini**: RHI sets it read-only; no runtime component re-adds the keys.

## Known traps

1. `relimiter.ini` template ships `[UltraLimiter]` (empty) but every writer targets
   `[FrameLimiter]`.
2. `colorGradeInternalLUTShaper` non-zero without a matching output shader produces the
   "Missing output shader" warning; set `0` for Vanilla.
3. `SettingsMode` is 0/1/2 = Simple/Intermediate/Advanced (1 verified, 2 likely).
4. ReShade screenshot *name* template is the one key not confirmed for 6.8; path, format and
   overlay-shot toggle are confirmed.

---

# Capture index

All captures are window-specific (`PrintWindow` on the game window), not desktop captures.

| File | Shows |
|---|---|
| `09-postkey.png` | ReShade **Home** tab, opened via `PostMessage` without focus |
| `10-relimiter-main.png` | **ReLimiter** main panel: VRR cap, target/background/FG-off FPS, vsync, API/Reflex/Streamline status |
| `11-renodx.png` | **RenoDX** Simple mode: tone mapping, color grading, effects |
| `12-renodx-advanced.png` | **RenoDX** Intermediate mode: full tone mapping, LUT, effects list |
| `13-settings.png` | ReShade **Settings**: General, Screenshots, Overlay & Styling |
| `14-addons.png` | ReShade **Add-ons**: ReLimiter + RenoDX checked, Generic Depth / Effect Runtime Sync unchecked |
| `16-relimiter-osd.png` | ReLimiter **OSD** section expanded: position/opacity/scale/presets/metric toggles |
| `17-relimiter-screen.png` | ReLimiter **Screen** section: refresh/display/window mode, fake fullscreen, focus lock, blackout, OLED care |
| `18-relimiter-advanced.png` | ReLimiter **Advanced** (top): Reflex markers, flip model, telemetry, logging |
| `19-relimiter-advanced2.png` | ReLimiter **Advanced** (scrolled): DLSS info hooks, proactive Streamline hooks |

## Quick recipes

```powershell
# HDR brightness as exposed by the RenoDX in-game Tone Mapping slider (writes every preset)
rhictl renodx peak-nits --game "Death in the Water 2" --value 800 --yes

# switch RenoDX between Simple / Intermediate / Advanced
rhictl renodx mode --game "Death in the Water 2" --value intermediate --yes

# cap frames with ReLimiter before launching the game
rhictl relimiter ini-set --game "Death in the Water 2" --section FrameLimiter --key target_fps --value 60 --yes

# change the ReShade overlay key
rhictl reshade ini-set --game "Death in the Water 2" --section INPUT --key KeyOverlay --value 36,0,0,0 --yes
```

Write these **while the game is closed**: ReShade/RenoDX/ReLimiter rewrite their ini files
from memory while running, so an in-game edit wins over a CLI edit made mid-session.


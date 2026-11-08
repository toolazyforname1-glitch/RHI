# RHI GUI → rhictl coverage matrix

Source: live UI Automation dump of **RHI 2.8.9** (`C:\Program Files\RHI\RHI.exe`) taken on 2026-11-05,
359 automation elements, plus the app source for behaviour. Raw dump: `GUI-INVENTORY-raw.txt`.

Legend: **yes** = CLI verb exists · **dry** = implemented, verified only with `--dry-run` ·
**todo** = GUI action with no CLI equivalent yet.

## Top bar

| GUI control | AutomationId | rhictl |
|---|---|---|
| Quick Start | `FaqBtn` | todo (docs only) |
| Available Mods | `HdrModsListBtn` | todo |
| Refresh | `RefreshBtn` | `library refresh --yes` |
| Shaders/Addons | `ShadersAddonsBtn` | `shaders list`, `addons list|install|remove` |
| Update All | `UpdateBtn` | `update all --yes` |
| Links | `LinksBtn` | todo |
| Help | `SupportBtn` | n/a |
| Donate | `DonateBtn` | n/a |
| Settings | `SettingsBtn` | `config list|get|set|unset` |
| Filter games… | `SearchBox` | `library list` + shell filtering |
| + (add game) | `AddGameBtn` | `game add --path <dir>` |

## Filters

| GUI | AutomationId | rhictl |
|---|---|---|
| All Games | `FilterDetected` | `library list` |
| Installed | `FilterInstalled` | `status` per game (no aggregate filter yet) |
| Favourites / Hidden | `FilterFavourites` / `FilterHidden` | `library list --favourites` / `--hidden` |
| Unreal / Unity / Other | `FilterUnreal` / `FilterUnity` / `FilterOther` | `library list --engine unreal|unity|other` |
| RenoDX / Luma | `FilterRenoDX` / `FilterLuma` | `status --json` + filter on `components` |

## Game detail header

| GUI | AutomationId | rhictl |
|---|---|---|
| Launch | `DetailLaunchBtn` | `game launch --game <n>` |
| HDR toggle | `DetailHdrToggleBtn` | `hdr on|off` |
| RES toggle | `DetailResToggleBtn` | `res set` / `res restore` |
| Hide / Favourite | `DetailHideBtn` / `DetailFavBtn` | `game hide|unhide`, `game favourite|unfavourite` |
| Browse folder | `DetailFolderBtn` | `paths` / `status` gives the path |
| Copy Report | — | `status --json` |

## ReShade section

| GUI | AutomationId | rhictl |
|---|---|---|
| Install / Reinstall / Update | `DetailRsInstallBtn` | `reshade install --yes`, `reshade update --yes` |
| ReShade.ini ⚙ | `DetailRsIniBtn` | `reshade ini-list|ini-get|ini-set` |
| Uninstall | `DetailRsDeleteBtn` | `reshade uninstall --yes` |
| Status / version | — | `status` |

## RenoDX section

| GUI | AutomationId | rhictl |
|---|---|---|
| Install / Reinstall / Update | `DetailRdxInstallBtn` | `renodx install --yes`, `renodx update --yes` |
| UE-Extended toggle | `DetailUeExtendedBtn` | `renodx ue-extended --value on|off` |
| Uninstall | `DetailRdxDeleteBtn` | `renodx uninstall --yes` |
| Status / version | — | `status` |

## Components (limiters)

| GUI | AutomationId | rhictl |
|---|---|---|
| ReLimiter install | `DetailUlInstallBtn` | `relimiter install --yes` |
| ReLimiter ini | `DetailUlIniBtn` | `relimiter ini-list|ini-get|ini-set` |
| ReLimiter uninstall | `DetailUlDeleteBtn` | `relimiter uninstall --yes` |
| Display Commander install | `DetailDcInstallBtn` | `display-commander install --yes` |
| Display Commander ini / uninstall | `DetailDcIniBtn` / `DetailDcDeleteBtn` | `display-commander ini-list|ini-get|ini-set`, `display-commander uninstall --yes` |

## Extras / neural rendering

Observed buttons: `Install ASI Loader`, `Install OptiScaler`, `Install RTX Encore`,
`Install DLSS Enabler`, `Install 20/30 FG`, `Install Feeder Addon`, `Multi Frame Gen`,
`Deploy DLL`, `Quick Apply`, `Restore DLSS/SL`, `Restore Defaults`, `Change install folder`,
`Reset / Remove game`, `Reset Overrides`.

CLI today covers only:

| GUI | rhictl |
|---|---|
| OptiScaler install/uninstall | `optiscaler install|uninstall --yes` (dry) |
| DXVK install/uninstall | `dxvk install|uninstall --yes` (dry) |
| DLSS/Streamline swap/restore | `dlss set|restore --yes` (dry), `dlss versions` |
| DOF Fix | `dofix install|uninstall` |
| RE Framework | `reframework install|uninstall` |

Everything else in this group now maps to a CLI verb:

| GUI | rhictl |
|---|---|
| ASI Loader | `asi install|uninstall --game <n> --dll <name>` |
| RTX Encore | `rtx-encore install|uninstall --game <n> [--nr]` |
| DLSS Enabler | `dlss-enabler install|uninstall --game <n>` |
| 20/30 FG Unlock | `fg2030 install|uninstall --game <n>` |
| Universal MFG Unlocker ("Multi Frame Gen") | `mfg40 install|uninstall --game <n>` |
| dgVoodoo2 | `dgvoodoo install|uninstall --game <n> [--version <v>]` |
| Ultra+ | `ultraplus url --game <n>` (link only; the site does the install) |
| Deploy DLL / Quick Apply | covered per component via `--dll` |
| Restore DLSS/SL | `dlss restore --game <n>` |
| DLSS render scale | `dlss render-scale --game <n> --component sr|rr [--value <pct>]` |
| Change install folder | `game set-path --game <n> --path <dir>` |
| Reset overrides | `game reset-overrides --game <n>` |

## Overrides panel

| GUI | rhictl |
|---|---|
| Game name / Wiki mod name / Reset name | `game set-name`, `game set-wiki-name`, `game reset-wiki-name` |
| DLL name dropdowns / Reset DLL names | `game set-dll`, `game reset-dll`; `reshade --dll` at install |
| Shader mode + Choose components… | `reshade --shaders/--packs` covers ReShade only; `game reset-overrides` resets per-game mode |
| Select ReShade Preset | `preset deploy --game <n> --file a.ini [--with-shaders]` |
| Launch exe / Launch arguments / Reset | `game set-launch --exe/--args`, `game reset-launch` |
| ReShade channel | `reshade --channel` |
| Keep ReShade.ini Updated | `config get|set --key …` |

## Settings pages

The Settings panel was not opened during the dump (the UIA run captured the main window with a
game selected). Settings tabs visible in the source: General, Appearance, ReShade & RenoDX,
DLSS & Streamline, NVIDIA, Accounts & Data, Tools & Maintenance, About.
Shared settings are read and written by the CLI through `config list|get|set|unset` (atomic
`SettingsFile.Update`), matching the Settings pages key by key.

## Summary

- **Working now:** library detection/add, per-game component status, ReShade (incl. Vulkan layer),
  RenoDX, Luma, DLSS/Streamline, OptiScaler, DXVK, RE Framework, DOF Fix, ASI Loader, DLSS Enabler,
  20/30 FG, RTX Encore, MFG Unlocker, dgVoodoo2, ReLimiter, Display Commander, shaders, presets,
  `update all`, game launch, HDR/resolution/vibrance, NVIDIA presets/overrides/global settings,
  DLDSR, and an MCP server.
- **Remaining UI-only:** Ultra+ download (vendor site) and purely visual settings (theme, layout,
  section order).

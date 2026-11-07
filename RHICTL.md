# rhictl — headless CLI for RHI

This fork adds **`rhictl`**, a console front-end for RHI that reuses the app's own service layer
instead of reimplementing it. It can install and manage every component RHI supports — ReShade,
RenoDX, Luma, OptiScaler, DXVK, DLSS/Streamline, NVIDIA driver settings, limiters and the rest —
without opening the GUI.

Full documentation:

- **[RHI.Cli/README.md](RHI.Cli/README.md)** — build, commands, safety
- **[docs/EVERYTHING.md](docs/EVERYTHING.md)** — complete project reference
- **[docs/IN-GAME-MENU.md](docs/IN-GAME-MENU.md)** — in-game overlay reference
- **[docs/GUI-INVENTORY.md](docs/GUI-INVENTORY.md)** — GUI → CLI coverage matrix

## Quick start

```powershell
dotnet build RHI.Cli\RHI.Cli.csproj -c Release
.\RHI.Cli\bin\Release\net8.0-windows10.0.19041.0\rhictl.exe doctor
```

```powershell
rhictl library list
rhictl status --game "Your Game"
rhictl reshade install --game "Your Game" --channel stable --dry-run
rhictl reshade install --game "Your Game" --channel stable --yes
rhictl renodx install --game "Your Game" --yes
```

## What is added in this fork

| Path | What |
|---|---|
| `RHI.Core/` | headless compilation of `RenoDXCommander/Services` + `Models` (linked at build time) and the UI shims |
| `RHI.Cli/` | the `rhictl` console app, ~85 verbs across 21 command groups |
| `docs/EVERYTHING.md` | full project reference |
| `docs/IN-GAME-MENU.md`, `docs/IN-GAME-UI.md` | in-game overlay and settings research |
| `docs/GUI-INVENTORY.md`, `docs/GUI-INVENTORY-raw.txt` | GUI → CLI coverage matrix and raw accessibility dump |
| `docs/PROBE.md` | usage guide for the window probe |
| `docs/in-game-captures/` | window-only ReShade/RenoDX/ReLimiter captures |

The app project (`RenoDXCommander/`) is **not modified**. `git diff` against upstream on tracked
app files is empty.

## License

GPL-3.0, inherited from RHI. See `LICENSE`.

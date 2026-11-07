# probe.ps1 — universal window probe

A dependency-free PowerShell tool for an agent to **inspect, drive, and capture any Windows
window** — not tied to one game. It was written to explore ReShade's in-game overlay, but works on
Notepad, Chrome, a game, or a launcher.

Two things make it useful for agent work:

1. **No-focus input.** `postkey` / `postclick` / `postwheel` / `postdrag` send Win32 messages to
   the target window without bringing it to the foreground. ReShade accepts window messages, so
   an overlay can be driven while the user keeps working.
2. **Window capture, not desktop capture.** `shot` uses `PrintWindow(PW_RENDERFULLCONTENT)` on the
   target's client area. `CopyFromScreen` captures the desktop compositor, which for a fullscreen
   game returns whatever window is on top (Discord, terminal, …). That was the original bug.

## Requirements

- Windows 10/11
- PowerShell 5.1 or PowerShell 7 (`pwsh`)
- .NET `System.Drawing` / `System.Windows.Forms` (present on Windows PowerShell and pwsh on Windows)
- No admin required, **unless** the target window belongs to an elevated process (UIPI blocks
  messages from a non-elevated sender).

## Quick start

```powershell
# 1. see what is open
.\probe.ps1 -Action list

# 2. inspect one window
.\probe.ps1 -Action info -ProcessName notepad
.\probe.ps1 -Action info -WindowTitle "Task Manager"

# 3. capture it (window-only, no desktop grab)
.\probe.ps1 -Action shot -ProcessName notepad -Path .\notepad.png

# 4. capture downscaled for an LLM (fewer tokens)
.\probe.ps1 -Action shot -ProcessName notepad -Path .\notepad.png -MaxWidth 1280 -Json
```

## Window selection

Every action except `list` needs a target. **Exactly one** of these:

| Parameter | Behavior |
|---|---|
| `-ProcessName "notepad"` | first visible window of that process |
| `-ProcessName "..." -WindowTitle "regex"` | first visible window of the process whose title matches |
| `-WindowTitle "regex"` | first window in any process whose title matches |
| `-Hwnd 123456` | exact handle (decimal), overrides the others |
| _(none)_ | `-Action list` shows all top-level windows with pid/hwnd/title/client size |

`-Action list -Json` returns an array of `{Process,Pid,Hwnd,Title,Client}` — useful to pick a
handle programmatically.

## Actions

| Action | Parameters | Focus stolen? | What it does |
|---|---|---|---|
| `list` | — | no | enumerate visible top-level windows |
| `info` | target | no | pid, title, class, client size, window rect, DPI, foreground state |
| `focus` | target | **yes** | restore + `AttachThreadInput` + ALT trick + `SwitchToThisWindow`; reports whether it worked |
| `key` | target, `-Key` | **yes** | real `keybd_event` to the focused window (auto-focuses, refuses if focus fails) |
| `postkey` | target, `-Key` | no | `WM_KEYDOWN`/`WM_KEYUP` to the window |
| `move` | target, `-X -Y` | no | `WM_MOUSEMOVE` to client coordinates |
| `postclick` | target, `-X -Y` | no | move + `WM_LBUTTONDOWN`/`UP` at client coordinates |
| `click` | target, `-X -Y` | **yes** | real cursor move + click at client coordinates (converted to screen) |
| `postwheel` | target, `-X -Y -Value <n>` | no | wheel at client point; negative = down |
| `postdrag` | target, `-X -Y -X2 -Y2` | no | left-button drag between client points |
| `shot` | target, `-Path`, [`-MaxWidth`] | no | `PrintWindow` capture of the client area |
| `close` | target | no | posts `WM_CLOSE` |

`-DelayMs` (default 60) controls the gap between down/up or key down/up.

## Coordinates

All `-X/-Y` values are **client-area** coordinates (0,0 = top-left of the window's content),
not screen coordinates. `info` reports `client` size, so a tab at 20% width is `0.20 * clientWidth`.

`click` converts client → screen internally; `postclick`/`postwheel`/`postdrag` build the
`lParam` directly from client coordinates (wheel is converted to screen because `WM_MOUSEWHEEL`
expects screen coordinates).

## Key names

`Home`, `End`, `PageUp`, `PageDown`, `Insert`, `Delete`, `Escape`/`Esc`, `Enter`/`Return`, `Tab`,
`Space`, `Backspace`, `Up`, `Down`, `Left`, `Right`, `PrintScreen`/`PrtSc`, `Pause`, `CapsLock`,
`F1`–`F12`.

Or raw virtual-key hex: `-Key 0x24` (= Home), `-Key 0x2C` (= PrintScreen).

## Capture details

- `PrintWindow` flag `2` = `PW_RENDERFULLCONTENT` (captures DWM-composed content, including
  overlays drawn into the swapchain).
- Output format follows the extension: `.png` (default) or `.jpg`.
- `-MaxWidth N` resizes proportionally before saving (token saving for vision models).
- The result includes `printWindowOk` and `black`. If `black` is true, the window is likely
  exclusive-fullscreen or not composed; switch the app to borderless/windowed, or use the app's
  own screenshot key.
- The cursor is not drawn into the capture.

## JSON output

Every action supports `-Json` and prints a single JSON object (or array for `list`):

```json
{"black":false,"width":900,"action":"shot","height":506,"path":"C:\\tmp\\shot.png",
 "process":"WindowsTerminal","title":"…","hwnd":197378,"printWindowOk":true}
```

Use it to branch without parsing prose.

---

## Recipe: capture and drive a ReShade overlay (any game)

ReShade accepts window messages, so the whole loop can run without focus.

```powershell
$game = "Death in the Water 2"          # any process name
$out  = "C:\captures"

# 1. confirm the window and its client size
.\probe.ps1 -Action info -ProcessName $game

# 2. open the overlay (Home = VK 0x24)
.\probe.ps1 -Action postkey -ProcessName $game -Key Home
Start-Sleep -Seconds 2

# 3. capture the overlay (window-only)
.\probe.ps1 -Action shot -ProcessName $game -Path "$out\overlay.png" -MaxWidth 1600 -Json

# 4. click a tab. Coordinates are client pixels from the captured image:
#    e.g. RenoDX tab at x=636, y=21 on a 2560x1440 client
.\probe.ps1 -Action postclick -ProcessName $game -X 636 -Y 21
Start-Sleep -Seconds 1
.\probe.ps1 -Action shot -ProcessName $game -Path "$out\renodx.png" -MaxWidth 1600

# 5. scroll a long panel (negative = down)
.\probe.ps1 -Action postwheel -ProcessName $game -X 400 -Y 800 -Value -6

# 6. close the overlay, then the game gracefully
.\probe.ps1 -Action postkey -ProcessName $game -Key Home
.\probe.ps1 -Action close -ProcessName $game
```

**Finding coordinates:** capture once, read the image, measure the target in the image, then
multiply by `clientWidth / imageWidth` if you used `-MaxWidth`. Example: a 1600-wide downscale of a
2560-wide client has a factor of 1.6.

## Recipe: any ordinary app

```powershell
.\probe.ps1 -Action info -ProcessName notepad
.\probe.ps1 -Action shot -ProcessName notepad -Path .\notepad.png -MaxWidth 1200 -Json
.\probe.ps1 -Action postkey -ProcessName notepad -Key F5
```

## Recipe: enumerate and pick a window by title

```powershell
$targets = .\probe.ps1 -Action list -Json | ConvertFrom-Json |
           Where-Object { $_.Title -match "RenoDX|ReShade|Death in the Water" }
$targets | Format-Table

$h = $targets[0].Hwnd
.\probe.ps1 -Action shot -Hwnd $h -Path .\target.png -Json
```

## Limitations

| Limitation | Effect | Workaround |
|---|---|---|
| Exclusive fullscreen | `PrintWindow` may return black | switch the app to borderless/windowed, or use its own screenshot key |
| UIPI / elevated target | messages and captures fail from a non-elevated process | run the probe elevated (same integrity level) |
| Raw-input apps | `postkey`/`postclick` ignored because the app reads raw input | use `key`/`click` (they focus the window) |
| Some UWP / Chromium surfaces | `PrintWindow` returns black | try `-Action shot` on a child window, or use the app's screenshot |
| DPI scaling | coordinates are physical client pixels | use `info`'s `client` size and `dpi`; the script is DPI-aware |
| Cursor | not included in captures | n/a |
| No resize/move | the script does not change window geometry | use the app or Win32 directly |
| Anti-cheat games | input injection may be blocked or bannable | do not use on protected online games |

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `No visible window found for process 'X'` | process name wrong, or window is hidden | `-Action list`; use `-WindowTitle` or `-Hwnd` |
| `Could not bring the target window to the foreground` (`key`/`click`) | foreground lock | use `postkey`/`postclick`, or run `-Action focus` first |
| `Capture is black` | exclusive fullscreen / not composed | borderless, or app screenshot |
| `printWindowOk: false` | the window does not support `WM_PRINT` | try a child window or the app's screenshot |
| Coordinates land wrong | used screen instead of client coordinates, or image was downscaled | read `info.client`, multiply by the downscale factor |
| Nothing happens on `postclick` | app uses raw input or a custom hit-test | use `click` (focuses) or the real UI |
| Access denied / silence on an elevated app | UIPI | run the probe elevated |

## Safety

- Only target windows you own or are authorized to control.
- `key` and `click` **steal focus** and move the real cursor; prefer `postkey`/`postclick`.
- `close` posts `WM_CLOSE` (graceful); an app may show a save prompt.
- Do not drive anti-cheat-protected online games with synthetic input.
- The tool does not read memory, inject code, or modify files.

---

*End of probe guide.*


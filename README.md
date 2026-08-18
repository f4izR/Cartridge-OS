# Cartridge-OS
A PC Game launcher that detects games installed on the system with the ability of user to add there own directories to make the ease of launching games from couch

Turn your Windows gaming PC into a true console experience — controller-first, fullscreen, and deeply integrated with the OS, without giving up any of the flexibility Windows gives you.

Inspired by Steam Big Picture, SteamOS, Playnite, LaunchBox, and the Xbox/PlayStation dashboards — but built to go further with native OS-level integration.

See `features.md` for a full, current-state feature list (this section below is the pitch, not necessarily up to the minute).

---

## ✨ Core Features

### No Accounts, No Sign-In
Fully offline. No forced login, no cloud account, no sign-up wall. Plug in a controller and go.

### Detects Every Game You Own
Automatically scans and finds installed games — regardless of where they came from, including standalone executables and installs not registered with any launcher. If it's installed on your PC, Cartridge OS will find it.

### Multi-Launcher Support
Pulls your library from every major platform, including:
- Steam
- Epic Games Launcher
- Ubisoft Connect
- EA App
- GOG Galaxy
- Battle.net
- Riot Client
- Xbox PC App
- Emulators
- Standalone executables

### Runs Quietly in the Background
Launch it once and it stays running — starts automatically with Windows and lives in the background (tray + service), just like Steam Input, DS4Windows, or SCP Toolkit.

### Seamless In-Game Overlay
An Nvidia-overlay-style in-game overlay lets you exit back to the launcher without ever touching a keyboard or mouse — fully controller-navigable while open, and only ever appears while a game is actually running.

### Controller-First Navigation
The entire UI — from boot to launch to returning from a game — is designed to be fully navigable with a controller. No keyboard, no mouse required.

### Broad Controller Compatibility
Supports Xbox, PlayStation, and generic controllers via XInput, DirectInput, and HID, including devices like:
- Xbox controllers
- DualShock 3 / DualShock 4
- EasySMX D10
- Other DirectInput/HID gamepads

### Polished, Console-Grade UI
Fullscreen, hardware-accelerated, themeable interface with sound effects and a hidden mouse cursor for a true dashboard feel.

---

## 📖 User Guide

### Starting Cartridge OS

Run `CartridgeOS.Launcher.exe`. It's a single-instance app: the first launch starts a lightweight background core (tray icon + controller/hotkey listener) and opens the fullscreen launcher window; running the exe again while it's already open just brings that window back to the front instead of starting a second copy.

Closing the launcher window (the X button) doesn't quit Cartridge OS — it destroys the window to free its memory and keeps the tray icon running in the background. Reopen it from the tray icon (click or double-click), or by running the exe again.

The launcher window opens fullscreen, borderless, and always-on-top — that's intentional, it's built to feel like a console dashboard rather than a desktop app.

### Building your library

Three ways to add games, all in **Settings → Library** (gear icon, top-right):

- **Scan for Games** — checks Steam, Epic, Riot, GOG, Ubisoft Connect, EA App, and Battle.net for anything installed, and adds it automatically. Safe to click any time; it skips launchers you don't have and games already in your library. It also re-runs on its own every 15 minutes, so newly-installed games show up without you doing anything.
- **Find More Games** — looks for games *not* tied to any launcher: standalone/DRM-free installs and Xbox app/PC Game Pass/Microsoft Store titles. By default it sweeps Program Files across every drive; the **Scan Directory** picker above the button lets you point it at a specific folder instead (an NVIDIA-style combo box of your last 5 picks, plus Browse...). Because this is a best-effort guess rather than a certainty, it shows you a checklist first — everything starts **unchecked**, so tick only what's actually a game, use **Select All**/**Unselect All** if that's faster, then **Add Selected**.
- **Add Game** — pick an executable (and optionally artwork) yourself, for anything the scanners miss entirely.

### Screens

Three screens, switchable via the top-center nav pill (mouse click, or gamepad L1/R1):

- **Home** — the default screen. Selected game's own artwork fills the background; an infinite PS5-style carousel at the bottom lets you browse and launch.
- **Recently Played** — a "Continue Playing" card for the last game you played, a 2x2 grid of the next 4, and a System Overview panel (games installed, storage used, session uptime).
- **Library** — the full, searchable grid of everything in your collection.

### Navigating

| Action | Controller | Keyboard | Mouse |
|---|---|---|---|
| Move around a screen | D-pad / left stick | Arrow keys | Click a tile |
| Switch Home / Recently Played / Library | L1 / R1 | — | Click the nav pill |
| Launch the selected game | A | Enter / Space | Double-click / Play button |
| Add a game | Y | Insert | Settings → Add Game |
| Per-game menu (change wallpaper, revert, delete) | Menu/Start/Options | Apps key | Right-click a tile |
| Move the cursor | Right stick | — | — |
| Click | Right trigger | — | Left mouse button |
| Toggle overlay | Guide / PS button (while a game is running) | Ctrl+Shift+O | — |
| Open power menu | Start | F4 | Header power button (top-right) |

### The tray icon

Click or right-click it:
- **Open Cartridge OS** — brings back the launcher window (also works via left-click or double-click on the icon itself)
- **Exit Cartridge OS** — fully quits: removes the tray icon, stops the controller/hotkey listeners, and closes the launcher if it's open

### Known limitations (current build)

- The overlay won't render on top of true DirectX exclusive-fullscreen games (bypasses the desktop compositor entirely) — windowed/borderless games are fine.
- The scanners for GOG, Ubisoft Connect, EA App, Battle.net, and Xbox/Store are best-effort (no documented format to read, unlike Steam/Epic/Riot) — if one misses a game, use **+ Add Game**.
- Sound effects are placeholder tones, not final sound design.

See `production-readiness.md` for the full list of what's left before this is a finished product.

---

## 🧱 Tech Stack

| Layer | Technology |
|---|---|
| Language | C# |
| Framework | .NET 10 (LTS) |
| UI | WPF (see context.md for why, not WinUI 3/Avalonia as originally considered) |
| Database | SQLite |
| Architecture Pattern | MVVM |
| IPC | Named Pipes |
| IDE | Visual Studio 2022 Community |

---

## 🏗️ Architecture

Because a Windows service can't directly render a UI, Cartridge OS is split into cooperating components that talk over named pipes:

```text
Windows Boot
      │
      ▼
Windows Service
      │
      ├── Controller Manager
      ├── Input Mapper
      ├── Game Scanner
      ├── Configuration Manager
      └── IPC Server
                   │
                   ▼
Tray Application
                   │
                   ▼
Fullscreen Launcher UI
```

---

## 🛠️ Running & Testing

Requires the .NET 10 SDK and Windows, since WPF/XInput/named pipes are Windows-only.

```powershell
# Build everything
dotnet build

# Run Cartridge OS (single process: tray core + launcher window; single-instance, see context.md)
dotnet run --project src/CartridgeOS.Launcher

# Run the background service (hosts a named-pipe IPC server; scanning/controller ownership hasn't moved here yet — see context.md)
dotnet run --project src/CartridgeOS.Service

# Run the automated test suite (xUnit) — wraps every self-check below as a [Fact], see src/CartridgeOS.Tests
dotnet test
```

### Testing without a controller

The Launcher is controller-first, but every gamepad action has a keyboard equivalent for testing without hardware:

| Gamepad | Keyboard | Action |
|---|---|---|
| D-pad / left stick | Arrow keys | Navigate the game grid |
| A | Enter / Space | Launch the selected game |
| Y | Insert | Open the add-game flow (exe + artwork picker) |
| Right stick | — | Moves the real system mouse cursor (no keyboard equivalent — just use the mouse) |
| Right trigger | — | Left-click (no keyboard equivalent) |
| Guide / PS | — | Toggle the in-game overlay (Return to Cartridge OS / Quit Game) — controller-navigable once open, keyboard/mouse also work |
| — | Ctrl+Shift+O | Same as controller Guide/PS — toggles the overlay |
| Start | F4 | Opens the power menu: Exit to Desktop (closes the launcher window, tray keeps running), Shut Down Cartridge OS (full quit), Restart System, Turn Off System |

Mouse works normally (click tiles, click the header buttons).

### Self-checks

`dotnet test` (`src/CartridgeOS.Tests`) is now the primary way to run these — one xUnit `[Fact]` per check below, discoverable/runnable by any .NET test runner or CI, all green/red in one run instead of one exit code per manual invocation. The individual `--self-check-*` CLI flags below still work unchanged (same `Run()` methods, just invoked a different way) for a quick one-off check without pulling in the test SDK:

```powershell
# Verifies the artwork decode/cache pipeline (generates a temp image, checks decode width + cache hit/miss). Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-artwork

# Verifies the Steam VDF-parsing logic against sample library/manifest text (no real Steam install needed). Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-steam

# Verifies the Epic manifest-parsing logic against sample JSON (no real Epic install needed). Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-epic

# Verifies the Riot manifest-parsing logic against sample JSON (no real Riot install needed). Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-riot

# Verifies the exe-guessing heuristic (used for GOG/Ubisoft/EA/Battle.net) against a real temp directory. Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-executable-heuristics

# Verifies the standalone-executable scanner (blocks known launcher folders, finds the real game) against a real temp directory. Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-standalone

# Verifies both sound files exist and load without error (can't check anything audible headlessly). Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-sound

# Verifies the named-pipe transport (server + client, real round-trip) in-process, against a throwaway db. Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-ipc

# Cross-process diagnostic: pings whatever's actually listening on the pipe right now (e.g. a running Service). Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --ipc-ping

# Verifies right-stick deadzone math, then briefly moves and restores the real cursor to confirm the P/Invoke path works. Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-mouse-emulation

# Verifies Xbox/Store JSON parsing against sample data, then actually runs the real PowerShell script as a smoke test. Exits 0/1.
dotnet run --project src/CartridgeOS.Launcher -- --self-check-xbox
```

### Notes

- The Launcher runs fullscreen, borderless, and topmost — this is intentional (console-dashboard UX), not a bug. The cursor is currently visible (hiding it is deferred until there's real input-mode detection — show on mouse move, hide on gamepad input).
- "Scan for Games" (header button) checks Steam, Epic, Riot, GOG, Ubisoft Connect, EA App, and Battle.net, adding anything not already in the list. Each source no-ops safely if that launcher isn't installed. Steam/Epic/Riot read documented manifest files and are fairly reliable; GOG/Ubisoft/EA/Battle.net fall back to scanning the Windows uninstall registry by publisher and guessing the main exe, since those four don't expose a documented manifest — expect more misses/false picks there than the other three. Xbox/Microsoft Store games aren't detected yet (different install mechanism — UWP/MSIX packages, not traditional installs).
- "Find More Games" (header button) looks for games not registered with any launcher at all — standalone executables (folder + exe guessing) plus Xbox app/PC Game Pass/Microsoft Store games (via `Get-AppxPackage`). Both are pure heuristics with no reliable "this is actually a game" signal, so results go through a confirmation dialog before anything's added, unlike "Scan for Games". Has no concept of a game's origin or license status; it finds game-shaped things, nothing more. Not tested against a real Xbox/Store install — no such install available during development.
- The 7 trusted launcher scanners also re-run automatically every 15 minutes in the background (never the heuristic standalone scanner) — no action needed, new installs show up on their own.
- Navigating the grid and launching a game play short sound effects. These are placeholder procedurally-generated tones (no real sound design exists yet) — replace `Assets/Sounds/*.wav` with real SFX whenever they're available; nothing else needs to change.
- The named-pipe transport (`Core/Ipc/`) is used for two independent things: `--ipc-ping`/the standalone Service's `Ping`/`GetGameCount` commands, and (separately, its own pipe name) single-instance signaling — a second launch of the Launcher signals the running instance to show itself rather than starting a duplicate. Scanning/controller logic still runs in the Launcher, not the Service — the Service's pipe exists as working infrastructure, not a full architecture migration yet (see context.md if you're picking that up).
- First run seeds a handful of placeholder games (no real executable) into `%LocalAppData%\CartridgeOS\games.db` so the grid/recent-row aren't empty during development. Delete that file to reset to a clean state.
- See `progress.md` for what's actually built, `production-readiness.md` for what's left before shipping, and `context.md` for orientation if you're a fresh agent/contributor picking this up.

---

## 🗺️ Roadmap

### Version 1
- Fullscreen launcher
- Controller navigation
- Manual game launching
- Game artwork support
- Recent games list
- Hidden tray icon

### Version 2
- Automatic library detection across all launchers
- Controller-to-mouse emulation (right stick as cursor)
- Custom themes
- Sound effects
- Background scanning

### Version 3
- In-game overlay support
- Suspend and resume functionality
- Discord Rich Presence
- Cloud synchronization
- Plugin support

---

## 📌 Project Status

Version 1 and most of Version 2 are built and running (see the User Guide above and `progress.md` for detail): fullscreen launcher, controller/keyboard/mouse navigation, artwork, recent games, manual add, all 7 launcher scanners plus the heuristic standalone/Xbox scanners, background rescanning, sound effects, named-pipe IPC to the Service, and right-stick mouse emulation. Not yet production-ready — see `production-readiness.md` for what's left before shipping (packaging, installer, reliability hardening, testing on real hardware/installs).

---

## 🔒 License

This is a commercial, closed-source project. The repository is private and the source code is not open for redistribution.

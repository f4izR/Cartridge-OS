# Cartridge-OS
A PC Game launcher that detects games installed on the system with the ability of user to add there own directories to make the ease of launching games from couch

Turn your Windows gaming PC into a true console experience — controller-first, fullscreen, and deeply integrated with the OS, without giving up any of the flexibility Windows gives you.

Inspired by Steam Big Picture, SteamOS, Playnite, LaunchBox, and the Xbox/PlayStation dashboards — but built to go further with native OS-level integration.

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
An Nvidia-overlay-style in-game overlay lets you exit back to the launcher without ever touching a keyboard or mouse.

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

Run `CartridgeOS.Tray.exe` first — it sits in the system tray and is meant to stay running in the background. Right-click (or double-click) the tray icon to open the fullscreen launcher.

You can also run `CartridgeOS.Launcher.exe` directly if you just want the fullscreen UI without the tray icon.

The Launcher opens fullscreen, borderless, and always-on-top — that's intentional, it's built to feel like a console dashboard rather than a desktop app.

### Building your library

Three ways to add games, all in the header bar:

- **Scan for Games** — checks Steam, Epic, Riot, GOG, Ubisoft Connect, EA App, and Battle.net for anything installed, and adds it automatically. Safe to click any time; it skips launchers you don't have and games already in your library. It also re-runs on its own every 15 minutes, so newly-installed games show up without you doing anything.
- **Find More Games** — looks for games *not* tied to any launcher: standalone/DRM-free installs and Xbox app/PC Game Pass/Microsoft Store titles. Because this is a best-effort guess rather than a certainty, it shows you a checklist first — everything starts **unchecked**, so tick only what's actually a game, use **Select All**/**Unselect All** if that's faster, then **Add Selected**.
- **+ Add Game** — pick an executable (and optionally artwork) yourself, for anything the scanners miss entirely.

### Navigating

| Action | Controller | Keyboard | Mouse |
|---|---|---|---|
| Move around the grid | D-pad / left stick | Arrow keys | Click a tile |
| Launch the selected game | A | Enter / Space | Double-click |
| Add a game | Y | Insert | Click "+ Add Game" |
| Move the cursor | Right stick | — | — |
| Click | Right trigger | — | Left mouse button |
| Minimize / Exit | — (not bound yet) | Alt+F4 | Header buttons (top-right) |

The **Recent** row (above the main grid, only shown once you've launched something) is the fastest way back into whatever you played last — double-click or navigate to a tile there and press A/Enter, same as the main grid.

### The tray icon

Right-click it for:
- **Open Launcher** — brings up the fullscreen UI (also works via double-click)
- **Service Status** — reports whether the background service is running and how many games are in your library
- **Exit** — closes the tray icon (the Launcher, if open, keeps running independently)

### Known limitations (current build)

- Exiting/minimizing only works via mouse or Alt+F4 — no controller button does it yet.
- The scanners for GOG, Ubisoft Connect, EA App, Battle.net, and Xbox/Store are best-effort (no documented format to read, unlike Steam/Epic/Riot) — if one misses a game, use **+ Add Game**.
- Right-stick mouse emulation is currently limited to your primary monitor.
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

# Run the fullscreen launcher (the main UI)
dotnet run --project src/CartridgeOS.Launcher

# Run the system-tray app (icon + "Open Launcher"/"Exit" menu)
dotnet run --project src/CartridgeOS.Tray

# Run the background service (hosts a named-pipe IPC server; scanning/controller ownership hasn't moved here yet — see context.md)
dotnet run --project src/CartridgeOS.Service
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
| — | Alt+F4 | Exit (also has on-screen Minimize/Close buttons in the header, mouse-only — no gamepad binding yet, see `production-readiness.md`) |

Mouse works normally (click tiles, click the header buttons).

### Self-checks

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
- Tray's "Service Status" menu item talks to the Service over a real named pipe (`Core/Ipc/`) and reports whether it's running plus the current game count. Scanning/controller logic still runs in the Launcher, not the Service — the pipe exists as working infrastructure, not a full architecture migration yet (see context.md if you're picking that up).
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

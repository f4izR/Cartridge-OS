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

## 🧱 Tech Stack

| Layer | Technology |
|---|---|
| Language | C# |
| Framework | .NET 10 (LTS) |
| UI | WinUI 3 or Avalonia UI |
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

Currently in **design and planning phase**. Core architecture, tech stack, and scope for Version 1 are finalized; UI prototyping is in progress.

---

## 🔒 License

This is a commercial, closed-source project. The repository is private and the source code is not open for redistribution.

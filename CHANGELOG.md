# Changelog

All user-facing changes to Cartridge OS are tracked here, release by release. This is the
source for the release notes shown to users when an update is available (see
`Services/UpdateChecker.cs`).

Format loosely follows [Keep a Changelog](https://keepachangelog.com/): entries are grouped as
**Added** / **Changed** / **Fixed**, newest release first. Write entries for what a user would
notice, not internal implementation detail — that level of detail belongs in `progress.md`, not
here.

## How to keep this updated

- Add entries under **[Unreleased]** as user-facing changes land — don't batch it all up at
  release time, it's easy to forget what shipped three weeks ago.
- When cutting a release (bumping `<Version>` in `CartridgeOS.Launcher.csproj`), rename
  `[Unreleased]` to the new version + today's date, and start a fresh empty `[Unreleased]` above
  it.
- Skip pure dev/internal changes (refactors, self-checks, dependency bumps with no visible
  effect) — if a user wouldn't notice it, it doesn't belong here.

## [Unreleased]

### Added
- Launching a game now shows a fullscreen splash with that game's own artwork and a subtle zoom
  animation (PS5 / Steam Big Picture-style), covering the gap before the game's window appears,
  instead of dropping straight to the desktop. Cartridge OS still minimizes to tray as before
  once that's done.
- Power menu now shows an icon per action (Minimize, Exit to Desktop, Shut Down Cartridge OS),
  matching the Android/Pixel-style power menu look.

### Changed
- Power menu items are now color-coded by severity (neutral for Minimize, informational for
  Exit to Desktop, brand color for Shut Down Cartridge OS) and the keyboard/controller focus
  highlight now tints in that same color, so it's clearer what you're about to select while
  navigating.

## [1.2.0] - 2026-08-22

### Added
- Home screen: per-game visibility control (hide/show individual games from Home).
- App theming support and windowed-mode plumbing.

### Changed
- Various Settings UI adjustments.

### Fixed
- Assorted bug fixes alongside the settings UI changes.

## [1.1.0]

### Added
- Controller button prompts shown on-screen based on connected controller type.
- Rename game feature.
- On-screen keyboard for text entry without a physical keyboard.
- Keyboard hint keys shown when no controller is connected.

### Fixed
- Settings/Search gamepad focus bugs.
- Resume-game handling improvements.

## [1.0.2]

### Fixed
- DualShock 4 analog stick mapping.
- Cursor lock UX issues.
- CS2 minimize glitch.
- Tile sizing now responsive to window size.

## [1.0.1]

### Fixed
- Controller D-Pad and cursor navigation across Settings, Power menu, in-game overlay, and game
  options screens.

## [1.0.0] - Beta

Initial public beta.

### Added
- Fullscreen launcher with controller, keyboard, and mouse navigation.
- Automatic game detection for Steam, Epic, GOG, Ubisoft Connect, EA App, Battle.net, Riot, and
  Xbox/Microsoft Store, plus manual "Add Game" and a heuristic scan for standalone executables.
- Recently Played row and PS5-style carousel Home screen.
- In-game overlay (toggle via controller Start or a hotkey) with Return to Cartridge OS / Quit
  Game.
- Discord Rich Presence integration.
- System tray integration with single-instance launching.
- Background game rescanning.
- Online artwork lookup (SteamGridDB / TheGamesDB) for games without local box art.

# Features

What Cartridge OS actually does today, as a clean snapshot rather than a changelog. For *how* each of these was built, dig into `progress.md` (log) and `context.md` (technical detail/gotchas). For what's left before shipping, see `production-readiness.md`. Update this whenever a feature is added, reworked, or removed — keep it in sync with the real UI, not with what was originally planned.

## Screens & navigation

- **Boot splash** — Steam-style animated splash shown at startup, before the launcher window: the logo mark traces itself in (outer shell, then inner diamond), glow builds, a solid fill lands on top, then the wordmark slides/fades in, with a whoosh sound timed to the trace start. Fixed ~2.6s duration, then swaps to the launcher.
- **Home** — PS5-dashboard-style default screen. Selected game's artwork fills the background (top and bottom gradient fades keep the header and title/carousel text legible over it), an infinite horizontal carousel sits at the bottom with the selected tile enlarged and centered — tiles genuinely slide and grow/shrink as you move, not just swap content in place — game title/last-played/playtime/Play button above it.
- **Recently Played** — "Continue Playing" hero card (most recent game, highlights with a blue border + gradient when selected) + a 2x2 grid of the next 4 recently-played games + a System Overview panel (games installed, storage used — labeled with which drive, e.g. "Storage Used (D:)" — session uptime). Ambient drifting-particle background layer behind the content. Selection highlight is consistent between the hero and the grid — selecting one always clears the other.
- **Library** — full virtualized grid of every game in the collection, filterable via the header search box (search only appears on this screen, since it's the only one it actually filters). Double-click a tile to launch, same as every other screen. Same ambient drifting-particle background as Recently Played.
- **Settings** — collapsible right-side panel (gear icon), Steam/NVIDIA-app-style category rail:
  - **Library tab**: Scan for Games, Find More Games (+ scan-directory picker, see below), Add Game, Remove Game, and a storage-drive picker (which fixed drive the Recently Played storage stat reads from — defaults to the system drive).
  - **Wallpaper tab**: app-wide background source (selected game's artwork vs. a custom image) + file picker.
  - **Sound tab**: independent toggles for navigation sound and confirm sound.
- Top nav bar (Home / Recently Played / Library) is a centered pill switcher — mouse click, keyboard, or gamepad L1/R1 (shoulder buttons) all work; switching crossfades rather than cutting instantly. Settings' own Library/Wallpaper/Screen Saver tabs fade in on switch the same way.
- Header bar: logo, nav pills, and a status area (controller/device battery, real internet-reachability indicator, live search — Library only, clock, settings button, and a power button opening the power menu).
- A game's artwork zooms in briefly the moment it's launched, on every screen that can launch one (Library, Home carousel, Recently Played's hero card and 2x2 grid).

## Building your library

- **Scan for Games** — trusted, auto-add scanners for Steam, Epic Games, GOG Galaxy, Ubisoft Connect, EA App, Battle.net, and Riot Client. Steam/Epic/Riot read documented manifests; the other four use a Windows-uninstall-registry + publisher-filter heuristic. Re-runs automatically every 15 minutes in the background.
- **Find More Games** — broader heuristic scan for standalone/DRM-free executables and Xbox app/PC Game Pass/Microsoft Store titles. Results require confirmation (checklist dialog, unchecked by default) before anything's added, since this scan has no reliable "this is actually a game" signal. The results window has its own copy of the scan-directory picker (see below) — changing it there re-scans immediately and replaces the list with just that folder's results, no need to close the window and go back to Settings.
- **Scan whole drive** (optional, slower) — a checkbox next to the directory picker (both in Settings and the Find More Games results window) switches from a shallow one-level scan to a full recursive walk of the picked folder's subtree, so games installed a few folders deep are still found (e.g. Riot's `Riot Games\VALORANT\live\VALORANT.exe`). Correctly names the game after its real install folder even when the exe itself sits inside an uninformative wrapper folder (`live`, `bin`, `Win64`, etc.) a few levels down.
- **Scan-directory picker** (NVIDIA-style) — a combo box of the last 5 directories you've pointed "Find More Games" at, plus a Browse... button to add a new one, in both Settings → Library and the Find More Games results window (same underlying list — picking a folder in either place updates both). Picking a directory scans only that folder instead of the default Program Files/all-fixed-drives sweep. Seeded with `Program Files`/`Program Files (x86)` on first run so the list isn't empty before you've ever browsed.
- **Add Game** — manually pick an executable (and optionally artwork) for anything the scanners miss.
- **Remove Game** — deletes the selected game from the library (confirmation prompt), doesn't touch the actual install.

## Artwork

- Local artwork decode-and-cache pipeline (fixed pixel width, disk-cached, never full-res per tile).
- Online fallback for games with no local art source: SteamGridDB first, TheGamesDB as a rate-limit-aware fallback — downloaded once, cached permanently.
- Per-game **Change Wallpaper** (right-click a tile, or gamepad Menu/Start/Options) — pick a local image; if its aspect ratio doesn't match the tile shape, a crop/pan/zoom dialog (mouse or gamepad-drivable) lets you choose what survives the crop.
- **Revert to Previous Artwork** — single-level undo for the last artwork change, per game, per session.
- App-wide custom wallpaper (Settings → Wallpaper), independent from per-game artwork.

## Controller & input

- Xbox controllers via XInput, PlayStation (DualShock/DualSense) and other HID/DirectInput pads via `RawGameController` — both normalized into the same internal action set (navigate, confirm, back, secondary, menu, toggle settings, toggle search, previous/next tab) so the UI never sees which physical button did what. Full binding table, per-brand glyphs, and keyboard equivalents live in `keybinds.md`.
- Controller-first: every screen, Settings, and Library search are all reachable without touching a mouse — B/Circle backs out of whatever's open (Settings, then Search), the View/Share button opens Settings directly, X/Square opens search.
- Per-brand on-screen button glyphs (Xbox vs. PlayStation labels) where used, e.g. the overlay's toggle hint.
- Full keyboard equivalents for every gamepad action — the app is fully usable with no controller attached.
- Right-stick-as-mouse emulation (moves the real system cursor, right trigger clicks) — primary monitor only.
- Controller hot-plug/reconnect handling, held-direction repeat, analog deadzones.
- Controller (or, if none reports one, this machine's own) battery percentage shown in the header.
- Modal dialogs (e.g. the artwork crop screen) can take exclusive gamepad input while open, so background nav can't fire underneath them.

## In-game

- **In-game overlay** — Ctrl+Shift+O or gamepad Guide/PS button, shows the running game's title with Return-to-launcher / Quit-game buttons. Only ever toggles while a tracked game process is actually running. Fully controller-navigable while open (registers as a modal gamepad target: D-Pad Up/Down moves focus, Confirm activates, Back/Guide-PS closes) — not mouse-only. Re-asserts `Topmost` on open so it reliably appears above a windowed/borderless game without alt-tabbing first (true DirectX exclusive fullscreen is the one case this can't cover — bypasses the desktop compositor). Works for directly-launched executables only (Steam/Xbox shell launches have no trackable process).
- **Power menu** — Start button, F4, or the header's power button: Exit to Desktop / Shut Down Cartridge OS / Restart System / Turn Off System, replacing the old bare minimize/close title-bar buttons. Fully controller-navigable (D-Pad or arrow keys move focus, Confirm/Enter activates, Back/Escape closes), on-screen glyphs match the connected controller brand.
- **Discord Rich Presence** — shows "Playing {title}" while a game is running, including Steam/Xbox launches (just doesn't auto-clear on exit for those, since there's no exit signal to hook).
- **"Launching..." tile indicator** — covers a tile the moment its launch is requested, so repeated clicks on a slow (especially shell-protocol) launch don't queue multiple attempts.
- Launcher window minimizes automatically on every launch so it doesn't sit on top of the game.

## Idle screen saver

- Fullscreen ambient slideshow + background music after a configurable period of no keyboard/mouse/gamepad activity — a real crossfade between shuffled photos (not a hard cut), music fades in/out through a shuffled looping playlist, and a big centered clock + date. Suppressed while a game is running.
- Any input dismisses it — keyboard, mouse (past a small movement threshold), or gamepad.
- Settings → Screen Saver: enable/disable, inactivity duration (preset dropdown), volume, and folder overrides for images/music (replaces the bundled defaults entirely when set, rather than mixing in) — plus a "Preview Now" button to test immediately.

## System integration

- Single-instance app — a second launch just brings the existing window to the front instead of starting a duplicate.
- Disposable launcher UI + persistent tray core — closing the window frees its memory (destroyed, not hidden) while the tray icon, controller listener, hotkey, and Discord connection keep running; reopens from the tray icon or by running the exe again.
- Tray icon: Open Cartridge OS / Exit Cartridge OS.
- Named-pipe IPC transport (`Core/Ipc/`) — used for single-instance signaling today; a separate `CartridgeOS.Service` process hosts a basic Ping/GetGameCount endpoint over the same mechanism, not yet a full architecture migration.
- Sound effects on navigation and game launch (placeholder procedurally-generated tones, not final sound design) — independently toggleable in Settings → Sound.
- Local, offline storage only (SQLite for games, a small JSON file for settings) — no accounts, no cloud.
- Internet connectivity indicator does a real reachability probe (Windows' own connectivity-check endpoint) every few seconds, not just a network-adapter-up check — so it correctly flips to offline even if the adapter itself stays connected but there's no actual internet upstream.

## Not yet built

Tracked as open checklist items rather than duplicated here — see `progress.md`'s "Phase checklist" section for the current list (custom themes, suspend/resume — deliberately skipped, cloud sync — deliberately skipped, plugin support — deliberately skipped, packaging/installer and the rest of `production-readiness.md`).

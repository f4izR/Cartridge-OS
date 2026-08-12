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
- Header bar: logo, nav pills, and a status area (controller/device battery, real internet-reachability indicator, live search — Library only, clock, settings/minimize/close buttons).
- A game's artwork zooms in briefly the moment it's launched, on every screen that can launch one (Library, Home carousel, Recently Played's hero card and 2x2 grid).

## Building your library

- **Scan for Games** — trusted, auto-add scanners for Steam, Epic Games, GOG Galaxy, Ubisoft Connect, EA App, Battle.net, and Riot Client. Steam/Epic/Riot read documented manifests; the other four use a Windows-uninstall-registry + publisher-filter heuristic. Re-runs automatically every 15 minutes in the background.
- **Find More Games** — broader heuristic scan for standalone/DRM-free executables and Xbox app/PC Game Pass/Microsoft Store titles. Results require confirmation (checklist dialog, unchecked by default) before anything's added, since this scan has no reliable "this is actually a game" signal.
- **Scan-directory picker** (NVIDIA-style) — a combo box of the last 5 directories you've pointed "Find More Games" at, plus a Browse... button to add a new one. Picking a directory scans only that folder instead of the default Program Files/all-fixed-drives sweep. Seeded with `Program Files`/`Program Files (x86)` on first run so the list isn't empty before you've ever browsed.
- **Add Game** — manually pick an executable (and optionally artwork) for anything the scanners miss.
- **Remove Game** — deletes the selected game from the library (confirmation prompt), doesn't touch the actual install.

## Artwork

- Local artwork decode-and-cache pipeline (fixed pixel width, disk-cached, never full-res per tile).
- Online fallback for games with no local art source: SteamGridDB first, TheGamesDB as a rate-limit-aware fallback — downloaded once, cached permanently.
- Per-game **Change Wallpaper** (right-click a tile, or gamepad Menu/Start/Options) — pick a local image; if its aspect ratio doesn't match the tile shape, a crop/pan/zoom dialog (mouse or gamepad-drivable) lets you choose what survives the crop.
- **Revert to Previous Artwork** — single-level undo for the last artwork change, per game, per session.
- App-wide custom wallpaper (Settings → Wallpaper), independent from per-game artwork.

## Controller & input

- Xbox controllers via XInput, PlayStation (DualShock/DualSense) and other HID/DirectInput pads via `RawGameController` — both normalized into the same internal action set (navigate up/down/left/right, confirm, back, secondary, menu) so the UI never sees which physical button did what.
- Per-brand on-screen button glyphs (Xbox vs. PlayStation labels) where used, e.g. the overlay's toggle hint.
- Full keyboard equivalents for every gamepad action — the app is fully usable with no controller attached.
- Right-stick-as-mouse emulation (moves the real system cursor, right trigger clicks) — primary monitor only.
- Controller hot-plug/reconnect handling, held-direction repeat, analog deadzones.
- Controller (or, if none reports one, this machine's own) battery percentage shown in the header.
- Modal dialogs (e.g. the artwork crop screen) can take exclusive gamepad input while open, so background nav can't fire underneath them.

## In-game

- **In-game overlay** — Ctrl+Shift+O or gamepad Guide/PS button, shows the running game's title with Return-to-launcher / Quit-game buttons. Works for directly-launched executables only (Steam/Xbox shell launches have no trackable process).
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

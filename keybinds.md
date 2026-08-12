# Keybinds

Living log of every controller (and keyboard-equivalent) binding in Cartridge OS. Update this
whenever a `GamepadAction`/`GamepadButton` mapping changes — this is the source of truth for "what
does this button do," not `context.md` (which explains *why*/*how* it was built).

Physical buttons are read via `Input/GamepadWatcher.cs`'s `ActionMap` (`GamepadButton →
GamepadAction`) so the rest of the app only ever deals with the `GamepadAction` vocabulary, never
raw buttons — see `context.md`'s input-normalization section. `Input/ControllerGlyphs.cs` maps
`GamepadAction` to the right on-screen label per controller brand (Xbox vs PlayStation vs generic
fallback), used by `OverlayViewModel.MenuButtonLabel` today.

**Naming clash to watch for**: `GamepadButton.Back` is the *physical* Xbox View / PlayStation
Share button (XInput's legacy name for it — it used to be called "Back" pre–Xbox One). This is
**not** the same as `GamepadAction.Back`, which is the *action* produced by the B/Circle face
button. Don't confuse the two when reading `GamepadWatcher.ActionMap`.

## Global bindings (work on every screen)

| Button | Xbox | PlayStation | Action | Does | Modeled after |
|---|---|---|---|---|---|
| D-Pad / Left Stick | D-Pad / Left Stick | D-Pad / Left Stick | Navigate Up/Down/Left/Right | Move the selection | Every console dashboard (Xbox, PS5, Steam Deck) |
| A | A | ✕ Cross | Confirm | Launch the selected game | Universal console convention — bottom face button always confirms |
| B | B | ○ Circle | Back | Closes whatever's open on top (Settings, then Search) — never navigates screens | Universal console convention — right face button always backs out/cancels |
| Y | Y | △ Triangle | Secondary | Add Game (opens the file picker) | Xbox dashboard uses Y for a screen's main secondary action |
| X | X | □ Square | Toggle Search | Opens/closes the Library search box — no-ops outside Library | Common "search/filter" slot on the left face button in dashboard-style UIs |
| Guide / PS button | Guide | PS | Menu | Toggles the in-game overlay while a game is running; opens the selected tile's context menu (Change Wallpaper / Delete Game) otherwise | Xbox Guide button opens the Xbox overlay; PS button opens the PS5 control center — same "the one button that always does something contextual" role |
| View / Share | View | Share | Toggle Settings | Opens/closes the Settings sidebar | Xbox dashboard's View button is the standard "more options" button; PS5's equivalent is Share/Create in some apps |
| Left Shoulder (LB/L1) | LB | L1 | Previous Tab | Home → Recently Played → Library, cycling backward | Universal — shoulder buttons page between tabs in nearly every console UI (Xbox dashboard, PS5 settings, Steam Big Picture) |
| Right Shoulder (RB/R1) | RB | R1 | Next Tab | Cycles forward through the three screens | Same as above |
| Right Trigger | RT | R2 | *(mouse click)* | Right-stick mouse emulation's click button — not a `GamepadAction`, handled separately (`MouseEmulator`) | Matches "trigger = click" in any controller-as-mouse scheme |
| Right Stick | Right Stick | Right Stick | *(mouse move)* | Moves the real system cursor, primary monitor only | Same as most "controller as mouse" implementations (Steam Big Picture desktop mode, etc.) |

**Not yet bound to anything**: nothing — every `GamepadButton` XInput exposes now has a meaning
except the raw analog trigger values themselves (only used as the mouse-click threshold above,
not as their own actions) and the two thumbstick clicks (L3/R3), which have no mapped use yet.

## Screen-specific behavior

Everything below is still driven by the *same* global bindings table above — this section is
just what Navigate/Confirm/Secondary *do* differently depending on which screen is active
(`MainWindow.HandleGamepadAction`).

- **Home**: Left/Right wraps around the carousel (past the last tile brings you back to the
  first). Up/Down do nothing here — it's a single row.
- **Library**: full 2D grid nav (column count derived from the grid's actual rendered width).
  Confirm launches; Secondary (Y) opens Add Game; X opens/closes search.
- **Recently Played**: a fixed 3-row layout — row 0 is the hero card (spans both columns), rows
  1–2 are the 2×2 "other recent games" grid. Nav moves through that shape, not a flat list.
- **Settings open**: Back (B) closes it. Nothing else changes — you can still Confirm/navigate
  underneath, matching how a lightweight sidebar (not a full modal) behaves in most apps.

## Modal dialogs (take over gamepad input exclusively)

While one of these is open, `App.SetModalGamepadTarget` routes every `GamepadAction` (and the
right stick) to it instead of the launcher window underneath — see `context.md`'s "Modal-dialog
gamepad routing" section for the mechanism.

| Dialog | Button | Action |
|---|---|---|
| Artwork crop (`ArtworkCropWindow`) | A / Confirm | Use This Crop |
| | B / Back | Cancel |
| | D-Pad Up / Down | Zoom in / out (repeat-while-held, same repeat timer as nav) |
| | Right Stick | Pan the image |
| | Right Trigger | *(suppressed)* — no stray mouse click lands on whatever's underneath |
| Screen saver | Any button | Dismiss |

**Known gap**: the in-game overlay (`OverlayWindow`, Return to Cartridge OS / Quit Game) is
**mouse-only** — it doesn't register as a modal gamepad target, so there's no controller way to
click its buttons today. Toggling the overlay itself (Guide/PS button) works; using it once open
doesn't. Worth fixing if a controller-only user actually hits this in practice.

## Keyboard equivalents

Every `GamepadAction` should be reachable without a controller — not just for keyboard/mouse
users, but because it's how this app's controller-facing behavior gets smoke-tested without
physical hardware (see `context.md`'s "no real UI automation here" limitation).

| Key | Action |
|---|---|
| Arrow keys | Navigate Up/Down/Left/Right |
| Enter / Space | Confirm |
| Insert | Secondary (Add Game) |
| Apps (context-menu key) | Menu |
| Escape | Back |
| Tab | Toggle Settings |

**Deliberately no keyboard shortcut for Toggle Search** — the search box is opened by clicking its
icon (mouse) or the X/Square button (gamepad) only. A keyboard shortcut here would need to avoid
every character a user might actually want to type while the search box has focus (arrow keys
already have this exact problem — `PreviewKeyDown` is window-level and tunnels down to a focused
`TextBox` before it can type into it — a pre-existing gap this session didn't introduce or fix),
so letters/punctuation are off the table without a focus-aware guard that doesn't exist yet.

## Log

| Date | What changed |
|---|---|
| 2026-08-12 | Overlay toggle moved from Start/Options to the Xbox Guide button / PS button (undocumented `XInputGetStateEx`, since the public XInput API masks the guide bit out) — see `context.md`. |
| 2026-08-12 | First real "controller-first" pass: wired up three previously-dead/missing bindings — B (`GamepadAction.Back`) now actually closes Settings/Search instead of doing nothing outside modal dialogs; added `ToggleSettings` (View/Share button) and `ToggleSearch` (X/Square) as new `GamepadAction`s so Settings and Search are reachable by controller at all (previously mouse-only). Fixed `ControllerGlyphs`' stale "Start" label for `GamepadAction.Menu` (leftover from before the Guide-button remap) to "Guide"/"PS". Added `Key.Escape`→Back and `Key.Tab`→ToggleSettings keyboard equivalents. This file created to track all of it going forward. |

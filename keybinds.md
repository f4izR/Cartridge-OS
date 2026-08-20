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
| Menu (☰, was "Start") | Menu | Options | Menu | Opens the selected tile's context menu (Change Wallpaper / Delete Game) | Real Xbox Home: selecting a game and pressing Menu shows its options (Manage game, Uninstall, Pin). PS5: Options does the same job on a selected item. This is "the button that opens a game's options," matching what a user coming from either console expects. |
| View / Share | View | Share | Toggle Settings | Opens/closes the Settings sidebar | Xbox dashboard's View button is the standard "more options" button; PS5's equivalent is Share/Create in some apps |
| Left Shoulder (LB/L1) | LB | L1 | Previous Tab | Home → Recently Played → Library, cycling backward | Universal — shoulder buttons page between tabs in nearly every console UI (Xbox dashboard, PS5 settings, Steam Big Picture) |
| Right Shoulder (RB/R1) | RB | R1 | Next Tab | Cycles forward through the three screens | Same as above |
| Guide / PS button (round, center) | Guide | PS | Power | Toggles the in-game overlay while a game is running; opens the Power menu (Turn Off System / Restart System / Exit to Desktop / Shut Down Cartridge OS) otherwise | Xbox Guide button and the PS button are always "the system-level button" on their respective consoles — matches that role here too. **Known caveat**: Windows' own Xbox Game Bar grabs this exact physical button globally by default (Settings → Gaming → Xbox Game Bar → "Open Xbox Game Bar using this button on a controller"), which this app cannot suppress — a press can open Windows' own overlay instead of (or on top of) this app's. Because of that, this button is deliberately *not* the only path to anything essential (see Menu above for item options; Power is also reachable via the header's Power button/mouse). If Guide/PS presses feel unreliable, disable that Windows setting. |
| Right Trigger | RT | R2 | *(mouse click)* | Right-stick mouse emulation's click button — not a `GamepadAction`, handled separately (`MouseEmulator`) | Matches "trigger = click" in any controller-as-mouse scheme |
| Right Stick | Right Stick | Right Stick | *(mouse move)* | Moves the real system cursor, works across every monitor (2026-08-18: was primary-monitor-only, now clamped to the full virtual desktop) | Same as most "controller as mouse" implementations (Steam Big Picture desktop mode, etc.) |
| Left Trigger + Right Trigger (held together) | LT+RT | L2+R2 | `ToggleCursorLock` | Freezes/unfreezes the right-stick-driven mouse cursor. Added 2026-08-19 for worn/drifting sticks — lock the cursor in place and drive everything with D-Pad instead. Edge-triggered once per combo (`GamepadWatcher.Poll`), and suppresses that press from also registering as a Right-Trigger click. Plays the Confirm sound as the only feedback (no on-screen indicator yet). | Common "click-in both sticks/triggers to toggle a mode" pattern in controller-as-mouse tools |

**Not yet bound to anything**: the two thumbstick clicks (L3/R3).

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
- **Settings/Search open**: 2026-08-19 — D-Pad/left stick now moves WPF focus around the panel
  (`MainWindow.HandleGamepadAction`'s top-of-method guard), Confirm activates the focused button,
  Back closes it. Previously this guard blocked everything except Back/ToggleSettings/ToggleSearch
  with no fallback at all, so D-Pad nav was silently dead while either panel was open — fixed
  together with seeding initial focus onto the tab strip when Settings opens (`SettingsPanel`'s
  `RootBorder_IsVisibleChanged`), since nothing had focus for MoveFocus to start from before that.
- **Game context menu** (Menu button, `OpenGameContextMenu`): 2026-08-19 — now anchored to the
  selected tile (`Placement=Center` + `PlacementTarget`) instead of `ContextMenu`'s `MousePoint`
  default, which opened it wherever the emulated cursor last sat. D-Pad Up/Down moves through the
  menu items (seeded focus on open), Confirm activates the focused item, Back/Menu closes it —
  routed the same way as the Settings/Search guard above.

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
| In-game overlay (`OverlayWindow`) | A / Confirm | Activates the focused button (Return to Cartridge OS / Quit Game) |
| | B / Back, Guide/PS / Power | Closes the overlay |
| | D-Pad Up / Down | Move focus between the two buttons |
| Power menu (`PowerMenuWindow`) | Confirm (A / ✕ / 1) or Enter/Space | Activates the focused option — glyph shown on screen matches the connected controller brand via `ControllerGlyphs`, same as the overlay's `MenuButtonLabel` |
| | Back (B / ○ / 2), Start / Power, or Escape | Closes the menu |
| | D-Pad Up / Down or Arrow Up / Down | Move focus between the four options — order top-to-bottom is Exit to Desktop, Shut Down Cartridge OS, Restart System, Turn Off System (harmless option first/focused by default, the two real PC power operations pushed to the end so they're never the accidental default) |
| Screen saver | Any button | Dismiss |

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
| F4 | Power (opens/closes the power menu) |

**Deliberately no keyboard shortcut for Toggle Search** — the search box is opened by clicking its
icon (mouse) or the X/Square button (gamepad) only.

**2026-08-18**: the focus-aware guard mentioned above now exists — `MainWindow`'s `PreviewKeyDown`
skips entirely when `Keyboard.FocusedElement is TextBox`, so arrow keys/Enter/Escape reach a focused
search box or Settings API-key field normally instead of being swallowed as nav actions first. Typing
letters/punctuation while the search box has focus already worked (only the nav keys were the
problem); this was found and fixed via a live keyboard-only test, not just code inspection.

## Log

| Date | What changed |
|---|---|
| 2026-08-20 | Fourth report, from live DualShock 4 testing: (1) `RawGameControllerSource`'s guessed axis order (`LX,LY,RX,RY,L2,R2`) was wrong for this pad — it actually reports `LX,LY,RX,L2,R2,RY`. Reading axis 3 as RY meant every poll fed the L2 trigger's rest-at-0 value through the inverted-stick formula, which pins to full deflection — that's why the cursor drifted straight up the instant a DS4 connected, with no stick touched at all. And axis 5 (the real RY) was being read as R2, so pulling the right stick down registered as a right-click instead of cursor movement. Swapped the mapping to match. (2) Replaced the floating always-on-top `CursorLockIndicatorWindow` (rendered over games/other apps, and the LT+RT lock only seemed to "take" while it happened to be the topmost window) with in-window state: `MainViewModel.IsCursorLocked`/`OverlayViewModel.IsCursorLocked` drive a small header pill (`MainWindow`) and overlay footer line instead — no separate window at all now. (3) `App.OnRightStickMoved` now no-ops entirely unless one of our own windows is the OS foreground window (`MainWindow.IsForegroundWindowInThisProcess`, reused from the existing Deactivated-minimize guard) — the stick-driven cursor no longer moves at all while alt-tabbed away or while a game has real focus, independent of the lock toggle. (4) The CS2 "minimizes then instantly re-maximizes" report: `LaunchGame`'s `process.Exited` handler already had a same-exe-name heuristic to avoid treating a stub-then-relaunch as a real exit, but it only checked once, immediately — if the stub exits before its replacement process has actually started, that check sees zero matching processes and fires `OnGameExited` -> `ShowLauncher()` prematurely, which is the flash of the launcher reappearing before CS2's real window retakes focus. Added a second recheck after a 2s delay (`HandleGameProcessExitedAsync`) before accepting the exit as real. **Axis-swap and CS2 recheck are build-verified only, not yet hands-on retested by the reporting user.** |
| 2026-08-19 (7) | Sixth follow-up: overlay D-Pad reported not working. `OverlayWindow` uses the identical modal-target + `Keyboard.FocusedElement`-based `MoveFocus`/`Command.Execute` pattern as `PowerMenuWindow` (confirmed working after the `CursorLockIndicatorWindow` focus-steal fix), so the most likely cause here is the same class of bug but from a different source: a running **fullscreen game actively fighting for OS foreground focus** — `Focus()` alone only sets WPF's logical keyboard focus, not real OS-level focus, so if the game re-asserts itself (common with `SetForegroundWindow` calls or exclusive-fullscreen modes), `Keyboard.FocusedElement` can end up stale/empty even though the overlay is visibly on top. Added `Activate()` before `Focus()` in `OverlayWindow`'s `Loaded` handler to actively contest that, plus `[Overlay]`-tagged debug logging on both `Loaded` and every `HandleAction` call (focused element + `IsActive`) to confirm or rule this out. Also gave `QuitButton` the same `PrimaryButtonStyle` + focus-ring fix `PowerMenuWindow`'s red buttons got two entries ago — it had no style at all, so even if focus did move there it wouldn't have been visible. **Needs a real running game to retest against** — could not be verified without one. |
| 2026-08-19 (6) | Fifth follow-up: `AdjustSlider`/`AdjustComboBox` (added in the previous entry) always handled Up/Down or Left/Right whenever a ComboBox/Slider had focus, even once already at the first/last item — trapping the D-Pad there permanently with no way to move on. Both now return whether they actually changed anything; the Settings guard falls back to `MoveFocusFrom` when they didn't. Added a "Controls" tab to Settings (`SettingsPanel.xaml`) — a static, player-facing trim of this file's Global-bindings table plus a one-paragraph "how to test the overlay" note (launch a game, Guide/PS or Ctrl+Shift+O to bring it up). Kept in sync by hand; update both if a binding changes. |
| 2026-08-19 (5) | Fourth follow-up, with a log showing D-Pad/Confirm dispatched correctly to `PowerMenuWindow` as the modal target but doing nothing once there: `CursorLockIndicatorWindow` was stealing OS-level foreground focus when shown, even with `ShowActivated="False"` (that only suppresses WPF's own Activated/initial-focus bookkeeping, not Windows handing it real focus) — since WPF's `Keyboard.FocusedElement` is one shared value across every window on the same UI thread, this silently redirected it away from whatever Power-menu button actually had it, breaking that window's `MoveFocus`/`Command.Execute` calls entirely without erroring anywhere. Fixed with the real mechanism for "this window can never take focus" — `WS_EX_NOACTIVATE`, applied via `SetWindowLong` on `SourceInitialized` (P/Invoke, no existing helper for this in the codebase). |
| 2026-08-19 (4) | Third follow-up, with a live Debug Output log from the user: the synthetic-KeyDown approach from the previous entry never actually moved focus at all (log showed `focused=TabItem Header:Library` unchanged across every D-Pad direction) — root cause: `KeyEventArgs.KeyStates` is derived from the *real* physical keyboard device's current state, not from anything the constructor takes, so WPF's own directional-navigation and control keyboard handling saw a key that (per the real keyboard) wasn't actually down, and ignored it entirely. Replaced with direct API calls per control type instead of synthetic input: `MoveFocusFrom` (plain `UIElement.MoveFocus`, proven reliable — this is what already made Power menu/context menu D-Pad work) for ordinary controls, `AdjustSlider`/`AdjustComboBox` (direct `Value`/`SelectedIndex` changes) for Left/Right on a Slider and Up/Down on a ComboBox, `ConfirmFocused` (direct `IsChecked`/`IsDropDownOpen`/`Command.Execute` per type, with `RadioButton` special-cased ahead of `ToggleButton` so Confirm can't uncheck an already-selected radio option) for Confirm. Context menu's Confirm switched from the same broken synthetic-Enter to directly raising `MenuItem.ClickEvent` (its items use `Click=` handlers, and raising a plain `RoutedEventArgs` has no keyboard-device dependency, unlike `KeyEventArgs`). `RaiseKeyOnFocused` removed entirely — dead code once both call sites were gone, and not worth keeping around as a trap for the next person who reaches for it. Sound-on-controller report still open: the shared log dump was captured entirely while Settings was open, where `PlayNavigate`/`PlayTabSwitch` were never wired in (correctly — that's tile-grid/tab-switch feedback, not Settings nav) — need a `[Sound]`-tagged log from LB/RB tab-switching and Library D-Pad nav *outside* Settings to actually diagnose it. |
| 2026-08-19 (3) | Second follow-up: LT+RT cursor lock wasn't firing/logging at all (lowered the combo's own threshold to `ComboPressThreshold=40`, separate from the higher click threshold, since pressing both triggers to a full half-press *simultaneously* is harder than either alone; added edge-triggered raw-value logging for both triggers so a pad whose trigger axes don't map where `RawGameControllerSource` guesses shows up in the log instead of silently never crossing); D-Pad moved focus onto ComboBox/Slider/CheckBox in Settings but couldn't change their value (the MoveFocus/Command-per-type approach never let the control's own keyboard handling run) — replaced with `RaiseKeyOnFocused`, which injects a real `KeyDown` routed event at whatever's focused so WPF's built-in per-control handling (Slider adjusts on Left/Right, ComboBox opens/selects on Up/Down/Space, CheckBox toggles on Space) runs exactly as it would for a physical key, same fix applied to the game context menu's Confirm (its MenuItems use `Click=` handlers, not `Command`, so the old `item.Command?.Execute()` was silently a no-op); game options menu still opened off-tile because `OpenGameContextMenu` always looked up the container in the Library grid regardless of which screen was active — Home/Recently Played have no context menu of their own, so a stale Library container (if one existed from an earlier visit) got used instead, anchored nowhere near the visible tile; gated the Menu button to `SelectedScreen == AppScreen.Library` (matching mouse right-click, which only ever worked there too) and made the open itself wait a layout pass (`ScrollIntoView` + `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`) so Popup placement math sees real container bounds, not a stale zero-size one; added a `CursorLockIndicatorWindow` (see the entry above — this was the intended visual indicator, confirmed via user testing that it hadn't been observed yet, listed here since it's part of the same LT+RT diagnosis); added `Debug.WriteLine("[Sound] ...")` to `SoundService.Play` (skipped/played/threw, tagged by caller) to chase the "sounds don't play from the controller" report, since `PlayTabSwitch`/`PlayNavigate` are already wired into the same code paths both input methods share — no code fix here yet, logging only, pending what the log shows. **Still needs hands-on retest** for all of the above. |
| 2026-08-19 (2) | Follow-up after user retest: D-Pad now confirmed working in Power menu and game context menu, but still dead in Settings, and no visible sign the LT+RT cursor lock did anything. Added: `CursorLockIndicatorWindow`, a small always-on-top badge shown/hidden by `App.SetLocked` in step with `_cursorLocked`; a keyboard-focus ring on `PrimaryButtonStyle` (`IsKeyboardFocused` trigger + `FocusVisualStyle="{x:Null}"` to remove the barely-visible default dashed one) so the focused Power-menu/Settings/context-menu button is actually visible, and applied that style to the two red Power-menu buttons which previously had no style at all; a fallback in the Settings guard that re-seeds focus via the new `SettingsPanel.FocusFirst()` if `Keyboard.FocusedElement` isn't inside the panel when a nav press arrives (the suspected remaining cause — `IsVisibleChanged` firing before layout, or focus never having moved in if Settings was opened by mouse click); `Debug.WriteLine("[Gamepad]"/"[App]"/"[MainWindow]"/"[SettingsPanel]" ...)` at every action/focus decision point, visible in VS's Debug Output window while running under the debugger. **Still needs the user's hands-on retest** to confirm Settings D-Pad and the cursor-lock indicator now actually work — the logs are there specifically so the next failure (if any) points at the exact step that misbehaved. |
| 2026-08-19 | User report: D-Pad dead in Settings/Power menu/game context menu, cursor creeping upward on its own, options menu opening away from the tile — all from a controller with stick drift. Fixed: (1) `MainWindow.HandleGamepadAction`'s Settings/Search guard now routes D-Pad/Confirm through WPF focus nav instead of no-op'ing everything, and seeds initial focus so it has something to start from; (2) game context menu now anchors to the tile (`Placement=Center`) instead of `MousePoint`, and gained the same D-Pad routing; (3) `XInput.LeftThumbDeadzone`/`RightThumbDeadzone` both raised — the left stick feeds the same D-Pad bits (`GamepadWatcher.ToDirectionBits`), so its drift was holding a phantom direction and fighting real D-Pad presses everywhere, not just moving the cursor; (4) added `GamepadAction.ToggleCursorLock` (LT+RT held together) to freeze the stick-driven cursor entirely as a manual escape hatch. **Not yet hands-on verified with the reporting user's actual controller** — build/compile-checked only. |
| 2026-08-18 | Live keyboard-only test (no controller) found `MainWindow`'s window-level `PreviewKeyDown` was swallowing Left/Right/Up/Down/Enter/Escape before a focused `TextBox` (search box, Settings API-key fields) ever saw them, breaking cursor movement and Escape/Enter while typing. Fixed by skipping the handler when `Keyboard.FocusedElement is TextBox`. Also added a keyboard equivalent to `OverlayWindow` (previously mouse-only, the one modal dialog missing one). |
| 2026-08-17 | Swapped what the Start (Menu/☰/Options) and Guide (round Xbox/PS) buttons do, after hands-on testing with a real Xbox controller found the mapping confusing — "which button opens a game's options," "the home button doesn't do what's expected." Root cause for the Guide-button confusion: Windows' own Xbox Game Bar grabs that exact physical button globally by default (Settings → Gaming → Xbox Game Bar), which this app can't override — pressing it could pop Windows' overlay instead of/on top of this app's. Fix: Start/Menu/Options now opens the selected tile's context menu (matches real Xbox Home/PS5 dashboard, where that button always means "options for what's selected" — previously this lived on Guide); Guide/PS keeps its "system-level button" role (overlay toggle in-game, Power menu otherwise) but is no longer the only path to anything essential, since it's the one button an OS-level hook can steal. `GamepadWatcher.ActionMap`, `App.OnGamepadAction`/`OnControllerChanged`, `OverlayWindow.HandleAction`, `OverlayViewModel`, `ControllerGlyphs` (Xbox: Menu→"Menu", Power→"Xbox"; PlayStation: Menu→"Options", Power→"PS") all updated to match. Builds clean, all 10 self-checks pass. **Still not re-verified hands-on after this specific change** — the original complaint was found via user testing, but this fix itself needs the same real-controller retest before calling it done. |
| 2026-08-13 | Overlay (`OverlayWindow`) now registers as a modal gamepad target (D-Pad Up/Down + Confirm/Back), fixing the mouse-only gap. Title bar's bare minimize/close buttons replaced with a Power button opening a new controller-navigable power menu (`PowerMenuWindow`): Turn Off System / Restart System / Exit to Desktop / Shut Down Cartridge OS. Bound to the previously-unused Start button (`GamepadAction.Power`) and keyboard F4. Each menu option also has an Alt+letter access-key keyboard alternative (native WPF mnemonics, no extra code). |
| 2026-08-12 | Overlay toggle moved from Start/Options to the Xbox Guide button / PS button (undocumented `XInputGetStateEx`, since the public XInput API masks the guide bit out) — see `context.md`. |
| 2026-08-12 | First real "controller-first" pass: wired up three previously-dead/missing bindings — B (`GamepadAction.Back`) now actually closes Settings/Search instead of doing nothing outside modal dialogs; added `ToggleSettings` (View/Share button) and `ToggleSearch` (X/Square) as new `GamepadAction`s so Settings and Search are reachable by controller at all (previously mouse-only). Fixed `ControllerGlyphs`' stale "Start" label for `GamepadAction.Menu` (leftover from before the Guide-button remap) to "Guide"/"PS". Added `Key.Escape`→Back and `Key.Tab`→ToggleSettings keyboard equivalents. This file created to track all of it going forward. |

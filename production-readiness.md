# Production Readiness

What's left before this ships to a real user's PC, beyond feature completion. Update as items are closed or new gaps are found.

## Current assessment (2026-08-16): not production-ready

Triage of the checklist below by actual severity, done when asked "if this goes to production is everything fine?" — not a new checklist, just prioritization of what's already tracked.

**Hard blockers (would actively hurt real users):**
- No physical controller has ever tested this app, despite it being marketed as controller-first — XInput/PlayStation mapping, right-stick mouse emulation, deadzones, the whole input layer is verified only by code inspection and self-checks. The single riskiest gap given the app's core pitch. See "Controller compatibility tested" under Testing.
- No installer — no MSI/MSIX/Inno Setup, no autostart registration, no clean uninstall. Right now "shipping" means handing someone a folder of exes. See Packaging & install.
- No code signing — every launch will trigger Windows SmartScreen warnings, which reads as malware to most users. See Packaging & install.
- No crash recovery — if the Service crashes there's no restart policy; if the Launcher crashes in fullscreen controller mode, there's no documented fallback to get a mouse/keyboard back. See Reliability.
- API keys (SteamGridDB, TheGamesDB) and the Discord Client ID are hardcoded into the shipped DLL — a deliberate, reasoned tradeoff for a closed-source app (see context.md's "Branding assets"/`ArtworkFetcher.cs` notes), but worth flagging before wider distribution: no way to rotate a leaked/abused key without shipping a new build. Not previously tracked here — added as a new Security item below.

**Should-fix before calling it done:**
- Only Steam, Riot, EA, and the standalone/Xbox scanners have been checked against real installs (2026-08-16) — GOG, Ubisoft, Battle.net, and Epic are still unverified against anything real. See Testing.
- Named-pipe IPC has no message validation/sanitization yet (Service trusts Launcher input blindly). See Security.
- No memory profiling on the long-running Service, no check that the artwork cache doesn't grow unbounded, no test with a large (500+) game library. See Performance.
- "Graceful handling of a game that fails to launch" isn't built — a bad exe path or permissions issue has no defined user-facing behavior yet. See Reliability.

**Lower risk / polish:**
- No auto-update mechanism (may be an intentional V1 decision, not necessarily a blocker). See Packaging & install.
- Several UI/animation details are marked "not visually verified" throughout progress.md/context.md — cosmetic risk, not functional risk, not re-listed here.

## Packaging & install
- [ ] Installer (MSI/MSIX or Inno Setup) that registers the Service, installs Launcher (now the single tray+UI process — Tray project was merged in, see progress.md 2026-08-07), sets up autostart
- [ ] Clean uninstall (removes service, registry entries, scheduled tasks)
- [ ] Code signing (avoid SmartScreen warnings)
- [ ] Auto-update mechanism (or explicit decision to skip for V1)

## Reliability
- [ ] Service crash recovery / restart policy
- [ ] Launcher crash doesn't take down the Service or leave the machine unusable (no keyboard/mouse in fullscreen controller mode)
- [ ] Graceful handling of games that fail to launch (missing exe, permissions)
- [ ] Named-pipe reconnect logic if Launcher starts before Service is ready

## Performance
- [ ] Cold boot time to fullscreen UI measured and acceptable
- [ ] Game grid virtualization verified with large library (500+ games)
- [ ] Artwork cache doesn't grow unbounded on disk
- [ ] Memory profiled for leaks in long-running Service (runs continuously)

## Security
- [ ] Named-pipe IPC validates/sanitizes messages (don't trust Launcher input blindly in Service)
- [ ] No arbitrary command execution beyond configured game launch paths
- [ ] SQLite DB not writable by untrusted processes in a way that leads to code exec
- [ ] Hardcoded API keys (SteamGridDB, TheGamesDB) and Discord Client ID ship inside the compiled DLL of every install (`Core/Scanning/ArtworkFetcher.cs`, `Services/DiscordRichPresence.cs`) — deliberate tradeoff for a closed-source app (env vars would buy no real protection and add a setup step, see context.md's "Branding assets" section for the full reasoning), but means a leaked/abused/rate-limited key can only be rotated by shipping a new build, not by revoking it server-side. Revisit if this project is ever open-sourced.

## UX / accessibility
- [x] Full controller-only navigation with no dead ends (every screen reachable/exitable via controller) — the header's old bare Minimize/Close buttons were replaced with a Power button/menu (Exit to Desktop / Shut Down Cartridge OS / Restart System / Turn Off System), bound to the controller Start button (`GamepadAction.Power`) and keyboard F4, fully controller-navigable
- [ ] Keyboard/mouse fallback still works for setup/debugging
- [ ] Error states are visible in fullscreen mode (no silent failures)

## Testing
- [ ] Scanner tested against real installs of each supported launcher — **partially done 2026-08-16**: Steam, Riot, EA, standalone-executable, and Xbox scanners verified against real installs on one dev machine (see progress.md/context.md 2026-08-16 entries) and several real bugs found+fixed there (Riot title bug, missed/mistitled nested installs, Xbox false-positive flood, Steam duplicate). Epic, GOG, Ubisoft, and Battle.net still unverified — no real installs of those available on the machine this was tested on.
- [ ] Controller compatibility tested: Xbox, DS4, generic HID/DirectInput — **not a gap, the single riskiest open item in this whole file**: no physical controller of any kind has tested this app yet, despite "controller-first" being the core pitch (see "Current assessment" above). Every gamepad-touching feature across progress.md/context.md — XInput polling, PlayStation/RawGameController fallback, deadzones, right-stick mouse emulation, the Guide/PS-button overlay toggle, controller-only navigation with no dead ends — is verified only by code inspection, self-checks, and the not-connected code path not throwing.
- [ ] Multi-monitor behavior verified

## Docs
- [ ] Basic user setup guide
- [ ] Known limitations documented

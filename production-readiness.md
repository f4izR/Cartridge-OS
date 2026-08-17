# Production Readiness

What's left before this ships to a real user's PC, beyond feature completion. Update as items are closed or new gaps are found.

## Current assessment (2026-08-17): not production-ready

Triage of the checklist below by actual severity, done when asked "if this goes to production is everything fine?" — not a new checklist, just prioritization of what's already tracked.

**Hard blockers (would actively hurt real users):**
- No physical controller has ever tested this app, despite it being marketed as controller-first — XInput/PlayStation mapping, right-stick mouse emulation, deadzones, the whole input layer is verified only by code inspection and self-checks. The single riskiest gap given the app's core pitch. See "Controller compatibility tested" under Testing.
- No installer — no MSI/MSIX/Inno Setup, no autostart registration, no clean uninstall. Right now "shipping" means handing someone a folder of exes. See Packaging & install.
- No code signing — every launch will trigger Windows SmartScreen warnings, which reads as malware to most users. See Packaging & install.
- No crash recovery — **narrowed 2026-08-17**: the Service is now SCM-controllable (`AddWindowsService()`), so a restart-on-crash policy is installable, but nothing configures it yet since there's no installer to run `sc failure`. The Launcher-crash/no-mouse-keyboard half of this was checked and isn't a real gap (see Reliability) — remaining blocker here is purely "no installer to set the SCM restart policy," which folds into the installer item below rather than being separate.
- API keys (SteamGridDB, TheGamesDB) and the Discord Client ID are hardcoded into the shipped DLL — the Discord Client ID isn't actually sensitive (client IDs are meant to be public). The two bundled API keys are still shared by every install and can only be rotated by shipping a new build if abused — **narrowed 2026-08-17**: users can now opt into their own free key via Settings → Library → Artwork Sources instead of the bundled one, which removes the shared-key liability for anyone who cares to; the bundled key remains the default so nothing changes for anyone who doesn't. See Security below.

**Should-fix before calling it done:**
- Only Steam, Riot, EA, and the standalone/Xbox scanners have been checked against real installs (2026-08-16) — GOG, Ubisoft, Battle.net, and Epic are still unverified against anything real. See Testing.
- ~~Named-pipe IPC has no message validation/sanitization yet~~ — **fixed 2026-08-17**, see Security.
- No memory profiling on the long-running Service, no check that the artwork cache doesn't grow unbounded, no test with a large (500+) game library. See Performance.
- ~~"Graceful handling of a game that fails to launch" isn't built~~ — **fixed 2026-08-17**, see Reliability.

**Lower risk / polish:**
- No auto-update mechanism (may be an intentional V1 decision, not necessarily a blocker). See Packaging & install.
- Several UI/animation details are marked "not visually verified" throughout progress.md/context.md — cosmetic risk, not functional risk, not re-listed here.

## Packaging & install
- [ ] Installer (MSI/MSIX or Inno Setup) that registers the Service, installs Launcher (now the single tray+UI process — Tray project was merged in, see progress.md 2026-08-07), sets up autostart
- [ ] Clean uninstall (removes service, registry entries, scheduled tasks)
- [ ] Code signing (avoid SmartScreen warnings)
- [ ] Auto-update mechanism (or explicit decision to skip for V1)

## Reliability
- [~] Service crash recovery / restart policy — **2026-08-17**: Service now calls `AddWindowsService()` (`Program.cs`) so it's SCM-controllable once installed; the actual restart-on-crash policy is an `sc failure CartridgeOS reset=...` call the installer still needs to make (no installer exists yet, see Packaging & install). Also added a global crash handler + append-only crash log (`%LocalAppData%\CartridgeOS\crash.log`) to the Launcher (`App.xaml.cs`) — previously zero diagnostic trail existed for any unhandled exception in either process.
- [x] Launcher crash doesn't take down the Service or leave the machine unusable — **checked 2026-08-17, not actually a gap**: separate OS processes (a Launcher crash can't touch the Service), and Windows' own crash dialog renders above topmost/fullscreen windows fine, so a crash doesn't strand the user without mouse/keyboard. No code needed here.
- [x] Graceful handling of games that fail to launch (missing exe, permissions) — **fixed 2026-08-17**: `App.LaunchGame` (`App.xaml.cs`) now distinguishes a real launch failure (Win32Exception/FileNotFoundException) from a legitimate Steam/Xbox shell launch (both previously returned `process is null`), clears the "Launching..." indicator immediately instead of minimizing the window, and surfaces the failure via the existing tray balloon tip instead of failing silently.
- [x] Named-pipe reconnect logic if Launcher starts before Service is ready — **checked 2026-08-17, not actually a gap**: `CartridgeOsPipeClient.SendAsync` already never throws (returns `null` on any connection failure), and nothing on the Launcher's real startup/runtime path depends on the Service's pipe — only the `--ipc-ping` diagnostic and cross-instance signaling do, both of which already handle a `null` response. No retry logic needed unless the Service becomes load-bearing for real functionality later.

## Performance
- [ ] Cold boot time to fullscreen UI measured and acceptable
- [ ] Game grid virtualization verified with large library (500+ games)
- [ ] Artwork cache doesn't grow unbounded on disk
- [ ] Memory profiled for leaks in long-running Service (runs continuously)

## Security
- [x] Named-pipe IPC validates/sanitizes messages — **fixed 2026-08-17**: `CartridgeOsPipeServer` (`Core/Ipc/CartridgeOsPipeServer.cs`) now (1) creates the pipe with an explicit ACL restricting access to Authenticated Users, so anonymous/guest logons on the same machine can no longer connect at all — previously the pipe had the OS default ACL, and this will matter more once the Service is actually installed and runs as LocalSystem; (2) rejects a request outright (before it ever reaches `handleRequest`) if `Command` is empty/whitespace/over 256 chars or `Payload` is over 4KB — sized to what every real command today actually needs (short fixed strings, no payload), not to `PipeFraming`'s 10MB framing-safety cap. Both processes' pipes go through this same server class, so Service and single-instance-signaling get the fix for free.
- [x] No arbitrary command execution beyond configured game launch paths — **checked 2026-08-17, already true**: the Service's command switch (`Worker.HandleRequest`) only exposes `Ping`/`GetGameCount`, and the only thing that ever calls `Process.Start` on IPC-adjacent code is game launching itself, which always reads `ExecutablePath` from the local SQLite DB, never from a pipe message.
- [x] SQLite DB not writable by untrusted processes — **fixed 2026-08-17**: `GameDatabase`'s constructor (`Core/Data/GameDatabase.cs`) now explicitly hardens the `%LocalAppData%\CartridgeOS` directory's ACL (protects it from inherited rules, grants only the current user + Administrators + SYSTEM) before opening the connection, rather than relying on whatever ACL the folder happened to inherit. Verified with `icacls` before/after: a pre-existing test folder that had inherited a broad sandbox-group grant lost that grant and kept only SYSTEM/Administrators/current-user after a real run. Since this directory is also where `settings.json`, the artwork cache, and every `*.log` file live, they get the same hardening for free (inherited from the now-protected parent) — this was the one call site guaranteed to run first in both the Launcher and Service startup paths. Best-effort/non-fatal if it can't set the ACL (e.g. non-NTFS volume); a pre-existing `games.db` file itself keeps its old file-level ACL until rewritten, only the directory (and anything created in it afterward) is affected.
- [x] Hardcoded API keys — **narrowed 2026-08-17**: `ArtworkFetcher.cs`'s bundled `SteamGridDbApiKey`/`TheGamesDbApiKey` are still shared by every install (rotating either still means shipping a new build — an env var would buy no real protection for a closed-source app, see the file's own comment), but a user who cares can now opt out of the shared key entirely via Settings → Library → Artwork Sources, pasting their own free key instead. `AppSettings.SteamGridDbApiKeyOverride`/`TheGamesDbApiKeyOverride` (blank = use the bundled key); `ArtworkFetcher.EffectiveSteamGridDbApiKey`/`EffectiveTheGamesDbApiKey` resolve which one to use per fetch. The Discord Client ID needed no change — client IDs are meant to be public, not a real secret.
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

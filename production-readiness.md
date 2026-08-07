# Production Readiness

What's left before this ships to a real user's PC, beyond feature completion. Update as items are closed or new gaps are found.

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

## UX / accessibility
- [ ] Full controller-only navigation with no dead ends (every screen reachable/exitable via controller) — mouse-clickable Minimize/Close buttons now exist in the header (fixes the mouse/keyboard-only path, Alt+F4 is no longer the only way out), but there's still no gamepad-button binding for exit/minimize
- [ ] Keyboard/mouse fallback still works for setup/debugging
- [ ] Error states are visible in fullscreen mode (no silent failures)

## Testing
- [ ] Scanner tested against real installs of each supported launcher
- [ ] Controller compatibility tested: Xbox, DS4, generic HID/DirectInput
- [ ] Multi-monitor behavior verified

## Docs
- [ ] Basic user setup guide
- [ ] Known limitations documented

# Changelog

All notable changes to KTP File Distributor will be documented in this file.

## [1.1.2] - 2026-04-02

### Fixed
- **Async void crash risk in ChangeDebouncer** — `OnTimerElapsed` was `async void`, meaning unhandled exceptions would crash the entire service. Refactored to `async Task` with explicit fire-and-forget wrapper and try/catch.
- **Path traversal in remote upload** — `BuildRemotePath` did not validate relative paths. A path containing `../` could escape the server's base directory. Now rejects traversal patterns.
- **Generic exception catch in `EnsureRemoteDirectoryExists`** — Bare `catch` swallowed all exceptions including fatal ones. Now catches only `SshException` and `IOException`.

---

## [1.1.1] - 2026-03-03

### Changed
- Shutdown Discord notification now uses embed format (orange) matching the startup embed style, instead of plain text

---

## [1.1.0] - 2026-01-10

### Added
- Multi-channel Discord support via `AdditionalChannelIds` config option
- `GetAllChannelIds()` helper method for iterating all configured channels
- Error handling per-channel (one failure doesn't block others)

### Changed
- Discord notifications now sent to all configured channels (primary + additional)
- Improved logging to show which channel failed on errors

---

## [1.0.0] - 2025-12-18

### Added
- Initial release
- FileSystemWatcher-based directory monitoring
- SFTP file distribution to multiple servers in parallel
- Support for password and SSH private key authentication
- Configurable file patterns (*.amxx, *.bsp, *.txt, *.bmp, *.cfg, *.wad, *.res, *.mdl, *.wav)
- Debounced file change batching to prevent redundant uploads
- Automatic retry logic for failed uploads
- Discord notifications via webhook relay
- Serilog-based logging (console + rolling file)
- systemd service integration for Ubuntu 24.04
- Self-contained Linux x64 deployment support
- Automatic remote directory creation
- File deletion synchronization across servers

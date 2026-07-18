# Changelog

All notable changes to KTP File Distributor will be documented in this file.

## [1.1.3] - 2026-07-18

### Fixed
- **Renames never removed the old remote copy (FD-01).** `OnFileRenamed` only turned the *new* path into a `FileChangeEvent`; the old filename was logged and dropped, and `ChangeDebouncer` keys purely on `RelativePath`, so the old name never got a delete on its own. Renaming a watched file left the old copy on every fleet server and FastDL indefinitely, with no cleanup path — anything still referencing the old name (a stale `server.cfg` exec line, a cached FastDL link) kept serving it. `OnFileRenamed` now emits a `Deleted` event for the old path alongside the new-name event. It also handles a rename to a non-watched name (e.g. `.amxx` → `.amxx.disabled`): the new name no longer matches a pattern, so only the delete of the old copy fires.
- **One bad server blanked out the whole batch's result (FD-02).** `CreateSftpClient(server)` ran inside `UploadToServerAsync`'s outer `try` — which has only a `finally`, no `catch` — before the retry loop's try/catch. A missing/malformed `privateKeyPath` throws while constructing `PrivateKeyFile`, so that fault propagated past all per-server isolation, faulted `Task.WhenAll` in `DistributeAsync`, and skipped `result.ServerResults = …` entirely: the batch produced no per-server results and no Discord notification, even for servers that had already finished successfully, and it repeated every batch. Client construction is now wrapped in its own try/catch that records a normal failed `ServerUploadResult` and returns, so one bad server no longer discards the batch.

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

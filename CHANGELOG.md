# Changelog

All notable changes to KTP File Distributor will be documented in this file.

## [Unreleased]

### Documentation
- **FastDL `dod/` path rule documented in the README.** The rule existed only in the
  agent-facing skill and `CLAUDE.md`; it had never reached the operator-facing doc, even
  though this service is the thing that writes to FastDL and one wrong `remoteBasePath`
  reproduces the 404-on-disk bug silently for every asset forever. Added the canonical
  `/var/www/fastdl/dod/<game-relative-path>` form, the `curl -sI` verification, and the
  fastdl-root sanity check.
- README `WatchPatterns` example now matches the shipped `appsettings.json` (was missing
  `*.res`, `*.mdl`, `*.wav` — copying it verbatim silently stopped distributing models
  and sounds, exactly the FastDL client-download assets).
- Documented the 1.1.3 rename behavior and the 1.1.2 path-traversal rejection, both of
  which shipped with a README version bump only.
- Noted that delete failures are logged as warnings, are not retried, and do not fail the
  server's result — uploads do not behave this way.
- Stated the `servers.json` location and that a missing file auto-generates an example and
  starts the service with zero targets.
- `SYNC_NEW_SERVER.md`: replaced production IPs with the placeholder tokens this repo's
  `CLAUDE.md` already mandates (the file predates that convention), and added the two
  missing fleet hosts — including Chicago's 4-instance port range, which the doc's
  `27015 … 27019` loop idiom does not cover.
- Corrected the watch-directory "default" (it's the configured value, not the compiled-in
  one), switched the directory-structure example from `amxmodx` to this stack's `ktpamx`,
  and removed an orphaned `.gitignore` comment asserting that a tracked public file
  contains production credentials.

---

## [1.1.4] - 2026-08-09

### Fixed
- A failed delete no longer reports success. `DeleteFileAsync` caught every
  exception and logged a warning, so the per-server result stayed
  `Success = true` and the Discord embed said the batch landed while a server was
  still holding the stale file. Delete failures now propagate into the same retry
  loop uploads use; a server that cannot delete ends the batch red. Retrying is
  safe — re-uploads overwrite, and the `Exists` guard makes the delete idempotent.
  Delete events have been live traffic since the 1.1.3 rename fix, so this was no
  longer a dormant path.
- Both operations are now wrapped **per file**, so one failure no longer aborts
  the rest of the batch for that server. Letting the exception propagate (the
  first shape of this fix) traded a lie for a worse defect: every file ordered
  after the failure was dropped, on every retry, and `FileWatcherWorker` does not
  re-queue — so a single undeletable file would block unrelated pushes to that
  host indefinitely. The pass now applies what it can, retries while attempts
  remain, and fails with the offending paths named rather than just counted. The
  per-file catch is guarded on `client.IsConnected` so a dropped connection still
  reaches the retry loop instead of becoming N per-file failures.

### Removed
- `ChangeDebouncer.PendingCount` — declared, never read. `_pendingChanges.Count`
  is already logged from `AddChange` for the same information.

## [1.1.3] - 2026-07-18

### Fixed
- **Renames never removed the old remote copy (FD-01).** `OnFileRenamed` only turned the *new* path into a `FileChangeEvent`; the old filename was logged and dropped, and `ChangeDebouncer` keys purely on `RelativePath`, so the old name never got a delete on its own. Renaming a watched file left the old copy on every fleet server and FastDL indefinitely, with no cleanup path — anything still referencing the old name (a stale `server.cfg` exec line, a cached FastDL link) kept serving it. `OnFileRenamed` now emits a `Deleted` event for the old path alongside the new-name event. It also handles a rename to a non-watched name (e.g. `.amxx` → `.amxx.disabled`): the new name no longer matches a pattern, so only the delete of the old copy fires.
- **One bad server blanked out the whole batch's result (FD-02).** `CreateSftpClient(server)` ran inside `UploadToServerAsync`'s outer `try` — which has only a `finally`, no `catch` — before the retry loop's try/catch. A missing/malformed `privateKeyPath` throws while constructing `PrivateKeyFile`, so that fault propagated past all per-server isolation, faulted `Task.WhenAll` in `DistributeAsync`, and skipped `result.ServerResults = …` entirely: the batch produced no per-server results and no Discord notification, even for servers that had already finished successfully, and it repeated every batch. Client construction is now wrapped in its own try/catch that records a normal failed `ServerUploadResult` and returns, so one bad server no longer discards the batch.

---

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

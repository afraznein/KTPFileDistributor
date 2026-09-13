# Changelog

All notable changes to KTP File Distributor will be documented in this file.

## [1.2.2] - 2026-09-12

### Fixed
- **A server a per-server filter excludes from a whole batch rendered as a real, instant
  upload in Discord.** `UploadToServerAsync` returns early for that server without ever
  connecting, and set only `Success = true` -- indistinguishable in the embed from an
  upload that genuinely finished in `Duration = 0.0s`. With the FastDL entry's
  `excludePatterns` on `*.cfg`/`*.ini`, every config-only push showed FastDL as a 0.0s
  success. `ServerUploadResult` now carries a `Skipped` flag, set on that early return;
  the embed renders a skipped server as `- {ServerName} skipped (filtered)` instead of a
  duration, and the `Servers` field and `DistributionResult.GetSummary()` name the skip
  count separately from the success count (e.g. `24/24 successful (1 skipped)`) rather
  than folding it into "successful" with no distinction. `AllSuccessful` still treats a
  skip as non-failing -- a batch whose only non-uploads are filtered-out skips is not a
  partial failure. `TotalBytesTransferred` now multiplies by servers that actually
  uploaded, not by `SuccessCount` (which included skips and so overstated bytes moved).

## [1.2.1] - 2026-09-12

### Fixed
- **`WatchPatterns` never actually replaced the compiled-in default.** `AppSettings.WatchPatterns`
  defaulted to `new() { "*.*" }`, and .NET's configuration binder *adds* configured list items onto
  an existing `List<T>` default rather than replacing it. So a configured list of, say, 11 extensions
  bound to 12 entries — the 11 configured plus the original `"*.*"` still sitting at index 0 — and the
  service watched every single file in the tree regardless of what `appsettings.json` said. This has
  been true since 1.0.0: the live service has logged `Patterns: *.*, *.amxx, ...` since at least
  2026-07-31. It is how a `sed` backup and `sed`'s own temp file were both replicated to every fleet
  server on 2026-08-06, and how a `.staging` ban-list file went out on 2026-08-09. The default is now
  an empty list, which `PatternMatcher.MatchesWatchPatterns` already treats as "match everything" — an
  unconfigured deployment behaves exactly as before, and a configured one now genuinely narrows.
- The default `WatchPatterns` example in `appsettings.json`, `README.md` and `CLAUDE.md` was missing
  `*.spr` and `*.ini`, both of which the fleet has been distributing in production (sprites reach
  FastDL — see the `xrain2.spr`/`flare1.spr` note already in the README's FastDL section — and `.ini`
  addon configs like `discord.ini`/`hltv_recorder.ini`/`plugins.ini` reach the game servers, which is
  why the FastDL example already excludes it). Narrowing `WatchPatterns` down to the pre-1.2.1 example
  list would have silently stopped distributing both. Added `*.spr` and `*.ini` and the corresponding
  entry to "Supported File Types".
- **Per-server `includePatterns`/`excludePatterns` (1.2.0) were invisible at startup.** The only signal
  was target *names*; the per-file "skipped" line is Debug level, and a typo like `excludePattern`
  (singular) deserializes to an empty list with no error from `System.Text.Json` — the filter fails
  open, silently. Each enabled target's include/exclude lists are now logged at Information level at
  startup, and a pattern in a shape the matcher can't honour (anything with a `*` other than `*.ext` or
  the literal `*.*` — e.g. `maps/*`, `**/*.cfg`, `*.cfg*`) logs a startup warning, since each of those
  falls through to the exact-path branch and matches nothing. Both the startup logging and the shape
  check tolerate a `null` list and a `null` element inside one (either is possible from hand-edited JSON;
  `ServerConfig.Accepts` already tolerated the same null-list case) — this runs unconditionally on every
  start, so a crash here would take the whole service down rather than fail to match one file later.

### Note
- **This narrows what is distributed, and the live evidence says the narrowing is exactly right.**
  Measured on the data server's own `distributor-*.log` history (18,088 `Distribution` lines is the
  control that the grep reads the logs at all): every extension ever actually distributed is `ini`
  9,016 · `amxx` 60 · `txt` 14 · `cfg` 11 — all four already in this list. The only other things `*.*`
  ever caught are exactly the junk it should never have caught: `bak-rot-20260806` (12), three `sed*`
  editor temp files (11, across two directories), and `.ktp_ac_bans.ini.staging` (3). **The live
  `appsettings.json`'s `WatchPatterns` is already this exact 11-entry list**, so after this fix the
  effective set is unchanged from what has actually been shipping — this PR removes `*.*` and changes
  nothing else about what is distributed.
- `*.amxx.new` (the plugin/module `.new` → 03:00 swap pipeline) was checked and is not a concern here:
  `.new` appears 0 times in the same log history, because that pipeline stages over SSH via
  `stage-wave.py`, never through this service's watch directory.
- 🔴 **A gap this list inherits rather than creates, worth recording rather than leaving as a surprise
  for whoever finds it next:** the watch tree currently holds 203 `.tga`, 74 `.jpg` and 51 `.sc` files
  that match no pattern, live or proposed, so none of them have ever been distributed and none will be
  after this change either. `.tga` and `.sc` in particular look like real DoD client assets. Whether
  they're meant to ship is an operator call, not something this PR decides — flagging it here so it's
  a known gap rather than a rediscovery.

## [1.2.0] - 2026-09-11

### Added
- Per-server `includePatterns` and `excludePatterns` in `servers.json`. A path
  a server does not accept is neither uploaded to it nor deleted from it, and
  that includes the delete a rename issues for the old name. Both default to
  empty, which is exactly the old behaviour, so an existing `servers.json`
  needs no change. When nothing in a batch applies to a server, the service
  does not connect to it and reports it as succeeded.
- The patterns use the same rules as `WatchPatterns`, now kept in one shared
  `PatternMatcher`: `*.ext` matches at any depth, case-insensitively; `*.*`
  matches everything; any other pattern must equal the whole watch-relative
  path.
- The recommended FastDL entry now carries `"excludePatterns": ["*.cfg", "*.ini"]`.
  The FastDL docroot is public over both HTTP and FTP, and server configs
  (which can hold `rcon_password`) have no reason to be there. Neither
  extension is a client download.
- `KTPFileDistributor.Tests` (xUnit), run by CI on every PR.

### Note
- Filters apply from the moment they are configured; they do not clean up.
  Files a server already holds that now match its excludes stay there until
  someone removes them by hand, because deletions for those paths are no longer
  sent to that server.

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
  per-file catch is guarded on `client.IsConnected` for **latency, not
  correctness** — the batch replays either way, since `failed.Count > 0` reaches
  the same disconnect/delay/replay the outer catch would use. What the filter
  avoids is grinding the batch tail against a half-open socket at up to
  `ConnectionTimeoutSeconds` per file while holding one of five semaphore slots.
- The README asserted the **pre-1.1.4** delete behaviour, inverted on all three
  counts — that delete failures are not retried, do not fail the server, and that
  the operator should trust the log over the Discord embed. The embed is now the
  correct signal, so that line pointed the operator away from it. This is the
  residue of removing the CHANGELOG line making the same claim without removing
  the README text it was documenting.
- Shutdown is no longer logged as an upload failure. A cancellation raised in the
  file loop was caught by the attempt-level handler ("attempt N failed,
  retrying") and, on the final attempt, swallowed into a normal failed result.
- The Discord embed summarises a per-server error instead of pasting it whole,
  and truncation now reports how many servers it dropped. Naming failed paths
  made each line 3-5x longer, and `Server Details` is capped at Discord's 1024;
  with 25 targets a failure that hits every host — one bad file mode is enough —
  showed ~5 servers behind a bare `...` that read as a short list. The per-server
  error is also flattened before truncation: the `(+N more)` count is derived from
  newlines, so one embedded newline made the very number this added go negative.
- Cancellation now ends the batch quietly rather than as an error with a stack
  trace. ⚠️ **A stop mid-batch leaves no Discord record of the partial
  application** — `DistributeAsync` throws before it can populate `ServerResults`,
  and `ChangeDebouncer` has already cleared the pending set, so nothing re-queues.
  The log line says so explicitly.

### Removed
- `ChangeDebouncer.PendingCount` — declared, never read. `_pendingChanges.Count`
  is already logged from `AddChange` for the same information.

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

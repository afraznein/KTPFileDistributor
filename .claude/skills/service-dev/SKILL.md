---
name: service-dev
description: Use BEFORE modifying, building, or deploying KTPFileDistributor — the SFTP fan-out gaps you must not reintroduce, the FastDL path rule this service's own config can violate, and the build/deploy/verify checklist.
---

# KTPFileDistributor Development

.NET 8 Worker Service running on the data server. Watches a directory
(`/home/dod/distribute`) for file changes and SFTPs them out in parallel to
every fleet game server plus FastDL. Single point of failure for plugin/map/
config distribution across the whole fleet — a silent bug here means stale
files on 24 live instances, not just one.

## Hard safety rules
- **Never restart a game server** (any `dodserver` instance) without explicit
  operator permission in the current conversation, even if a change here is
  meant to push new files to it.
- This service itself is not a game server, but don't stop/start it casually
  either — a stop mid-batch can drop an in-flight retry silently
  (`ChangeDebouncer` clears `_pendingChanges` before invoking `OnBatchReady`,
  so nothing re-queues it); prefer letting a batch finish first.
- Deploys to the AMX/plugin fleet still go through the standard `.new`
  staging + 03:00 ET nightly swap — this service is one of the things that
  *pushes* those `.new` files, not something that consumes them itself.
- Run the `ktp-code-review` agent before staging any nontrivial change to
  `SftpDistributorService.cs`, `FileWatcherWorker.cs`, or `ChangeDebouncer.cs`.
- After distributing anything fleet-wide through this service, verify with
  md5 across all 24 instances and check `find /tmp -maxdepth 1 -name
  'core.*' -mtime -1` on affected hosts — a game-tree `core.*` search always
  looks clean whether or not something crashed.
- Comments: short, explain why not what, no ticket/finding IDs, never delete
  a tripwire fact when trimming.

## Known gaps — don't reintroduce these, don't copy the pattern elsewhere
- ✅ **Renames leak remote copies — FIXED in 1.1.3** (FD-01). `OnFileRenamed`
  now emits a second `FileChangeEvent` for `GetRelativePath(e.OldFullPath)`
  with `ChangeType = Deleted` alongside the new-name event
  (`FileWatcherWorker.cs`). Kept here because the *pattern* is the trap:
  `ChangeDebouncer` keys purely on `RelativePath`, so any event that implies a
  second path must emit a second entry or that path is silently never applied.
- ✅ **One bad server blanking a whole batch's result — FIXED in 1.1.3**
  (FD-02). `CreateSftpClient(server)` is now inside the per-server try/catch
  in `UploadToServerAsync`, so a bad `privateKeyPath` can no longer propagate
  through `Task.WhenAll` in `DistributeAsync` and skip `result.ServerResults`
  entirely. Keep any future per-server construction inside that server's own
  try/catch — that is the rule this one broke.
- **Per-file failures must not abort the batch (fixed 1.1.4).** Both operations
  are wrapped per file: a failure is recorded, the remaining files still get
  applied, and the pass fails at the end with the failed paths named. Before
  1.1.4 `DeleteFileAsync` swallowed its exceptions and reported `Success = true`
  for a server still holding a stale file; the first fix let them propagate,
  which then dropped every file ordered *after* the failure — permanently, since
  `FileWatcherWorker` does not re-queue. One undeletable file would have blocked
  unrelated pushes to that host indefinitely.
  The per-file catch is guarded by `when (client.IsConnected)` for **latency,
  not correctness** — the batch replays either way (`failed.Count > 0` reaches
  the same disconnect/delay/replay). What the filter avoids is grinding the
  batch tail against a half-open socket at up to `ConnectionTimeoutSeconds` per
  file while holding one of five semaphore slots. Don't "simplify" it away, and
  don't believe a comment that calls it load-bearing for replay.

## What a successful distribution does and does not prove

- **Every push is a whole-file overwrite.** There is no merge and no diff, so a
  file that is missing lines does not fail — it removes those lines from every
  destination. A config regenerated from a template that has drifted from the
  live file strips the difference fleet-wide, and the run reports success
  because the bytes it was given arrived intact.
- **"Green" means bytes landed, not that the right content is live.** The
  service does not compare against an expected hash, read the file back, or ask
  the destination whether it liked what it got — the FastDL path footgun in this
  README is the standing example of a SUCCESS report while every client 404'd.
  Anything whose correctness matters needs its own verification after the push.
- **Distribution is change-driven and never reconciles.** A file sitting in the
  watch tree that nobody has touched has never been sent, so its presence there
  is not evidence any server has it. One had sat in the tree for months without
  ever landing anywhere while the transport was working perfectly the whole time.

## Scope: content and assets, never game-server binaries

Plugins, modules and engine binaries go through the `.new` staging path and the
03:00 swap, and moving them onto this service would break four things at once,
each of them across 24 live instances: it overwrites the live `.amxx` in place,
so an unplanned crash-restart brings up a binary nobody deliberately activated;
it fires on any change, so there is no one-wave-per-nightly attribution when
something goes wrong; a success report says bytes landed, not that the intended
binary is running; and a rename or delete in the watch tree propagates a delete
to every server. If the motivation is less manual work, script the `.new` path —
do not retarget this one.

## Anything distributed to the web root is published

The FastDL destination is a public document root, and destinations are per-server
entries with no per-file exclusions — the watch tree fans out to every enabled
entry or none. So a file placed in the tree for the game servers is also served
over HTTP. Keep configs that carry a secret out of the tree entirely; a mitigation
on the web server is a sweeper, not a gate.

## The service's account is not the tree's owner

It reads the watch tree through its own service account, which does not own those
files. Tightening permissions on them can therefore stop distribution to the whole
fleet without stopping the service or producing an error — the unit stays healthy
and nothing arrives. Grant access through group ownership and verify a real push
afterwards. Useful counterpart when hardening: changing a *directory's* mode leaves
file mtimes alone, so the watcher sees nothing and re-pushes nothing.

## FastDL path rule (this repo is where it usually bites)
`servers.json`'s FastDL entry must set `remoteBasePath` to
`/var/www/fastdl/dod` — the game engine always appends `dod/` itself before
requesting an asset. A `remoteBasePath` of `/var/www/fastdl` (missing the
`dod` segment) silently 404s for every client even though the file exists on
disk. Verify any newly-distributed asset with:
```bash
curl -sI http://<DATA_SERVER_IP>/dod/<game-relative-path>
```

## Config lives off-repo
`appsettings.json` and `servers.json` (with real hostnames, `privateKeyPath`,
sometimes passwords) exist only on the data server at
`/opt/ktp-file-distributor/`, not in this repo. Never commit real values —
the copies in README/CLAUDE.md are placeholder templates.

## Workflow
1. Build (Windows, self-contained linux-x64):
   ```powershell
   .\build-linux.ps1
   ```
   Output goes to `publish/`.
2. Version bump: bump the version string, add a `CHANGELOG.md` section,
   update the version line in `README.md`.
3. Binary-only update (preserves config) — upload `publish/KTPFileDistributor`
   to `/tmp/` on the data server, then:
   ```bash
   sudo systemctl stop ktp-file-distributor
   sudo cp /tmp/KTPFileDistributor /opt/ktp-file-distributor/
   sudo chmod +x /opt/ktp-file-distributor/KTPFileDistributor
   sudo systemctl start ktp-file-distributor
   sudo systemctl status ktp-file-distributor
   ```
   Fresh install (new host or full redeploy) uses `install.sh` — see
   `SYNC_NEW_SERVER.md`.
4. Post-deploy: `sudo journalctl -u ktp-file-distributor -f` and confirm a
   startup Discord notification lands, then trigger a small test file change
   and confirm the distribution embed reports all servers, not just some.

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
  either — a stop mid-batch can drop an in-flight retry silently (see the
  delete/upload asymmetry below); prefer letting a batch finish first.
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
- **Renames leak remote copies.** `OnFileRenamed` in `FileWatcherWorker.cs`
  only turns the *new* path into a `FileChangeEvent`; the old path is logged
  and then dropped. `ChangeDebouncer` keys purely on `RelativePath`, so the
  old filename never gets an entry either. Net effect: renaming a file in the
  watch directory leaves the old copy sitting on every fleet server and
  FastDL forever — there is no cleanup path today. If you add rename cleanup,
  it needs a *second* `FileChangeEvent` for `GetRelativePath(e.OldFullPath)`
  with `ChangeType = Deleted`, alongside the new-name event.
- **One bad server can blank out a whole batch's result.** In
  `UploadToServerAsync`, `CreateSftpClient(server)` runs before the retry
  loop's try/catch, so a bad or missing `privateKeyPath` throws past all
  per-server isolation. That fault propagates through `Task.WhenAll` in
  `DistributeAsync` and skips `result.ServerResults = ...` entirely — the
  whole batch loses its Discord notification and per-server results, even for
  servers that already finished successfully. Keep any future per-server
  construction inside that server's own try/catch.
- **Delete failures lie about success.** `DeleteFileAsync` catches its own
  exceptions and only logs a warning; it never fails the batch the way
  `UploadFileAsync` does. A remote permission error on one delete still
  reports `Success = true` for that server, and the failed delete is never
  retried. Don't mirror this in new code — let failures propagate like
  uploads do.
- `ChangeDebouncer.PendingCount` is unread anywhere in the codebase; it's
  dead API surface, not a bug — leave it or remove it, don't be alarmed by it.

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

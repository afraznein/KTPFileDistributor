# KTP File Distributor

**Version 1.1.4** - A .NET 8 Worker Service that monitors a directory for file changes and automatically distributes them to multiple game servers via SFTP.

## Features

- **File System Monitoring**: Watches a configured directory for file changes (create, modify, rename, delete)
- **Debounced Uploads**: Batches rapid file changes to avoid redundant uploads
- **Parallel Distribution**: Uploads to multiple servers simultaneously with configurable concurrency
- **SFTP Support**: Secure file transfer with SSH.NET (password or private key authentication)
- **Discord Notifications**: Reports distribution results via Discord webhook relay
- **Retry Logic**: Automatic retries for failed uploads
- **Structured Logging**: Serilog-based logging to console and rolling log files
- **systemd Integration**: Native support for running as a Linux systemd service

## Supported File Types

- `.amxx` - AMX Mod X plugins
- `.bsp` - Map files
- `.txt` - Text/configuration files
- `.bmp` - Spray images
- `.cfg` - Config files
- `.wad` - Texture files
- `.res` - Resource files
- `.mdl` - Model files
- `.wav` - Sound files

## Requirements

- .NET 8 Runtime (or self-contained deployment)
- Ubuntu 24.04 LTS (or other Linux distribution)
- SSH/SFTP access to target game servers

## Installation

### Building

On Windows, run the build script to create a self-contained Linux deployment:

```powershell
.\build-linux.ps1
```

This creates a `publish` folder with all required files.

### Deploying to Linux

1. Copy the `publish` folder contents to your Linux server
2. Run the installation script:

```bash
chmod +x install.sh
sudo ./install.sh
```

### Configuration

#### appsettings.json

```json
{
  "AppSettings": {
    "WatchDirectory": "/home/dod/distribute",
    "WatchPatterns": ["*.amxx", "*.bsp", "*.txt", "*.bmp", "*.cfg", "*.wad", "*.res", "*.mdl", "*.wav"],
    "IncludeSubdirectories": true,
    "DebounceDelayMs": 5000,
    "MaxConcurrentUploads": 5,
    "UploadRetryCount": 3,
    "RetryDelayMs": 2000,
    "ConnectionTimeoutSeconds": 30
  },
  "Discord": {
    "Enabled": true,
    "RelayUrl": "http://your-relay:3000/reply",
    "AuthSecret": "your-secret",
    "ChannelId": "your-channel-id",
    "AdditionalChannelIds": ["second-channel-id"],
    "NotifyOnSuccess": true,
    "NotifyOnFailure": true
  }
}
```

`WatchPatterns` is the only gate — a file whose extension isn't listed is dropped before
it reaches the debouncer. An empty list, or the literal `"*.*"` (the compiled-in default),
matches everything.

#### servers.json

Create a `servers.json` file in the application directory — `/opt/ktp-file-distributor/servers.json`
for the standard install, otherwise alongside the binary. If it's missing the service does **not**
fail: it logs a warning, writes an example file to that path, and starts with **no** distribution
targets, which otherwise reads as a healthy startup.

```json
[
  {
    "name": "Dallas 1",
    "host": "192.168.1.100",
    "port": 22,
    "username": "dod",
    "password": "your-password",
    "remoteBasePath": "/home/dod/server/dod",
    "enabled": true
  },
  {
    "name": "Chicago 1",
    "host": "192.168.1.101",
    "port": 22,
    "username": "dod",
    "privateKeyPath": "/home/dod/.ssh/id_rsa",
    "privateKeyPassphrase": "optional-key-passphrase",
    "remoteBasePath": "/srv/dod",
    "enabled": true
  }
]
```

**Server Configuration Options:**

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Display name for logging and Discord notifications |
| `host` | Yes | SFTP hostname or IP address |
| `port` | No | SFTP port (default: 22) |
| `username` | Yes | SFTP username |
| `password` | No* | Password for password authentication |
| `privateKeyPath` | No* | Path to SSH private key for key authentication |
| `privateKeyPassphrase` | No | Passphrase for encrypted private keys |
| `remoteBasePath` | Yes | Base path on server where files are uploaded |
| `enabled` | No | Whether to include this server (default: true) |

*Either `password` or `privateKeyPath` must be provided.

#### FastDL target — the `dod/` path rule

**The FastDL entry's `remoteBasePath` must end in `/dod`** (i.e. `/var/www/fastdl/dod`).
The game engine appends `dod/` to every asset request it makes, so a `remoteBasePath`
of `/var/www/fastdl` puts files on disk that **every client 404s on** — and the upload
genuinely succeeded, so the logs and the Discord embed both report green SUCCESS.
Nothing distinguishes this from a working deploy. This has bitten twice in production
(`xrain2.spr` and `flare1.spr`, both 2026-05).

The canonical asset path is `/var/www/fastdl/dod/<game-relative-path>`. So a server
asset at `serverfiles/dod/sprites/foo.spr` must land at `/var/www/fastdl/dod/sprites/foo.spr`,
**not** `/var/www/fastdl/sprites/foo.spr`.

Verify any newly-distributed asset (expect `200`):

```bash
curl -sI http://<DATA_SERVER_IP>/dod/<game-relative-path>
```

Sanity check the FastDL root — it should contain only `dod/`, `demos/` (the HLTV portal
alias), and `cstrike/` if applicable. Anything else at the top level is a misdeploy:

```bash
find /var/www/fastdl -maxdepth 1 -type d
```

**Path traversal:** relative paths that escape the watch directory (containing `../`)
are rejected outright and fail that server's batch with a `Relative path contains
traversal:` message.

## Usage

### Managing the Service

```bash
# Start the service
sudo systemctl start ktp-file-distributor

# Stop the service
sudo systemctl stop ktp-file-distributor

# Check status
sudo systemctl status ktp-file-distributor

# View logs
sudo journalctl -u ktp-file-distributor -f

# View application logs
tail -f /opt/ktp-file-distributor/logs/distributor-*.log
```

### How It Works

1. Place files in the watch directory (configured value: `/home/dod/distribute`; the compiled-in default, used only if `appsettings.json` omits the key, is `/srv/ktp/sync`)
2. The service detects changes and waits for the debounce period (default: 5 seconds)
3. Multiple changes to the same file are deduplicated (only the latest version is uploaded)
4. After the quiet period, all changed files are uploaded to all enabled servers in parallel
5. A Discord notification is sent with the distribution results

### Automatic Features

- **Watch directory creation**: If the watch directory doesn't exist, it's automatically created
- **FileSystemWatcher recovery**: If the file watcher encounters an error, it automatically restarts
- **File deletion sync**: When files are deleted in the watch directory, they're also deleted on remote servers. Delete failures are logged as warnings only — they are **not** retried and do **not** mark the server as failed, so confirm removals in the log rather than from the Discord embed (uploads do not behave this way; they propagate into the retry loop).
- **Rename handling**: Renaming a watched file uploads it under the new name and deletes the old remote copy. Renaming to a non-watched extension deletes the remote copy without re-uploading. Both are destructive remote side effects — a rename in the watch directory removes the old file from every fleet server and FastDL.
- **Remote directory creation**: Remote directories are automatically created as needed during upload
- **Startup/shutdown notifications**: Discord notifications are sent when the service starts and stops

### Directory Structure

Files in the watch directory are uploaded to the same relative path on each server. For example:

```
Watch: /home/dod/distribute/addons/ktpamx/plugins/myplugin.amxx
Remote: /home/dod/server/dod/addons/ktpamx/plugins/myplugin.amxx
```

See [CHANGELOG.md](CHANGELOG.md) for version history.

## License

MIT License - See LICENSE file for details.

## Author

**Nein_** ([@afraznein](https://github.com/afraznein))

Part of the [KTP Competitive Infrastructure](https://github.com/afraznein).

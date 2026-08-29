# KTPFileDistributor - Claude Code Context

> **IPs here are placeholders** — this repo is public. Real addresses resolve in
> the private root context (`KTP Git Projects/CLAUDE.md` § IP Addresses),
> which is deliberately not in any git repository.

**REQUIRED: Before modifying, building, or deploying this service, invoke the `service-dev` skill** (`.claude/skills/service-dev/SKILL.md`). It carries the SFTP fan-out gaps you must not reintroduce, the FastDL path rule, and the build/deploy/verify checklist; do not edit the Services code without it loaded.

## Overview
.NET 8 Worker Service that monitors a directory for file changes and distributes them to multiple destinations via SFTP.

## Build Command
```powershell
.\build-linux.ps1
```
Output goes to `publish/` folder.

## Deployment
Files are deployed to `/opt/ktp-file-distributor/` on the data server (<DATA_SERVER_IP>).

### Update Procedure (binary only, preserves config)
1. Upload `publish/KTPFileDistributor` to `/tmp/` on data server via SFTP
2. SSH into data server and run:
```bash
sudo systemctl stop ktp-file-distributor
sudo cp /tmp/KTPFileDistributor /opt/ktp-file-distributor/
sudo chmod +x /opt/ktp-file-distributor/KTPFileDistributor
sudo systemctl start ktp-file-distributor
sudo systemctl status ktp-file-distributor
```

### Fresh Install
1. Upload entire `publish/` folder contents to `/tmp/ktp-file-distributor/` on data server
2. SSH in and run:
```bash
cd /tmp/ktp-file-distributor
chmod +x install.sh
sudo ./install.sh
```
3. Edit `/opt/ktp-file-distributor/appsettings.json` and `/opt/ktp-file-distributor/servers.json` with actual values

## Configuration Files (on data server)

### /opt/ktp-file-distributor/appsettings.json
```json
{
  "AppSettings": {
    "WatchDirectory": "/home/dod/distribute",
    "WatchPatterns": ["*.amxx", "*.bsp", "*.txt", "*.bmp", "*.cfg", "*.wad", "*.res", "*.mdl", "*.wav", "*.ini"],
    "IncludeSubdirectories": true,
    "DebounceDelayMs": 5000,
    "MaxConcurrentUploads": 5
  },
  "Discord": {
    "Enabled": true,
    "RelayUrl": "https://your-relay/reply",
    "AuthSecret": "your-secret",
    "ChannelId": "primary-channel-id",
    "AdditionalChannelIds": ["second-channel-id", "third-channel-id"]
  }
}
```

### /opt/ktp-file-distributor/servers.json
```json
[
  {
    "name": "KTP - Atlanta 1",
    "host": "<ATL_BM_GAME_IP>",
    "port": 22,
    "username": "dodserver",
    "privateKeyPath": "<DISTRIBUTOR_KEY_PATH>",
    "remoteBasePath": "/home/dodserver/dod-27015/serverfiles/dod",
    "enabled": true
  },
  {
    "name": "FastDL (Data Server)",
    "host": "localhost",
    "port": 22,
    "username": "root",
    "privateKeyPath": "<DISTRIBUTOR_KEY_PATH>",
    "remoteBasePath": "/var/www/fastdl/dod",
    "enabled": true
  }
]
```

> **`<DISTRIBUTOR_KEY_PATH>` is a placeholder** — the real path is whatever the live
> `servers.json` on the data server says. It is deliberately not written here: this repo is
> public, and the key must live **outside every web- or FTP-served directory** (docroots,
> FTP chroots, anything a vhost or `vsftpd` can reach). A deploy key readable through a
> served path grants the whole fleet to anyone who can fetch it.

## Service Management
```bash
sudo systemctl start ktp-file-distributor
sudo systemctl stop ktp-file-distributor
sudo systemctl status ktp-file-distributor
sudo journalctl -u ktp-file-distributor -f
```

## How It Works
1. Place files in watch directory (e.g., `/home/dod/distribute/addons/ktpamx/plugins/MyPlugin.amxx`)
2. Service detects changes, waits 5 seconds for debounce
3. Uploads to ALL enabled servers in parallel
4. Sends Discord notification with results

## SSH Access

For data server deployment/management, use Python/Paramiko:

**Server Credentials:**
| Server | Host | User | Password |
|--------|------|------|----------|
| Data Server | <DATA_SERVER_IP> | root | (SSH key auth) |

See `N:\Nein_\KTP Git Projects\CLAUDE.md` for paramiko SSH documentation.

## Related
- Game Servers: Atlanta (<ATL_BM_GAME_IP>), Dallas (<DAL_GAME_IP>), Denver (<DEN_GAME_IP>), New York (<NYC_GAME_IP>)
- Data Server: <DATA_SERVER_IP>
- FastDL URL: http://<DATA_SERVER_IP>/dod/

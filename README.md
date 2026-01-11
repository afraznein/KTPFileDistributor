# KTP File Distributor

A .NET 8 Worker Service that monitors a directory for file changes and automatically distributes them to multiple game servers via SFTP.

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
    "WatchPatterns": ["*.amxx", "*.bsp", "*.txt", "*.bmp", "*.cfg", "*.wad"],
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

#### servers.json

Create a `servers.json` file with your target servers:

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
    "remoteBasePath": "/srv/dod",
    "enabled": true
  }
]
```

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

1. Place files in the watch directory (default: `/home/dod/distribute`)
2. The service detects changes and waits for the debounce period (default: 5 seconds)
3. After the quiet period, all changed files are uploaded to all enabled servers in parallel
4. A Discord notification is sent with the distribution results

### Directory Structure

Files in the watch directory are uploaded to the same relative path on each server. For example:

```
Watch: /home/dod/distribute/addons/amxmodx/plugins/myplugin.amxx
Remote: /home/dod/server/dod/addons/amxmodx/plugins/myplugin.amxx
```

## License

MIT License - See LICENSE file for details.

## Author

**Nein_** ([@afraznein](https://github.com/afraznein))

Part of the [KTP Competitive Infrastructure](https://github.com/afraznein).

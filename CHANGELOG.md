# Changelog

All notable changes to KTP File Distributor will be documented in this file.

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

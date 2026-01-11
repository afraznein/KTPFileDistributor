# Changelog

All notable changes to KTP File Distributor will be documented in this file.

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

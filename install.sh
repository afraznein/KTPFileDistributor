#!/bin/bash
# KTP File Distributor Installation Script for Ubuntu 24

set -e

INSTALL_DIR="/opt/ktp-file-distributor"
SERVICE_USER="dod"
SERVICE_GROUP="dod"
WATCH_DIR="/home/dod/distribute"

echo "=== KTP File Distributor Installation ==="

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "Please run as root (sudo)"
    exit 1
fi

# Install .NET 8 runtime if not present
if ! command -v dotnet &> /dev/null; then
    echo "Installing .NET 8 runtime..."
    apt-get update
    apt-get install -y dotnet-runtime-8.0
fi

# Create installation directory
echo "Creating installation directory..."
mkdir -p "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR/logs"

# Copy files (assumes published files are in current directory)
echo "Copying application files..."
cp -r ./publish/* "$INSTALL_DIR/"

# Create watch directory if it doesn't exist
echo "Creating watch directory..."
mkdir -p "$WATCH_DIR"
chown "$SERVICE_USER:$SERVICE_GROUP" "$WATCH_DIR"

# Set permissions
echo "Setting permissions..."
chown -R "$SERVICE_USER:$SERVICE_GROUP" "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/KTPFileDistributor"

# Install systemd service
echo "Installing systemd service..."
cp "$INSTALL_DIR/ktp-file-distributor.service" /etc/systemd/system/
systemctl daemon-reload

# Enable and start service
echo "Enabling service..."
systemctl enable ktp-file-distributor

echo ""
echo "=== Installation Complete ==="
echo ""
echo "Next steps:"
echo "1. Edit configuration: nano $INSTALL_DIR/appsettings.json"
echo "2. Edit server list: nano $INSTALL_DIR/servers.json"
echo "3. Start service: sudo systemctl start ktp-file-distributor"
echo "4. Check status: sudo systemctl status ktp-file-distributor"
echo "5. View logs: sudo journalctl -u ktp-file-distributor -f"
echo ""
echo "Watch directory: $WATCH_DIR"
echo "Logs directory: $INSTALL_DIR/logs"

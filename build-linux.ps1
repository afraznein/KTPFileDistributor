# Build KTP File Distributor for Linux x64
# Run this on Windows to create a self-contained Linux deployment package

$ErrorActionPreference = "Stop"
$ProjectDir = "$PSScriptRoot\KTPFileDistributor"
$PublishDir = "$PSScriptRoot\publish"

Write-Host "=== Building KTP File Distributor for Linux x64 ===" -ForegroundColor Cyan

# Clean previous build
if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
}

# Publish for Linux x64 (self-contained)
Write-Host "Publishing for Linux x64..." -ForegroundColor Yellow
dotnet publish $ProjectDir `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained true `
    --output $PublishDir `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:EnableCompressionInSingleFile=true

# Copy service file
Write-Host "Copying service file..." -ForegroundColor Yellow
Copy-Item "$ProjectDir\ktp-file-distributor.service" $PublishDir

# Copy install script
Write-Host "Copying install script..." -ForegroundColor Yellow
Copy-Item "$PSScriptRoot\install.sh" $PublishDir

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Output: $PublishDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "To deploy:" -ForegroundColor Yellow
Write-Host "1. Copy the 'publish' folder contents to your Linux server"
Write-Host "2. Run: chmod +x install.sh && sudo ./install.sh"

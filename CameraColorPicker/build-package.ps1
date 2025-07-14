# Build and Package Script for Camera Color Picker
# Run this on Windows to create deployment package for Raspberry Pi

$ErrorActionPreference = "Stop"

Write-Host "Camera Color Picker - Build Script" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green

# Clean previous builds
Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "bin") { Remove-Item -Path "bin" -Recurse -Force }
if (Test-Path "obj") { Remove-Item -Path "obj" -Recurse -Force }
if (Test-Path "publish") { Remove-Item -Path "publish" -Recurse -Force }
if (Test-Path "camera-color-picker-deploy.tar.gz") { Remove-Item "camera-color-picker-deploy.tar.gz" -Force }

# Restore packages
Write-Host "`nRestoring NuGet packages..." -ForegroundColor Yellow
dotnet restore

# Build for Linux ARM64
Write-Host "`nBuilding for Linux ARM64..." -ForegroundColor Yellow
dotnet publish -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o publish/

# Check if build succeeded
if (-not (Test-Path "publish/CameraColorPicker")) {
    Write-Host "Build failed! Executable not found." -ForegroundColor Red
    exit 1
}

# Copy deployment files
Write-Host "`nCopying deployment files..." -ForegroundColor Yellow
Copy-Item -Path "deployment/*" -Destination "publish/" -Recurse

# Create deployment package
Write-Host "`nCreating deployment package..." -ForegroundColor Yellow
Push-Location publish
try {
    # Use tar to create Linux-compatible archive
    tar -czf ../camera-color-picker-deploy.tar.gz *
}
finally {
    Pop-Location
}

# Verify package
if (Test-Path "camera-color-picker-deploy.tar.gz") {
    $size = (Get-Item "camera-color-picker-deploy.tar.gz").Length / 1MB
    Write-Host "`nPackage created successfully!" -ForegroundColor Green
    Write-Host "File: camera-color-picker-deploy.tar.gz" -ForegroundColor Cyan
    Write-Host "Size: $([math]::Round($size, 2)) MB" -ForegroundColor Cyan
    Write-Host "`nDeployment Instructions:" -ForegroundColor Yellow
    Write-Host "1. Copy camera-color-picker-deploy.tar.gz to Raspberry Pi"
    Write-Host "2. Extract: tar -xzf camera-color-picker-deploy.tar.gz"
    Write-Host "3. Install: sudo ./install.sh"
    Write-Host "4. The application will start automatically on boot"
}
else {
    Write-Host "Failed to create package!" -ForegroundColor Red
    exit 1
} 
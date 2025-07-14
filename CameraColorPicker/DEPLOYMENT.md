# Camera Color Picker - Deployment Guide

## Prerequisites

- Raspberry Pi 5 with ARM64 OS
- USB Camera connected
- 7-inch display connected
- SD card with Raspberry Pi OS 64-bit

## Deployment Steps

### 1. Build Package (Windows)

```powershell
cd CameraColorPicker
powershell -ExecutionPolicy Bypass -File .\build-package.ps1
```

This will create `camera-color-picker-deploy.tar.gz` (approximately 11MB).

### 2. Transfer to Raspberry Pi

Copy the deployment package to your Raspberry Pi using USB drive or SD card.

### 3. Install on Raspberry Pi

1. Open terminal on Raspberry Pi
2. Navigate to the directory containing the package
3. Extract the package:
   ```bash
   tar -xzf camera-color-picker-deploy.tar.gz
   ```

4. Run installation script with sudo:
   ```bash
   sudo ./install.sh
   ```

### 4. Verify Installation

Check service status:
```bash
sudo systemctl status camera-color-picker
```

The application should start automatically on next boot.

## Manual Control

Start application:
```bash
sudo systemctl start camera-color-picker
```

Stop application:
```bash
sudo systemctl stop camera-color-picker
```

View logs:
```bash
sudo journalctl -u camera-color-picker -f
```

## Troubleshooting

### Camera Not Found
- Check USB camera connection
- Verify camera is detected: `ls /dev/video*`
- Check permissions: `ls -l /dev/video0`

### Display Issues
- Ensure DISPLAY environment variable is set
- Check X server is running
- Verify user 'pi' has display access

### Service Won't Start
- Check logs: `sudo journalctl -u camera-color-picker -n 50`
- Verify installation directory: `ls -la /opt/camera-color-picker/`
- Check executable permissions: `ls -l /opt/camera-color-picker/CameraColorPicker`

## Uninstallation

To remove the application:
```bash
sudo /opt/camera-color-picker/uninstall.sh
``` 
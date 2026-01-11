# Installation Guide

Setup instructions for CheapShotcutRandomizer dependencies.

> **Note:** AI upscaling and frame interpolation features have been moved to [CheapUpscaler](https://github.com/CheapNud/CheapUpscaler).

---

## Quick Start: Automated Dependency Manager

**The application includes a built-in Dependency Manager that automates installation and verification of all required dependencies.**

### Using the Dependency Manager (Recommended)

1. **Launch the Application**
2. **Navigate to Dependency Manager** (from the main menu)
3. **Review Dependency Status** - The manager automatically detects installed dependencies
4. **Install Missing Dependencies** - Click "Install Missing" or install individual dependencies
5. **Verify Installation** - Click "Refresh Status" to re-check after installation

The Dependency Manager handles:
- Automatic detection of installed tools (FFmpeg, FFprobe, Melt)
- Guided installation with multiple strategies (Chocolatey, portable, installer)
- Real-time verification of dependency versions and compatibility
- Integration with existing installations (detects Shotcut)

**For advanced users or manual installation, see the detailed instructions below.**

---

## Required Dependencies

### FFmpeg (Required)

Video encoding and decoding.

**Automated Installation:**
- Navigate to Dependency Manager → Check "FFmpeg" status
- Follow automated installation instructions

**Manual Installation:**
- Download from: https://ffmpeg.org/download.html
- Add to system PATH
- Verify: `ffmpeg -version`

### FFprobe (Required)

Video analysis (bundled with FFmpeg).

**Automated Installation:**
- Installed with FFmpeg via Dependency Manager

**Manual Installation:**
- Bundled with FFmpeg download
- Verify: `ffprobe -version`

### Melt (Required)

Shotcut/MLT project rendering.

**Automated Installation:**
- Navigate to Dependency Manager → Check "Melt" status
- Follow automated installation instructions

**Manual Installation:**
- Install [Shotcut](https://shotcut.org/download/) (includes melt)
- Or install MLT framework directly
- Add to system PATH
- Verify: `melt --version`

---

## Troubleshooting

**First Step: Use the Dependency Manager**
- Navigate to **Dependency Manager** in the application
- Click "Refresh Status" to re-check all dependencies
- Review the status and error messages for each dependency

### "FFmpeg not found"

**Via Dependency Manager:**
- Check "FFmpeg" status in Dependency Manager
- Follow installation instructions if FFmpeg is missing

**Manual Solution:**
- Reinstall FFmpeg
- Ensure FFmpeg installation directory is in PATH
- Restart application after installation

### "melt not found"

**Via Dependency Manager:**
- Check "Melt" status in Dependency Manager
- Follow installation instructions if Melt is missing

**Manual Solution:**
- Install Shotcut (includes melt)
- Add Shotcut installation directory to PATH
- Typical path: `C:\Program Files\Shotcut\`
- Verify: `melt --version`

---

## Links

- **FFmpeg:** https://ffmpeg.org
- **Shotcut:** https://shotcut.org
- **MLT Framework:** https://www.mltframework.org

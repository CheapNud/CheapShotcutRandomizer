# Cheap Shotcut Randomizer

Desktop app for randomizing and generating optimized Shotcut video project playlists using simulated annealing algorithms.

> **Note:** AI upscaling and frame interpolation features have been moved to [CheapUpscaler](https://github.com/CheapNud/CheapUpscaler).

## Features

### Playlist Management
- **Shuffle Playlists** - Randomly reorder clips with one click
- **Generate Smart Compilations** - Create optimized playlists from multiple sources
- **Advanced Controls** - Fine-tune selection with duration and clip count weights
- **Non-Destructive** - Original projects are never modified

### MLT/Shotcut Rendering
- **Melt Integration** - Render Shotcut projects directly via melt
- **Multi-track Support** - Select specific video and audio tracks for rendering
- **In/Out Points** - Render specific ranges using Shotcut markers
- **Background Queue** - Queue multiple render jobs with persistent SQLite storage
- **Crash Recovery** - Resume interrupted jobs automatically on startup

### Dependency Management
- **Auto-Detection** - Automatic detection of FFmpeg, FFprobe, and Melt
- **SVP Integration** - Detects SVP's bundled FFmpeg for optimal encoding
- **Installation Options** - Chocolatey, portable, or manual installation
- **First-Run Wizard** - Guided setup on first launch

## Usage

### Playlist Randomization
1. **Load Project** - Select your `.mlt` Shotcut project file
2. **Shuffle** - Click shuffle button next to any playlist, or
3. **Generate Compilation**:
   - Check playlists to include
   - Adjust weights (optional):
     - Duration Weight: 0-20 (higher = prefer shorter clips, 4 = recommended)
     - Number of Videos Weight: 0-5 (higher = more clips, 0.8 = recommended)
   - Set target duration per playlist with slider (0 = use all)
   - Click "Generate Random Playlist"

Output files: `OriginalName.Random[####].mlt`

### Video Rendering
1. **Open Render Queue** - Navigate to Render Queue page
2. **Add Job** - Click "Add Render Job"
3. **Select Source** - Choose MLT project file
4. **Configure Settings** - Codec, CRF, preset, track selection
5. **Add to Queue** - Job processes automatically in background

## Algorithm

Uses simulated annealing optimization to select the best combination of clips based on:
- Target duration constraints
- Duration weight preferences
- Number of videos weight preferences

## Building

### Prerequisites
- .NET 10.0 SDK

### Build
```bash
dotnet build
```

### Publish Single Executable
```bash
dotnet publish -c Release -r win-x64
```

Output: `bin/Release/net10.0/win-x64/publish/CheapShotcutRandomizer.exe`

This creates a self-contained single-file executable (~90MB) that includes the .NET runtime.

## Requirements

- Windows 10/11
- FFmpeg (video encoding)
- Melt (Shotcut rendering) - Install [Shotcut](https://shotcut.org/download/)

## Tech Stack

- Blazor Server + Avalonia ([CheapAvaloniaBlazor](https://github.com/CheapNud/CheapAvaloniaBlazor))
- MudBlazor UI components
- Entity Framework Core + SQLite (job persistence)
- FFmpeg (video encoding/decoding)
- CheapHelpers (utilities)

## Related Projects

- **[CheapUpscaler](https://github.com/CheapNud/CheapUpscaler)** - AI upscaling (Real-ESRGAN, Real-CUGAN) and frame interpolation (RIFE)
- **[CheapAvaloniaBlazor](https://github.com/CheapNud/CheapAvaloniaBlazor)** - Cross-platform Blazor desktop framework
- **[CheapHelpers](https://github.com/CheapNud/CheapHelpers)** - Shared utility library

---

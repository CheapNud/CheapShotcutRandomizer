# Cheap Shotcut Randomizer

Desktop app that shuffles and generates randomized playlists from Shotcut projects, then batch-renders them through melt with a full render queue.

> **Note:** AI upscaling and frame interpolation features live in [CheapUpscaler](https://github.com/CheapNud/CheapUpscaler).

## Features

### Randomizer
- **Shuffle playlists** — randomly reorder clips per playlist, optionally avoiding consecutive clips from the same source
- **Generate smart compilations** — simulated-annealing selection from multiple source playlists with duration and clip-count weights, plus a per-playlist target duration
- **Non-destructive** — generated projects are written as separate temp files; your source project is never modified

### Render Queue
- **Queue-first workflow** — the queue is the home page; a full-page stepper (Source → Video → Audio → Tracks & Range → Review) configures each job
- **Encoder selection** — x264/x265 on CPU, or your detected hardware encoders (NVENC/QSV/AMF) with the correct per-encoder quality flags
- **Stock export presets** — the MLT/Shotcut preset library (YouTube, HEVC, ProRes, …) found next to your melt install, searchable with recommended picks on top
- **Output overrides** — resolution, aspect ratio, frame rate, and audio quality-based VBR
- **True pause/resume** — pausing a render suspends the melt process in place; resuming continues without losing progress
- **Pre-flight checks** — missing media, duplicate output targets, and low disk space are caught before the render starts
- **Post-completion actions** — move the output to a folder or reveal it in Explorer when a job finishes
- **Reliability** — persistent SQLite queue, crash recovery, safe-mode retry, keep-awake during renders

### Find Files
- Search clips across playlists by filename, spot duplicates, and jump to every occurrence on the timeline

## Usage

1. **Randomizer** — load your `.mlt` project, shuffle a playlist or generate a compilation; both hand off to the render stepper with the generated track pre-selected
2. **Add Files** on the Render Queue page opens the same stepper for any `.mlt` project
3. **Start Queue** — jobs process in the background with live progress, ETA, and per-job actions

Generated projects are stored in the app's temp folder and cleaned up automatically once their render finishes.

## Requirements

- Windows 10/11
- [Shotcut](https://shotcut.org/download/) (provides melt and the export preset library)
- FFmpeg/FFprobe (bundled with Shotcut and SVP; auto-detected, or set paths in Settings)

## Building

Prerequisites: .NET 11 SDK

```bash
dotnet build
dotnet test
```

Publish a self-contained single-file build (~90 MB, no .NET needed on the target machine):

```powershell
.\deploy\publish.ps1 -Version 1.0.0
```

Output: `deploy\out\CheapShotcutRandomizer\CheapShotcutRandomizer.exe`

## Tech Stack

- Blazor + Avalonia desktop via [CheapAvaloniaBlazor](https://github.com/CheapNud/CheapAvaloniaBlazor)
- MudBlazor UI
- Entity Framework Core + SQLite (render queue persistence)
- melt/MLT (rendering), invoked with Shotcut-parity consumer properties
- [CheapHelpers](https://github.com/CheapNud/CheapHelpers) (utilities, media processing)

## Related Projects

- **[CheapUpscaler](https://github.com/CheapNud/CheapUpscaler)** — AI upscaling (Real-ESRGAN, Real-CUGAN) and frame interpolation (RIFE)
- **[CheapAvaloniaBlazor](https://github.com/CheapNud/CheapAvaloniaBlazor)** — Blazor desktop framework
- **[CheapHelpers](https://github.com/CheapNud/CheapHelpers)** — shared utility library

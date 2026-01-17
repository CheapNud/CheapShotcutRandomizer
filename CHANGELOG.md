# Changelog

All notable changes to the Cheap Shotcut Randomizer project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Documentation Consolidation**: Reorganized all documentation into modular structure
  - Created `docs/features/` directory with dedicated docs for RIFE, Real-CUGAN, Real-ESRGAN, Non-AI Upscaling, and VapourSynth
  - Created `docs/architecture/` directory for implementation details
  - Consolidated GitHub Actions workflows into single comprehensive guide (`.github/WORKFLOWS.md`)
  - Enhanced `docs/README.md` as central documentation hub
- **VapourSynthEnvironment Service**: Centralized Python and VapourSynth detection
  - Automatic SVP Python detection with priority over system PATH
  - Consolidated environment management for all AI services (Real-CUGAN, Real-ESRGAN, RIFE)
  - Removed ~220+ lines of duplicate detection code
- **DebugLogger Utility**: Centralized logging with VerboseLogging flag support
  - `Log()`, `LogError()`, `LogWarning()` - Always log (errors, lifecycle events)
  - `LogVerbose()` - Only logs when VerboseLogging enabled (paths, progress, subprocess output)
  - Lazy initialization from settings file
- **DependencyChecker Service**: Comprehensive dependency detection and validation
  - Automatic detection of installed tools (FFmpeg, melt, VapourSynth, Python)
  - Real-time validation of dependency versions and compatibility
  - Shows which Python installation is being used (SVP vs System PATH)
- **DependencyManager UI**: Interactive dependency management page
  - One-click installation for missing dependencies
  - Real-time status checking with detailed error messages
  - Integration with existing installations (Shotcut, SVP, etc.)
- **GitHub Actions CI/CD**: Complete workflow automation
  - Build, test, and quality checks on every push
  - CodeQL security scanning
  - Automated release builds with GitHub Releases
  - Code coverage reporting
  - Auto-labeling for pull requests
- **MudBlazor 8.10.0 Compatibility**: Fixed all MUD0002 analyzer warnings
  - Updated `MudStack` Direction attributes to use `Row` boolean property
  - Removed invalid `AlignItems` attribute from `MudGrid`

### Changed
- **Installation Documentation**: Updated all docs to reference DependencyManager as primary installation method
  - `docs/installation.md`: Condensed from 470 to 263 lines with links to feature docs
  - `REALCUGAN_INSTALLATION.md`: Moved to `docs/features/real-cugan.md`
  - Manual installation instructions relabeled as "Advanced/Manual Installation"
  - SVP Python auto-detection highlighted throughout
- **Real-CUGAN Color Space Fix**: Added YUV420P16 conversion to prevent Y4M format errors
  - Fixed "can only apply y4m headers to YUV and Gray format clips" error
  - Proper Rec. 709 matrix for HD/1080p+ content
- **AI Services Refactoring**: Updated RealCuganService, RealEsrganService to use VapourSynthEnvironment
  - Dependency injection for environment detection
  - Consistent Python/vspipe detection across all services
- **RenderQueueService**: Fixed service instantiation to use DI container
  - Replaced `new RealEsrganService()` with `GetRequiredService<>`
  - Replaced `new RealCuganService()` with `GetRequiredService<>`
  - Proper dependency injection for VapourSynthEnvironment

### Removed
- **Duplicate Documentation**:
  - `REALCUGAN_INSTALLATION.md` (root) - merged into `docs/features/real-cugan.md`
  - `NON_AI_UPSCALING_IMPLEMENTATION.md` (root) - split into user/dev docs
  - `CheapShotcutRandomizer.Tests/SETUP_COMPLETE.md` - historical artifact
  - `.github/QUICK_START.md` - merged into `.github/WORKFLOWS.md`
  - `.github/WORKFLOW_SUMMARY.md` - merged into `.github/WORKFLOWS.md`
- **Duplicate Code**: Removed ~220+ lines of duplicate Python/vspipe detection across AI services

### Fixed
- **Build Warnings**: Resolved all MudBlazor MUD0002 analyzer warnings (9 total)
- **Real-CUGAN Y4M Error**: Fixed color space conversion issue for vspipe output
- **Python Detection Chaos**: Unified Python detection logic with clear SVP priority
- **Filename Length Issues**: Shortened GUID usage from 32 to 8 characters
- **Track Selection Bugs**: Fixed audio/track selection when shuffling/generating playlists
- **Clear All Jobs**: Added complete functionality with confirmation dialog

## [Previous Commits]

### [2025-01-XX] - TensorRT and Real-CUGAN Updates
- Updated TensorRT and Real-CUGAN installation logic
- Reset database schema

### [2025-01-XX] - Dependency Management
- Added Dependency Management and AI Upscaling Services
- Implemented comprehensive dependency checking

### [2025-01-XX] - RIFE Integration
- WORKING RIFE implementation
- Enhanced app with settings, dark mode, and SVP integration
- Added robust video rendering pipeline with RIFE support

### [2025-01-XX] - Initial Features
- Added file search and avoid consecutive clips feature
- Enhanced playlist UI and refactored XML handling
- Refactored namespaces and improved project structure
- Initial project files and setup

---

## Migration Notes

### Documentation Structure Changes

**Before**:
```
/
├── README.md
├── REALCUGAN_INSTALLATION.md
├── NON_AI_UPSCALING_IMPLEMENTATION.md
├── GRACEFUL_SHUTDOWN.md
├── docs/
│   ├── README.md (minimal)
│   ├── installation.md (470 lines, all features)
│   ├── hardware.md
│   └── development.md
└── .github/
    ├── QUICK_START.md
    ├── WORKFLOWS_README.md
    └── WORKFLOW_SUMMARY.md
```

**After**:
```
/
├── README.md
├── CHANGELOG.md (NEW)
├── docs/
│   ├── README.md (enhanced index)
│   ├── installation.md (263 lines, references features/)
│   ├── hardware.md
│   ├── development.md
│   ├── features/ (NEW)
│   │   ├── rife.md
│   │   ├── real-cugan.md
│   │   ├── real-esrgan.md
│   │   ├── non-ai-upscaling.md
│   │   └── vapoursynth.md
│   └── architecture/ (NEW)
│       └── graceful-shutdown.md
└── .github/
    ├── WORKFLOWS.md (NEW - consolidated)
    └── archive/
        └── WORKFLOW_ENHANCEMENTS.md
```

### Service Architecture Changes

**VapourSynthEnvironment** is now the single source of truth for:
- Python path detection (SVP → System PATH)
- vspipe executable location
- Version information for both Python and VapourSynth

All AI services (RealCuganService, RealEsrganService, RifeInterpolationService) now use this centralized service instead of duplicate detection logic.

**DependencyChecker** now reports which Python installation is actually being used:
- "Python 3.11.5 (SVP's Python)" - when using SVP's bundled Python
- "Python 3.11.5 (System PATH)" - when using system Python

---

[Unreleased]: https://github.com/CheapNud/ShotcutRandomizer/compare/master...HEAD

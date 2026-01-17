# Linux Support Feasibility Plan

## Executive Summary

**Answer: YES, Linux support is absolutely feasible and should be a long-term goal.**

You chose CheapAvaloniaBlazor specifically for cross-platform support - this was the right decision. Linux support is achievable with **moderate effort** (15-25 days) and minimal ongoing maintenance.

---

## Why Linux Support Makes Sense

### 1. **UI Framework Already Supports It**
- ✅ **Avalonia** - Already cross-platform (that's why you chose it over MAUI!)
- ✅ **CheapAvaloniaBlazor** - Built specifically because MAUI doesn't support Linux
- You've already made the architectural decision to support Linux

### 2. **Core Dependencies Are Cross-Platform**
- ✅ **VapourSynth** - Works on Linux (available via apt/pacman)
- ✅ **FFmpeg** - Native Linux support
- ✅ **Python** - Native Linux support
- ✅ **RIFE/Real-CUGAN/Real-ESRGAN** - Python-based, work on Linux
- ✅ **Shotcut/melt (MLT)** - Cross-platform, works on Linux
- ✅ **CUDA** - Full Linux support (NVIDIA drivers)
- ✅ **TensorRT** - Full Linux support (NVIDIA)

### 3. **Linux Users Are Advanced**
- They can install dependencies manually
- They understand `nvidia-smi`, `apt-get`, `pacman`
- They don't need automatic Chocolatey-style installation
- Manual installation instructions in docs are acceptable

### 4. **SVP Is Optional**
- SVP is just a convenient dependency manager for Windows
- Linux users can install TensorRT, CUDA, Python manually
- Make SVP a Windows-only enhancement feature
- Practical-RIFE works everywhere (Python-based)

---

## Only Real Blocker: System.Management (WMI)

### What Uses WMI?
1. **HardwareDetectionService** - GPU/CPU detection
2. **DependencyInstaller** - Admin privilege check

### Linux Alternatives (All Available)

#### GPU Detection
```bash
# NVIDIA GPU detection
nvidia-smi --query-gpu=name --format=csv,noheader

# All GPU detection
lspci | grep -i vga

# AMD GPU detection
lspci | grep -i amd
```

#### CPU Detection
```bash
# CPU info
cat /proc/cpuinfo | grep "model name" | uniq

# Core count
nproc
```

#### Privilege Detection
```bash
# Check if running as root/sudo
id -u  # Returns 0 if root
```

**Conclusion**: All WMI functionality has Linux equivalents. Just needs platform abstraction.

---

## Implementation Plan

### Phase 1: Platform Abstraction (5-7 days)

#### Create Interface-Based Hardware Detection

```csharp
// New: Services/Hardware/IHardwareDetector.cs
public interface IHardwareDetector
{
    Task<HardwareCapabilities> DetectHardwareAsync();
    Task<List<string>> GetGpuNamesAsync();
    Task<bool> IsNvidiaGpuAvailableAsync();
    Task<string> GetCpuNameAsync();
    int GetCpuCoreCount();
}

// Windows implementation (existing code)
[SupportedOSPlatform("windows")]
public class WindowsHardwareDetector : IHardwareDetector
{
    // Use existing WMI code from HardwareDetectionService
}

// New: Linux implementation
[SupportedOSPlatform("linux")]
public class LinuxHardwareDetector : IHardwareDetector
{
    public async Task<bool> IsNvidiaGpuAvailableAsync()
    {
        // Run: nvidia-smi --list-gpus
        var result = await RunCommandAsync("nvidia-smi", "--list-gpus");
        return result.ExitCode == 0;
    }

    public async Task<string> GetCpuNameAsync()
    {
        // Run: cat /proc/cpuinfo | grep "model name" | head -1
        var result = await RunCommandAsync("cat", "/proc/cpuinfo");
        // Parse "model name" line
        return ParseCpuName(result.Output);
    }

    public int GetCpuCoreCount()
    {
        return Environment.ProcessorCount; // Works on Linux
    }
}
```

#### Register Platform-Specific Implementation

```csharp
// Program.cs
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    builder.Services.AddSingleton<IHardwareDetector, WindowsHardwareDetector>();
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.Services.AddSingleton<IHardwareDetector, LinuxHardwareDetector>();
}
else
{
    // Fallback: basic detection without WMI
    builder.Services.AddSingleton<IHardwareDetector, BasicHardwareDetector>();
}
```

---

### Phase 2: Dependency Installation Abstraction (3-5 days)

#### Make Installation Platform-Aware

```csharp
public interface IDependencyInstaller
{
    Task<InstallationResult> InstallFFmpegAsync();
    Task<InstallationResult> InstallVapourSynthAsync();
    Task<InstallationResult> InstallPythonAsync();
    bool CanAutoInstall(); // Returns false on Linux (manual install)
}

[SupportedOSPlatform("windows")]
public class WindowsDependencyInstaller : IDependencyInstaller
{
    // Existing Chocolatey implementation
    public bool CanAutoInstall() => true;
}

[SupportedOSPlatform("linux")]
public class LinuxDependencyInstaller : IDependencyInstaller
{
    public bool CanAutoInstall() => false; // Manual install

    public async Task<InstallationResult> InstallFFmpegAsync()
    {
        // Return instructions for manual installation
        return new InstallationResult
        {
            Success = false,
            Message = "Please install FFmpeg using your package manager:\n" +
                     "Ubuntu/Debian: sudo apt install ffmpeg\n" +
                     "Arch: sudo pacman -S ffmpeg\n" +
                     "Fedora: sudo dnf install ffmpeg"
        };
    }
}
```

---

### Phase 3: Path Detection (2-3 days)

#### Abstract Platform-Specific Paths

```csharp
public interface IPlatformPaths
{
    string[] GetFFmpegSearchPaths();
    string[] GetMeltSearchPaths();
    string[] GetVapourSynthSearchPaths();
}

[SupportedOSPlatform("windows")]
public class WindowsPaths : IPlatformPaths
{
    public string[] GetFFmpegSearchPaths() => new[]
    {
        @"C:\Program Files\Shotcut\ffmpeg.exe",
        @"C:\Program Files (x86)\SVP 4\utils\ffmpeg.exe",
        "ffmpeg" // PATH
    };
}

[SupportedOSPlatform("linux")]
public class LinuxPaths : IPlatformPaths
{
    public string[] GetFFmpegSearchPaths() => new[]
    {
        "/usr/bin/ffmpeg",
        "/usr/local/bin/ffmpeg",
        Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile), ".local/bin/ffmpeg"),
        "ffmpeg" // PATH
    };

    public string[] GetMeltSearchPaths() => new[]
    {
        "/usr/bin/melt",
        "/usr/local/bin/melt",
        "melt" // PATH
    };
}
```

---

### Phase 4: Update DependencyChecker (2-3 days)

#### Make Detection Cross-Platform

```csharp
public class DependencyChecker
{
    private readonly IHardwareDetector _hardwareDetector;
    private readonly IPlatformPaths _platformPaths;

    // Detection now works on both platforms
    private async Task<DependencyInfo> CheckFFmpegAsync()
    {
        foreach (var path in _platformPaths.GetFFmpegSearchPaths())
        {
            if (await IsExecutableAvailableAsync(path))
            {
                return new DependencyInfo { IsInstalled = true, Path = path };
            }
        }
        return new DependencyInfo { IsInstalled = false };
    }

    private async Task<bool> IsExecutableAvailableAsync(string path)
    {
        // Works on both Windows and Linux
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
```

---

### Phase 5: Update Documentation (2-3 days)

#### Platform-Specific Installation Guides

**docs/installation-linux.md**:
```markdown
# Installation Guide (Linux)

## Prerequisites

### 1. Install FFmpeg
```bash
# Ubuntu/Debian
sudo apt update && sudo apt install ffmpeg

# Arch Linux
sudo pacman -S ffmpeg

# Fedora
sudo dnf install ffmpeg
```

### 2. Install VapourSynth
```bash
# Ubuntu/Debian (R65+)
sudo add-apt-repository ppa:djcj/vapoursynth
sudo apt update && sudo apt install vapoursynth

# Arch Linux
sudo pacman -S vapoursynth
```

### 3. Install Python 3.8-3.11
```bash
# Ubuntu/Debian
sudo apt install python3.11 python3-pip

# Arch Linux
sudo pacman -S python python-pip
```

### 4. Install NVIDIA Drivers (for GPU acceleration)
```bash
# Ubuntu/Debian
sudo apt install nvidia-driver-535  # Or latest

# Arch Linux
sudo pacman -S nvidia nvidia-utils
```

### 5. Install CUDA Toolkit (optional, for TensorRT)
Download from: https://developer.nvidia.com/cuda-downloads

### 6. Install TensorRT (optional, for best performance)
Download from: https://developer.nvidia.com/tensorrt

## AI Dependencies

Install Python packages for AI features:

```bash
# RIFE (frame interpolation)
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu118

# Real-CUGAN / Real-ESRGAN (upscaling)
pip install vsmlrt  # VapourSynth ML Runtime
pip install vsrealesrgan
```

## Verify Installation

Launch the app and go to **Dependency Manager** to verify all dependencies are detected.
```

---

## Effort Breakdown (Linux Only)

| Phase | Task | Effort | Complexity |
|-------|------|--------|------------|
| 1 | Platform Abstraction | 5-7 days | Medium |
| 2 | Dependency Installation | 3-5 days | Low-Medium |
| 3 | Path Detection | 2-3 days | Low |
| 4 | Update DependencyChecker | 2-3 days | Low |
| 5 | Documentation | 2-3 days | Low |
| 6 | Testing on Linux | 3-5 days | Medium |

**Total: 17-26 working days** (3.5-5 weeks)

**Much less than 30-50 days** because:
- ❌ Skip macOS (not a target)
- ❌ Skip complex TensorRT alternatives (Linux has TensorRT)
- ❌ Skip CPU fallbacks (Linux has NVIDIA GPUs)
- ✅ Focus only on Linux + NVIDIA

---

## What Works Out of the Box (No Changes Needed)

1. ✅ **Avalonia UI** - Already works on Linux
2. ✅ **Blazor Server** - .NET is cross-platform
3. ✅ **VapourSynth** - Install via apt/pacman
4. ✅ **FFmpeg** - Install via apt/pacman
5. ✅ **Python** - Install via apt/pacman
6. ✅ **RIFE/CUGAN/ESRGAN** - Python packages, work everywhere
7. ✅ **CUDA** - Full Linux support
8. ✅ **TensorRT** - Full Linux support
9. ✅ **MLT/melt** - Cross-platform
10. ✅ **Entity Framework Core** - Works on Linux
11. ✅ **SQLite** - Works on Linux

**~80% of the app already works on Linux!** Only need platform abstraction for hardware detection and paths.

---

## SVP Handling (Windows-Only Feature)

### Make SVP Optional

```csharp
// SvpDetectionService.cs
[SupportedOSPlatform("windows")]
public class SvpDetectionService
{
    // Existing code
}

// Program.cs
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    builder.Services.AddSingleton<SvpDetectionService>();
}
else
{
    // Linux: No SVP, that's fine
    builder.Services.AddSingleton<SvpDetectionService>(
        _ => new SvpDetectionService { IsInstalled = false });
}
```

### UI Changes

**Settings Page**:
```razor
@if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    <MudItem xs="12">
        <MudSwitch @bind-Value="_settings.UseSvpEncoders"
                   Label="Use SVP Encoders" />
        <MudText Typo="Typo.caption">
            Detect and use SVP's optimized FFmpeg build (Windows-only feature)
        </MudText>
    </MudItem>
}
else
{
    <MudAlert Severity="Severity.Info">
        SVP integration is a Windows-only feature.
        Linux users can install FFmpeg, TensorRT, and CUDA manually.
    </MudAlert>
}
```

---

## Linux-Specific Features & Enhancements

### Package Availability Detection

```csharp
[SupportedOSPlatform("linux")]
public class LinuxPackageDetector
{
    public async Task<bool> IsPackageInstalledAsync(string packageName)
    {
        // Detect package manager
        if (File.Exists("/usr/bin/dpkg"))
        {
            // Debian/Ubuntu
            var result = await RunCommandAsync("dpkg", $"-l {packageName}");
            return result.ExitCode == 0;
        }
        else if (File.Exists("/usr/bin/pacman"))
        {
            // Arch Linux
            var result = await RunCommandAsync("pacman", $"-Q {packageName}");
            return result.ExitCode == 0;
        }
        // Add more package managers as needed
        return false;
    }
}
```

### NVIDIA Driver Detection

```csharp
[SupportedOSPlatform("linux")]
public async Task<string?> GetNvidiaDriverVersionAsync()
{
    var result = await RunCommandAsync("nvidia-smi", "--query-gpu=driver_version --format=csv,noheader");
    if (result.ExitCode == 0)
    {
        return result.Output.Trim();
    }
    return null;
}
```

---

## Testing Plan

### Local Linux Testing

**Option 1: WSL 2 (Windows Subsystem for Linux)**
- Install WSL 2 with Ubuntu
- Install NVIDIA WSL drivers
- Test basic functionality
- **Limitation**: No GUI in WSL (need X server or WSLg)

**Option 2: Linux VM with GPU Passthrough**
- VirtualBox/VMware with NVIDIA GPU passthrough
- Full GUI testing
- More realistic environment

**Option 3: Dual Boot Linux**
- Native Linux environment
- Best testing scenario
- Full GPU support

### CI/CD for Linux

**GitHub Actions**:
```yaml
jobs:
  build-linux:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          dotnet-quality: 'preview'

      - name: Build for Linux
        run: dotnet build -r linux-x64

      - name: Run unit tests
        run: dotnet test --filter "Category=Unit"
```

---

## Recommended Approach

### Phase 1: Foundation (Now)
1. ✅ Keep Windows as primary platform (current state)
2. ✅ Document Windows-only features (SVP)
3. ✅ Use `RuntimeInformation.IsOSPlatform()` checks where needed

### Phase 2: Abstraction (Next 3-4 weeks)
1. Create `IHardwareDetector` interface
2. Move WMI code to `WindowsHardwareDetector`
3. Create `LinuxHardwareDetector` with nvidia-smi/lspci
4. Abstract path detection
5. Make SVP optional/Windows-only

### Phase 3: Linux Support (After abstraction)
1. Test on Linux
2. Create Linux installation documentation
3. Add Linux-specific CI/CD
4. Release as "beta" Linux support
5. Get feedback from Linux users

### Phase 4: Refinement (Ongoing)
1. Fix Linux-specific bugs
2. Optimize for different distros
3. Add more package managers
4. Improve Linux UX

---

## Long-Term Maintenance

### Minimal Ongoing Work

**Linux support won't add significant maintenance burden:**
- Most bugs will be cross-platform (.NET, Avalonia, FFmpeg)
- Platform-specific code is isolated behind interfaces
- Linux users report better bug reports (advanced users)
- Can leverage GitHub Actions for automated Linux testing

**Estimated Maintenance**: ~5-10% additional time
- Most changes are platform-agnostic
- Platform-specific issues are rare once abstracted

---

## Conclusion

### Answer: **YES, Linux support is absolutely feasible and recommended**

**Why it makes sense:**
1. ✅ You already chose Avalonia for cross-platform support
2. ✅ 80% of dependencies already work on Linux
3. ✅ Only blocker is WMI → easily abstracted
4. ✅ Linux users can handle manual dependency installation
5. ✅ SVP is optional (Windows-only convenience feature)
6. ✅ TensorRT/CUDA fully supported on Linux
7. ✅ Moderate effort: 17-26 days (not 30-50)

**Recommended Timeline:**
- **Now**: Continue Windows development
- **Q1-Q2 2025**: Implement platform abstraction
- **Q2-Q3 2025**: Beta Linux support
- **Q3-Q4 2025**: Stable Linux support

**Skip macOS** (low ROI):
- No NVIDIA support (CUDA deprecated)
- Poor AI performance (CPU/MPS only)
- Small user base for this type of app

**Focus on Windows + Linux** (best ROI):
- Windows: Best performance, full features
- Linux: Near-Windows performance, advanced users
- Both platforms support NVIDIA + TensorRT + CUDA

---

*This aligns perfectly with your decision to use CheapAvaloniaBlazor - you already made the architectural choice to support Linux. Now just need to abstract the Windows-specific bits.*

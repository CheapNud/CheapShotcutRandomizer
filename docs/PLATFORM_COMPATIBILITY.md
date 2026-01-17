# Platform Compatibility Analysis

---

## ⚠️ ARCHITECTURAL DECISION: DO NOT SWITCH TO MAUI

**CheapAvaloniaBlazor was the CORRECT choice. Linux support is feasible.**

### Executive Summary

**The app is cross-platform by nature, NOT Windows-only.**

- ✅ **80% of dependencies already work on Linux** (TensorRT, CUDA, VapourSynth, FFmpeg, Python, RIFE, Real-CUGAN, Real-ESRGAN, Shotcut/melt)
- ✅ **Only blocker**: System.Management (WMI) - requires interface abstraction (17-26 days effort)
- ✅ **SVP is optional** - Just a Windows-only dependency manager, not a core feature
- ✅ **TensorRT/CUDA fully supported on Linux** - Near-Windows performance achievable
- ✅ **CheapAvaloniaBlazor chosen specifically for Linux support** - This was the right architectural decision

### Why NOT to Switch to MAUI

❌ **MAUI does not support Linux** (this is why you chose Avalonia!)
❌ **Would throw away cross-platform architecture** when 80% already works
❌ **Would lock into Windows-only** unnecessarily
❌ **Would waste the correct architectural decision already made**

### The Reality

**You're 80% of the way to Linux support already.** The only work needed:

1. **Create `IHardwareDetector` interface** (platform abstraction)
2. **Move WMI code to `WindowsHardwareDetector`** (existing code)
3. **Create `LinuxHardwareDetector`** (nvidia-smi, lspci, /proc/cpuinfo)
4. **Make SVP optional/Windows-only** (it's just a convenience feature)

**Estimated Effort**: 17-26 days (see [LINUX_SUPPORT_PLAN.md](LINUX_SUPPORT_PLAN.md))

### Recommendation

✅ **Keep CheapAvaloniaBlazor**
✅ **Continue Windows development now**
✅ **Implement platform abstraction in Q1-Q2 2025**
✅ **Beta Linux support in Q2-Q3 2025**
❌ **DO NOT switch to MAUI** - It would eliminate Linux support forever

---

## Current Status

**Cheap Shotcut Randomizer** is currently a **Windows-first application** with NVIDIA GPU optimization, designed for future Linux support.

## Platform Support Matrix

| Dependency | Windows | Linux | macOS | Notes |
|-----------|---------|-------|-------|-------|
| **TensorRT** | ✅ Full | ✅ Full | ❌ No | NVIDIA-only, full Windows+Linux support |
| **SVP 4 Pro** | ✅ Full | ❌ No | ❌ No | Windows-only commercial software |
| **VapourSynth** | ✅ Full | ✅ Full | ✅ Full | Cross-platform video framework |
| **RIFE** | ✅ Full | ✅ Full | ⚠️ CPU Only | GPU support requires NVIDIA/CUDA |
| **Real-CUGAN** | ✅ Full | ✅ Full | ⚠️ CPU Only | Best on NVIDIA, macOS limited to CPU/MPS |
| **Real-ESRGAN** | ✅ Full | ✅ Full | ⚠️ CPU Only | Best on NVIDIA, macOS limited to CPU/MPS |
| **FFmpeg** | ✅ Full | ✅ Full | ✅ Full | Fully cross-platform |
| **Python 3.8-3.11** | ✅ Full | ✅ Full | ✅ Full | Fully cross-platform |
| **CUDA** | ✅ Full | ✅ Full | ❌ No | NVIDIA-only, deprecated on macOS since 2018 |
| **Shotcut/melt** | ✅ Full | ✅ Full | ✅ Full | MLT Framework is cross-platform |
| **Avalonia UI** | ✅ Full | ✅ Full | ✅ Full | Cross-platform .NET UI framework |
| **System.Management (WMI)** | ✅ Full | ❌ No | ❌ No | **Windows-only** - hardware detection |
| **WindowsIdentity** | ✅ Full | ❌ No | ❌ No | **Windows-only** - admin detection |

### Legend
- ✅ **Full** - Complete support, all features available
- ⚠️ **Limited** - Partial support, some limitations
- ❌ **No** - Not supported on this platform

---

## Windows-Only Components

### Critical Dependencies
1. **System.Management (WMI)** - Hardware detection (GPU, CPU, encoders)
2. **WindowsIdentity** - Administrator privilege detection
3. **SVP 4 Pro** - Commercial frame interpolation software
4. **TensorRT** - NVIDIA inference acceleration (not available on macOS)

### Hardware Features
- **NVENC** encoding - NVIDIA GPUs (Windows/Linux only)
- **QSV** encoding - Intel Quick Sync (Windows/Linux)
- **AMF** encoding - AMD GPUs (Windows/Linux)

---

## Cross-Platform Features (Potentially)

These features could work on other platforms with refactoring:

1. **FFmpeg Rendering** - FFmpeg is cross-platform
2. **Shotcut/Melt Rendering** - MLT Framework works on Linux/macOS
3. **RIFE Frame Interpolation** - Python-based, CPU fallback available
4. **Real-CUGAN/Real-ESRGAN** - Python-based, CPU fallback available
5. **VapourSynth Processing** - Cross-platform framework
6. **Non-AI Upscaling** - Uses VapourSynth, fully cross-platform
7. **Avalonia UI** - Already cross-platform

---

## Performance by Platform

### Windows with NVIDIA GPU (Current Target)
- **Best Performance**: TensorRT + NVENC + WMI hardware detection
- **RIFE**: 15-30 fps with TensorRT
- **Real-CUGAN**: 10-20 fps with TensorRT
- **Encoding**: NVENC hardware acceleration (8-10x faster than CPU)

### Linux with NVIDIA GPU
- **Near-Windows Performance**: TensorRT + CUDA + NVENC available
- **RIFE**: 15-30 fps with TensorRT (same as Windows)
- **Real-CUGAN**: 10-20 fps with TensorRT (same as Windows)
- **Encoding**: NVENC hardware acceleration
- **Missing**: WMI hardware detection (easily abstracted), SVP integration (optional feature)

### macOS
- **Significantly Slower**: No NVIDIA support (CUDA deprecated since macOS 10.13)
- **RIFE**: 1-3 fps CPU-only or 5-8 fps with MPS
- **Real-CUGAN**: 0.5-2 fps CPU-only or 3-5 fps with MPS
- **Encoding**: VideoToolbox hardware acceleration (H.264/HEVC)
- **Missing**: TensorRT, NVIDIA GPUs, WMI, SVP

---

## Effort Required for Linux Support

### Summary
**Estimated Effort**: **17-26 working days** (3.5-5 weeks for 1 developer)
**Complexity**: **Medium**

**Note**: This is for Linux support only. Full cross-platform (including macOS) would be 30-50 days, but macOS is not recommended due to lack of NVIDIA/CUDA support.

### Task Breakdown (Linux Only)

| Task | Effort | Complexity |
|------|--------|------------|
| Platform Abstraction Layer | 5-7 days | Medium |
| Hardware Detection Refactor (IHardwareDetector) | 2-3 days | Low-Medium |
| Path Detection Refactor (IPlatformPaths) | 2-3 days | Low |
| Dependency Installer Abstraction | 3-5 days | Low-Medium |
| Update DependencyChecker | 2-3 days | Low |
| Testing on Linux | 3-5 days | Medium |
| Documentation Updates | 2-3 days | Low |

**Total**: 17-26 working days

**Why less than full cross-platform?**
- ❌ Skip macOS (no NVIDIA/CUDA support)
- ❌ Skip complex TensorRT alternatives (Linux has TensorRT)
- ❌ Skip CPU fallbacks (Linux users have NVIDIA GPUs)
- ✅ Focus only on Linux + NVIDIA (same stack as Windows)

---

## Recommended Approach

### Option 1: Windows + Linux Support ⭐ RECOMMENDED
**Effort**: Medium (17-26 days)

- **Windows**: Full feature set (primary platform)
- **Linux**: Near-identical feature set (secondary platform)
  - TensorRT + CUDA + NVENC (same as Windows)
  - All AI features work (RIFE, Real-CUGAN, Real-ESRGAN)
  - Platform-abstracted hardware detection
  - Manual dependency installation (no SVP)
- **macOS**: Not supported (no NVIDIA/CUDA)

**Pros**:
- ✅ Honors the original architectural decision (CheapAvaloniaBlazor chosen for Linux)
- ✅ 80% of dependencies already work on Linux
- ✅ Near-Windows performance on Linux (TensorRT available)
- ✅ Moderate development effort (17-26 days)
- ✅ Minimal ongoing maintenance burden (5-10% additional time)
- ✅ Linux users are advanced (can handle manual setup)

**Cons**:
- Platform abstraction layer needed
- Separate installation documentation
- More testing required

**Why this is recommended**:
- You chose CheapAvaloniaBlazor specifically because MAUI doesn't support Linux
- Switching to MAUI would throw away this architectural decision
- Linux support validates the framework choice

### Option 2: Windows-Only (Current State)
**Effort**: Minimal (no additional work)

- Focus on Windows platform only
- Maintain full feature set
- No platform abstraction needed

**Pros**:
- No additional development needed
- Clear target audience

**Cons**:
- ❌ Wastes the CheapAvaloniaBlazor architectural decision
- ❌ Makes MAUI a more logical choice (if Windows-only)
- ❌ Excludes Linux users despite having the capability
- ❌ Leaves 80% cross-platform dependencies unused

**Why NOT recommended**:
- Contradicts the reason for choosing Avalonia over MAUI
- Most dependencies already work on Linux

### Option 3: Full Cross-Platform (Windows + Linux + macOS)
**Effort**: High (30-50 days)

- Complete platform abstraction
- macOS support with CPU-only fallbacks

**Pros**:
- Maximum user reach

**Cons**:
- ❌ Significant development time (30-50 days vs 17-26)
- ❌ macOS performance is poor (no NVIDIA/CUDA)
- ❌ Low ROI for macOS (small user base for AI video tools)
- ❌ Ongoing complexity for little gain

**Why NOT recommended**:
- macOS lacks NVIDIA GPU support (CUDA deprecated since 2018)
- AI features would be 5-10x slower on macOS
- Not worth the additional 13-24 days of effort

---

## Technical Blockers

### Cannot Be Ported
1. **SVP 4 Pro Integration** - Windows-only commercial software (make optional)
2. **TensorRT on macOS** - NVIDIA doesn't support macOS (use CPU/MPS fallback)
3. **System.Management (WMI)** - Windows-only (requires platform-specific implementations)

### Requires Significant Refactoring
1. **Hardware Detection** - Platform-specific code needed
2. **Dependency Installation** - Different package managers per platform
3. **Admin Privilege Detection** - OS-specific APIs

### Easy to Port
1. **File Path Handling** - Use `Path.Combine` and platform detection
2. **FFmpeg Integration** - Already cross-platform
3. **VapourSynth Usage** - Cross-platform framework

---

## Recommendations

### For Current State (Windows-Only)
1. ✅ **Document Windows requirement** clearly in README
2. ✅ **Suppress CA1416 warnings** (already done via `<NoWarn>`)
3. ✅ **Add platform guards** to Windows-specific code (already done)
4. ✅ **Specify runtime identifiers** in csproj (already done)

### For Future Cross-Platform Support
1. Create `IPlatformHardwareDetector` interface with platform-specific implementations
2. Abstract path detection into `IPlatformPathProvider`
3. Make SVP integration optional (Windows-only feature)
4. Use `onnxruntime` with platform-specific execution providers
5. Implement fallback to CPU/MPS for macOS AI processing
6. Test extensively on each platform

---

## Conclusion

The **Cheap Shotcut Randomizer** is designed for Windows + Linux with NVIDIA GPUs. The app is **cross-platform by nature** - 80% of dependencies (TensorRT, CUDA, VapourSynth, FFmpeg, Avalonia, all AI models) work on both Windows and Linux.

**Only blockers are Windows-specific APIs** (WMI, WindowsIdentity) which are easily abstracted behind interfaces. SVP is a Windows-only convenience feature (dependency manager), not a core requirement.

**The recommended approach is:**

1. ✅ **Keep CheapAvaloniaBlazor** - Correct architectural choice for Linux support
2. ✅ **Continue Windows-first development** - Current focus with best tooling
3. ✅ **Implement platform abstraction** - Q1-Q2 2025 (17-26 days effort)
4. ✅ **Beta Linux support** - Q2-Q3 2025 with near-Windows performance
5. ❌ **Skip macOS** - No NVIDIA/CUDA support (CPU-only, poor performance)
6. ❌ **DO NOT switch to MAUI** - Would eliminate Linux support forever

---

## Platform Requirements (Current)

### Minimum Requirements
- **OS**: Windows 10/11 (64-bit)
- **CPU**: Intel/AMD with 4+ cores
- **RAM**: 8 GB
- **GPU**: Optional (CPU rendering supported)

### Recommended Requirements
- **OS**: Windows 10/11 (64-bit)
- **CPU**: Intel i7/AMD Ryzen 7 or better
- **RAM**: 16 GB
- **GPU**: NVIDIA RTX 2060 or newer (for AI features)
- **VRAM**: 6 GB+ (for 1080p AI upscaling)
- **Storage**: SSD recommended for temp files

### Optimal Requirements (Target Configuration)
- **OS**: Windows 11 (64-bit)
- **CPU**: AMD Ryzen 9 5900X or better
- **RAM**: 32 GB
- **GPU**: NVIDIA RTX 3080 or newer
- **VRAM**: 10 GB+
- **Storage**: NVMe SSD

---

*Last Updated: 2025-01-28*
*Based on comprehensive cross-platform compatibility analysis*
*Architectural Decision: Keep CheapAvaloniaBlazor, pursue Linux support (17-26 days effort)*

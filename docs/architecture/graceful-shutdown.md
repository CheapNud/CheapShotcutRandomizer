# Graceful Shutdown Implementation

## Overview
This document describes the graceful shutdown implementation for background rendering processes in the ShotcutRandomizer application.

## Problem Statement
When users stop debugging or cancel a render job, background processes (vspipe, FFmpeg, melt) would:
- Continue running at high GPU/CPU usage
- Not terminate immediately
- Require manual cleanup or waiting for eventual timeout
- Create orphaned child processes (Python → PyTorch → CUDA for vspipe)

## Solution
Implemented a comprehensive graceful shutdown system with process tree cleanup.

## Architecture

### ProcessManager Utility Class
**Location**: `Services/Utilities/ProcessManager.cs`

The `ProcessManager` provides centralized process lifecycle management with graceful shutdown support.

#### Key Features:
1. **Graceful Termination First**: Attempts to close processes cleanly before force-killing
2. **Process Tree Cleanup**: Uses `Process.Kill(entireProcessTree: true)` to kill child processes
3. **Timeout-Based Escalation**: Waits for graceful exit, then force-kills if needed
4. **Cross-Platform Support**: Handles Windows (CloseMainWindow) and Unix (SIGTERM) differently

#### Main Methods:

```csharp
// Gracefully shutdown a process with timeout
Task<bool> GracefulShutdownAsync(
    Process? process,
    int gracefulTimeoutMs = 3000,
    string processName = "process")

// Create callback for CancellationToken.Register()
Action CreateGracefulShutdownCallback(
    Process process,
    string processName = "process")

// Cleanup temporary files after process termination
void CleanupTempFiles(
    IEnumerable<string> tempFiles,
    string processName = "process")
```

### Shutdown Flow

#### 1. User Cancellation (via CancelJobAsync)
```
User clicks Cancel
    ↓
RenderQueueService.CancelJobAsync(jobId)
    ↓
CancellationTokenSource.Cancel() for that job
    ↓
ProcessManager.GracefulShutdownAsync() registered callbacks execute
    ↓
Process gracefully exits (or force-killed after timeout)
```

#### 2. Application Shutdown (via StopAsync)
```
App shutdown initiated (stop debugging, close app)
    ↓
RenderQueueService.StopAsync() called by IHostedService lifecycle
    ↓
Iterate all running jobs, call CancelJobAsync() for each
    ↓
Wait up to 5 seconds for all jobs to cancel
    ↓
Force cleanup any remaining jobs
    ↓
Log "Graceful shutdown complete"
```

## Implementation Details

### MeltRenderService (melt process)
**File**: `Services/MeltRenderService.cs`

```csharp
// Before: Simple process.Kill(entireProcessTree: true)
cancellationToken.Register(() => {
    if (!process.HasExited) {
        process.Kill(entireProcessTree: true);
    }
});

// After: Graceful shutdown with timeout
cancellationToken.Register(async () => {
    await ProcessManager.GracefulShutdownAsync(
        process,
        gracefulTimeoutMs: 3000,
        processName: "melt");
});
```

**Why 3 seconds?**
- Melt can flush buffers and close files cleanly
- Most renders stop within 1-2 seconds
- 3 seconds provides comfortable margin

### RealEsrganService (vspipe + ffmpeg pipeline)
**File**: `Services/RealESRGAN/RealEsrganService.cs`

```csharp
// Register separate handlers for vspipe and ffmpeg
var vspipeCancellation = cancellationToken.Register(async () => {
    await ProcessManager.GracefulShutdownAsync(
        vspipe,
        gracefulTimeoutMs: 3000,
        processName: "vspipe (Real-ESRGAN)");
});

var ffmpegCancellation = cancellationToken.Register(async () => {
    await ProcessManager.GracefulShutdownAsync(
        ffmpeg,
        gracefulTimeoutMs: 2000,
        processName: "ffmpeg (Real-ESRGAN)");
});

try {
    // Pipeline processing...
}
finally {
    vspipeCancellation.Dispose();
    ffmpegCancellation.Dispose();
}
```

**Process Tree Handling:**
- vspipe spawns: Python → PyTorch → CUDA processes
- `Process.Kill(entireProcessTree: true)` handles all children
- FFmpeg gets slightly shorter timeout (2s) as it's downstream

### RifeInterpolationService (multiple variants)
**File**: `Services/RIFE/RifeInterpolationService.cs`

#### SVP RIFE (vspipe + ffmpeg)
Same pattern as Real-ESRGAN: separate handlers for vspipe and ffmpeg

#### Python RIFE (Practical-RIFE)
```csharp
var rifeCancellation = cancellationToken.Register(async () => {
    await ProcessManager.GracefulShutdownAsync(
        process,
        gracefulTimeoutMs: 3000,
        processName: "RIFE (Python)");
});

try {
    await process.WaitForExitAsync(cancellationToken);
}
finally {
    rifeCancellation.Dispose();
}
```

### RenderQueueService Application Lifecycle
**File**: `Services/Queue/RenderQueueService.cs`

Added `StopAsync()` override to handle application shutdown:

```csharp
public override async Task StopAsync(CancellationToken cancellationToken)
{
    Debug.WriteLine("=== RenderQueueService: Graceful shutdown initiated ===");

    // 1. Get all running jobs
    List<Guid> runningJobIds;
    lock (_runningJobsLock) {
        runningJobIds = _runningJobs.Keys.ToList();
    }

    // 2. Cancel all running jobs
    foreach (var jobId in runningJobIds) {
        await CancelJobAsync(jobId);
    }

    // 3. Wait up to 5 seconds for cancellations to complete
    var waitStart = DateTime.UtcNow;
    while ((DateTime.UtcNow - waitStart).TotalSeconds < 5) {
        lock (_runningJobsLock) {
            if (_runningJobs.Count == 0) {
                Debug.WriteLine("All jobs cancelled successfully");
                break;
            }
        }
        await Task.Delay(100, cancellationToken);
    }

    // 4. Force cleanup any remaining jobs
    lock (_runningJobsLock) {
        if (_runningJobs.Count > 0) {
            Debug.WriteLine($"WARNING: {_runningJobs.Count} job(s) did not cancel gracefully");
            foreach (var kvp in _runningJobs.ToList()) {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            _runningJobs.Clear();
        }
    }

    await base.StopAsync(cancellationToken);
}
```

## Timeouts Summary

| Process | Timeout | Reason |
|---------|---------|--------|
| melt | 3000ms | Needs time to flush video buffers |
| vspipe (RIFE) | 3000ms | Python → PyTorch cleanup |
| vspipe (ESRGAN) | 3000ms | Python → PyTorch cleanup |
| ffmpeg | 2000ms | Downstream, faster to kill |
| Python RIFE | 3000ms | Python process cleanup |
| App shutdown | 5000ms | Wait for all jobs to cancel |

## Testing Checklist

### Manual Testing
- [x] Cancel single render job during melt rendering
- [x] Cancel single render job during RIFE interpolation
- [x] Cancel single render job during Real-ESRGAN upscaling
- [x] Stop debugging with active render jobs
- [x] Close application with active render jobs
- [x] Cancel job with vspipe at 100% GPU usage
- [x] Verify process tree cleanup (check Task Manager for orphans)

### Automated Testing
```csharp
// TODO: Add integration tests for graceful shutdown
[Fact]
public async Task CancelJob_Should_CleanupProcesses_Within_Timeout()
{
    // Arrange: Start render job
    // Act: Cancel job
    // Assert: Processes terminated within 3 seconds
}

[Fact]
public async Task StopAsync_Should_CancelAllJobs()
{
    // Arrange: Start multiple render jobs
    // Act: Call StopAsync()
    // Assert: All jobs cancelled, no orphaned processes
}
```

## Debug Logging

All shutdown operations are logged to Debug output:

```
[ProcessManager] vspipe (RIFE): Attempting graceful shutdown...
[ProcessManager] Sending WM_CLOSE to main window...
[ProcessManager] Waiting 3000ms for graceful exit...
[ProcessManager] vspipe (RIFE): Gracefully exited
```

Or on force-kill:
```
[ProcessManager] vspipe (RIFE): Graceful shutdown failed, force killing process tree...
[ProcessManager] Killing process tree for PID 12345...
[ProcessManager] vspipe (RIFE): Force killed successfully
```

## Known Limitations

1. **vspipe on first run**: TensorRT compilation takes 5-15 minutes and cannot be gracefully interrupted
   - **Mitigation**: Show warning to user, don't allow cancel during first-run compilation

2. **CUDA processes**: CUDA kernels may take 100-500ms to release GPU
   - **Mitigation**: 3-second timeout provides adequate buffer

3. **Network file I/O**: Rendering to network drives may delay shutdown
   - **Mitigation**: Force-kill after timeout handles this

## Future Improvements

1. **User Feedback**: Show "Cancelling job..." progress in UI
2. **Configurable Timeouts**: Allow users to configure shutdown timeouts in settings
3. **Temp File Cleanup**: Automatically clean up temp files after force-kill
4. **Graceful HTTP Shutdown**: If API endpoints are added, implement graceful HTTP shutdown

## References

- [.NET Process.Kill Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill)
- [IHostedService Lifecycle](https://learn.microsoft.com/en-us/dotnet/core/extensions/hosted-services)
- [CancellationToken Best Practices](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)

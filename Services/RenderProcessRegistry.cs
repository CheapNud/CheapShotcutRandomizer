using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Tracks the melt process for each running job so pause/resume can suspend the
/// process in place instead of killing it and losing the render progress.
/// Uses NtSuspendProcess/NtResumeProcess (safer than the DebugActiveProcess trick
/// Shotcut uses - no debugger relationship, so melt survives if this app exits).
/// </summary>
public class RenderProcessRegistry
{
    private readonly ConcurrentDictionary<Guid, Process> _processes = new();

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    public void Register(Guid jobId, Process meltProcess) => _processes[jobId] = meltProcess;

    public void Unregister(Guid jobId) => _processes.TryRemove(jobId, out _);

    public bool TrySuspend(Guid jobId)
    {
        if (!_processes.TryGetValue(jobId, out var meltProcess))
            return false;

        try
        {
            if (meltProcess.HasExited)
                return false;

            return NtSuspendProcess(meltProcess.Handle) == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to suspend melt for job {jobId}: {ex.Message}");
            return false;
        }
    }

    public bool TryResume(Guid jobId)
    {
        if (!_processes.TryGetValue(jobId, out var meltProcess))
            return false;

        try
        {
            if (meltProcess.HasExited)
                return false;

            return NtResumeProcess(meltProcess.Handle) == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to resume melt for job {jobId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Resume every suspended process - called before shutdown so cancellation
    /// (graceful or kill) can actually reach a frozen melt.
    /// </summary>
    public void ResumeAll()
    {
        foreach (var jobId in _processes.Keys)
        {
            TryResume(jobId);
        }
    }
}

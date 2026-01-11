namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Statistics about the render queue
/// </summary>
public class QueueStatistics
{
    /// <summary>
    /// Whether the queue is currently paused
    /// </summary>
    public bool IsQueuePaused { get; set; }

    /// <summary>
    /// Number of jobs waiting to be processed
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>
    /// Number of jobs currently being processed
    /// </summary>
    public int RunningCount { get; set; }

    /// <summary>
    /// Number of completed jobs
    /// </summary>
    public int CompletedCount { get; set; }

    /// <summary>
    /// Number of failed jobs (including dead letter)
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Total number of jobs in the system
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Get a user-friendly summary of the queue status
    /// </summary>
    public string GetStatusSummary()
    {
        if (IsQueuePaused)
        {
            if (PendingCount > 0)
                return $"Queue PAUSED - {PendingCount} job(s) waiting";
            else
                return "Queue PAUSED - No jobs waiting";
        }

        if (RunningCount > 0)
            return $"Processing {RunningCount} job(s), {PendingCount} waiting";
        else if (PendingCount > 0)
            return $"Queue RUNNING - {PendingCount} job(s) waiting to start";
        else
            return "Queue RUNNING - No active jobs";
    }
}

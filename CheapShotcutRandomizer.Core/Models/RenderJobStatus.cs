namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Status of a render job in the queue
/// </summary>
public enum RenderJobStatus
{
    /// <summary>
    /// Job is waiting to be processed
    /// </summary>
    Pending,

    /// <summary>
    /// Job is currently being rendered
    /// </summary>
    Running,

    /// <summary>
    /// Job has been paused by user
    /// </summary>
    Paused,

    /// <summary>
    /// Job completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Job failed but can be retried
    /// </summary>
    Failed,

    /// <summary>
    /// Job was cancelled by user
    /// </summary>
    Cancelled,

    /// <summary>
    /// Job failed max retries and moved to dead letter queue
    /// </summary>
    DeadLetter
}

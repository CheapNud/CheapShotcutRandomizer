using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Models;

/// <summary>
/// Event arguments for render progress and status updates
/// </summary>
public class RenderProgressEventArgs : EventArgs
{
    /// <summary>
    /// Unique identifier of the job
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Current status of the job
    /// </summary>
    public RenderJobStatus Status { get; set; }

    /// <summary>
    /// Current progress percentage (0-100)
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Current frame being rendered
    /// </summary>
    public int CurrentFrame { get; set; }

    /// <summary>
    /// Total frames to render (0 if not yet known)
    /// </summary>
    public int TotalFrames { get; set; }

    /// <summary>
    /// Estimated time remaining (null if cannot be calculated)
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Time elapsed since job started
    /// </summary>
    public TimeSpan? ElapsedTime { get; set; }

    /// <summary>
    /// Error message if job failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

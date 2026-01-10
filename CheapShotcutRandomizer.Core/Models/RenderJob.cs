using System.Globalization;
using CheapHelpers.MediaProcessing.Models;

namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Represents a video render job in the queue
/// MLT/Shotcut focused - AI upscaling has been moved to CheapUpscaler
/// </summary>
public class RenderJob
{
    /// <summary>
    /// Database primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Unique identifier for this job (used for external references)
    /// </summary>
    public Guid JobId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Path to the source MLT project file
    /// </summary>
    public string SourceVideoPath { get; set; } = string.Empty;

    /// <summary>
    /// Path where the rendered video will be saved
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Serialized JSON of MeltRenderSettings
    /// </summary>
    public string RenderSettings { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the job
    /// </summary>
    public RenderJobStatus Status { get; set; } = RenderJobStatus.Pending;

    /// <summary>
    /// When the job was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the job was added to the queue
    /// </summary>
    public DateTime? QueuedAt { get; set; }

    /// <summary>
    /// When rendering started
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the job finished (completed, failed, or cancelled)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Last time the job was updated (used for progress updates)
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current progress percentage (0-100)
    /// </summary>
    public double ProgressPercentage { get; set; }

    /// <summary>
    /// Current frame being rendered
    /// </summary>
    public int CurrentFrame { get; set; }

    /// <summary>
    /// Total frames to render (null if not yet known)
    /// </summary>
    public int? TotalFrames { get; set; }

    /// <summary>
    /// Estimated time remaining for this job
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Type of source file (MLT Project or Video File)
    /// </summary>
    public RenderType RenderType { get; set; } = RenderType.MltSource;

    /// <summary>
    /// MLT-specific render settings (nullable)
    /// NOT MAPPED to database - use RenderSettings JSON property instead
    /// </summary>
    public MeltRenderSettings? MeltSettings { get; set; }

    /// <summary>
    /// FFmpeg-specific render settings (nullable)
    /// NOT MAPPED to database - use RenderSettings JSON property instead
    /// </summary>
    public FFmpegRenderSettings? FFmpegSettings { get; set; }

    /// <summary>
    /// Number of times this job has been retried
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Maximum number of retry attempts before moving to dead letter
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Last error message (if job failed)
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Last error stack trace (if job failed)
    /// </summary>
    public string? ErrorStackTrace { get; set; }

    /// <summary>
    /// Process ID that claimed this job (for crash recovery)
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// Machine name that claimed this job (for crash recovery)
    /// </summary>
    public string? MachineName { get; set; }

    /// <summary>
    /// Comma-separated list of video track indices to render (e.g., "0,1,2")
    /// If null, render all video tracks
    /// </summary>
    public string? SelectedVideoTracks { get; set; }

    /// <summary>
    /// Comma-separated list of audio track indices to render (e.g., "0,1")
    /// If null, render all audio tracks
    /// </summary>
    public string? SelectedAudioTracks { get; set; }

    /// <summary>
    /// In point marker (frame number). If null, render from start.
    /// Used for MLT projects to render only a specific range.
    /// </summary>
    public int? InPoint { get; set; }

    /// <summary>
    /// Out point marker (frame number). If null, render to end.
    /// Used for MLT projects to render only a specific range.
    /// </summary>
    public int? OutPoint { get; set; }

    /// <summary>
    /// Frame rate of the source video (for timecode conversion)
    /// Defaults to 30.0 fps if not specified
    /// </summary>
    public double FrameRate { get; set; } = 30.0;

    /// <summary>
    /// Size of the output file in bytes (null if not yet rendered)
    /// </summary>
    public long? OutputFileSizeBytes { get; set; }

    /// <summary>
    /// Get human-readable file size string (e.g., "1.5 GB", "250 MB")
    /// </summary>
    public string GetOutputFileSizeFormatted()
    {
        if (!OutputFileSizeBytes.HasValue)
            return "N/A";

        return FormatFileSize(OutputFileSizeBytes.Value);
    }

    /// <summary>
    /// Format bytes to human-readable string
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len.ToString("F2", CultureInfo.InvariantCulture)} {sizes[order]}";
    }

    /// <summary>
    /// Convert frame number to timecode string (HH:MM:SS.mmm)
    /// </summary>
    public string FramesToTimecode(int frames)
    {
        var totalSeconds = frames / FrameRate;
        var timeSpan = TimeSpan.FromSeconds(totalSeconds);

        // Format as HH:MM:SS.mmm
        return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}.{timeSpan.Milliseconds:D3}";
    }
}

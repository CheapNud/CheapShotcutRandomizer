using CheapShotcutRandomizer.Services;
using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Models;

/// <summary>
/// Represents a video render job in the queue
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
    /// RIFE-specific settings (nullable)
    /// NOT MAPPED to database - use RenderSettings JSON property instead
    /// </summary>
    public RifeSettings? RifeSettings { get; set; }

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
    /// Indicates this is a two-stage render job (MLT → temp video → RIFE interpolation)
    /// When true, the source is an MLT file that needs to be rendered before RIFE processing
    /// </summary>
    public bool IsTwoStageRender { get; set; } = false;

    /// <summary>
    /// Indicates this is a three-stage render job (MLT → RIFE → Real-ESRGAN)
    /// When true, applies both frame interpolation and AI upscaling
    /// </summary>
    public bool IsThreeStageRender { get; set; } = false;

    /// <summary>
    /// Path to the intermediate/temporary file for two-stage renders
    /// Used when rendering MLT → temp file → RIFE
    /// </summary>
    public string? IntermediatePath { get; set; }

    /// <summary>
    /// Path to the second intermediate file for three-stage renders
    /// Used when rendering MLT → RIFE → temp file → Real-ESRGAN
    /// </summary>
    public string? IntermediatePath2 { get; set; }

    /// <summary>
    /// Size of the output file in bytes (null if not yet rendered)
    /// </summary>
    public long? OutputFileSizeBytes { get; set; }

    /// <summary>
    /// Size of the intermediate file in bytes for two-stage renders (null if not applicable)
    /// </summary>
    public long? IntermediateFileSizeBytes { get; set; }

    /// <summary>
    /// Size of the second intermediate file in bytes for three-stage renders (null if not applicable)
    /// </summary>
    public long? IntermediateFileSizeBytes2 { get; set; }

    /// <summary>
    /// Current stage for two-stage renders (e.g., "Stage 1: MLT Render", "Stage 2: RIFE Interpolation")
    /// For three-stage: "Stage 1: MLT Render", "Stage 2: RIFE Interpolation", "Stage 3: Real-ESRGAN Upscaling"
    /// </summary>
    public string? CurrentStage { get; set; }

    /// <summary>
    /// Enable RIFE frame interpolation
    /// </summary>
    public bool UseRifeInterpolation { get; set; } = false;

    /// <summary>
    /// Enable Real-ESRGAN AI upscaling
    /// </summary>
    public bool UseRealEsrgan { get; set; } = false;

    /// <summary>
    /// Serialized JSON of Real-ESRGAN settings
    /// </summary>
    public string? RealEsrganOptionsJson { get; set; }

    /// <summary>
    /// Enable Real-CUGAN AI upscaling (10-13x faster than Real-ESRGAN)
    /// Optimized for anime/cartoon content
    /// </summary>
    public bool UseRealCugan { get; set; } = false;

    /// <summary>
    /// Serialized JSON of Real-CUGAN settings
    /// </summary>
    public string? RealCuganOptionsJson { get; set; }

    /// <summary>
    /// Target upscale resolution (height in pixels)
    /// 0 = disabled, 1440, 2160, 3840, 4320, etc.
    /// </summary>
    public int TargetUpscaleResolution { get; set; } = 0;

    /// <summary>
    /// Enable non-AI upscaling (xBR, Lanczos, HQx)
    /// Fast alternatives to Real-ESRGAN (seconds vs hours)
    /// </summary>
    public bool UseNonAiUpscaling { get; set; } = false;

    /// <summary>
    /// Non-AI upscaling algorithm (xbr, lanczos, hqx)
    /// </summary>
    public string? NonAiUpscalingAlgorithm { get; set; }

    /// <summary>
    /// Non-AI upscaling scale factor (2, 3, or 4)
    /// </summary>
    public int NonAiUpscalingScaleFactor { get; set; } = 2;

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
    /// Get human-readable intermediate file size string
    /// </summary>
    public string GetIntermediateFileSizeFormatted()
    {
        if (!IntermediateFileSizeBytes.HasValue)
            return "N/A";

        return FormatFileSize(IntermediateFileSizeBytes.Value);
    }

    /// <summary>
    /// Format bytes to human-readable string
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {sizes[order]}";
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

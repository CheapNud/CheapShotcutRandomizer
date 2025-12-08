namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Render settings for MLT/Melt-based rendering
/// IMPORTANT: Uses CPU multi-threading, NOT NVENC (MLT's NVENC is broken/slow)
/// </summary>
public class MeltRenderSettings
{
    /// <summary>
    /// DO NOT SET TO TRUE - MLT's NVENC is broken and 2x slower than CPU
    /// This exists only to document that hardware acceleration should NOT be used
    /// </summary>
    public bool UseHardwareAcceleration { get; set; } = false;

    /// <summary>
    /// Number of CPU threads to use. 0 = auto-detect all cores
    /// For Ryzen 9 5900X: use all 12 cores for maximum performance
    /// </summary>
    public int ThreadCount { get; set; } = 0;

    /// <summary>
    /// Video codec: "libx264" (H.264) or "libx265" (H.265/HEVC)
    /// NEVER use "h264_nvenc" or "hevc_nvenc" with melt
    /// </summary>
    public string VideoCodec { get; set; } = "libx264";

    /// <summary>
    /// Audio codec: "aac", "mp3", etc.
    /// </summary>
    public string AudioCodec { get; set; } = "aac";

    /// <summary>
    /// Encoding preset: ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow
    /// Recommended: "medium" for good balance, "slow" for better compression
    /// </summary>
    public string Preset { get; set; } = "medium";

    /// <summary>
    /// Constant Rate Factor: 0-51 (lower = better quality)
    /// 18 = visually lossless, 23 = default, 28 = lower quality
    /// </summary>
    public int? Crf { get; set; } = 23;

    /// <summary>
    /// Audio bitrate: "128k", "192k", "256k", etc.
    /// </summary>
    public string AudioBitrate { get; set; } = "128k";
}

/// <summary>
/// Progress information for melt rendering
/// </summary>
public class RenderProgress
{
    public int CurrentFrame { get; set; }
    public int Percentage { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
}

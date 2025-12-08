namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Render settings for FFmpeg-based encoding
/// IMPORTANT: ABSOLUTELY USE NVENC - it's 8-10x FASTER than CPU encoding
/// Perfect for RIFE frame reassembly workflow
/// </summary>
public class FFmpegRenderSettings
{
    /// <summary>
    /// Path to FFmpeg executable (optional - will auto-detect if not set)
    /// </summary>
    public string? FFmpegPath { get; set; }

    /// <summary>
    /// ABSOLUTELY SET TO TRUE if you have RTX 3080
    /// Speed improvement: 8-10x faster than CPU
    /// Example: 4-hour job becomes 24-30 minutes
    /// </summary>
    public bool UseHardwareAcceleration { get; set; } = true;

    /// <summary>
    /// Frame rate for output video
    /// For RIFE 2x interpolation: typically 60fps
    /// </summary>
    public int FrameRate { get; set; } = 60;

    /// <summary>
    /// Video codec when using hardware acceleration
    /// Options: "h264_nvenc" or "hevc_nvenc"
    /// Recommended: "hevc_nvenc" for better compression
    /// </summary>
    public string VideoCodec { get; set; } = "hevc_nvenc";

    /// <summary>
    /// NVENC preset: p1 (fastest) to p7 (slowest/best quality)
    /// Recommended: p7 for maximum quality (still WAY faster than CPU)
    /// RTX 3080 can handle p7 at 500+ fps
    /// </summary>
    public string NvencPreset { get; set; } = "p7";

    /// <summary>
    /// Rate control mode: "vbr" (variable bitrate) or "cq" (constant quality)
    /// Recommended: "vbr" for general use
    /// </summary>
    public string RateControl { get; set; } = "vbr";

    /// <summary>
    /// Quality level: 0-51 (lower = better quality)
    /// For NVENC: 18-23 recommended
    /// 19 = visually lossless
    /// </summary>
    public int Quality { get; set; } = 19;

    /// <summary>
    /// CPU preset (only used if hardware acceleration disabled)
    /// Options: ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow
    /// </summary>
    public string CpuPreset { get; set; } = "medium";

    /// <summary>
    /// CPU threads (only used if hardware acceleration disabled)
    /// </summary>
    public int CpuThreads { get; set; } = 0; // 0 = auto

    /// <summary>
    /// Output container format
    /// </summary>
    public string Container { get; set; } = "mp4";
}

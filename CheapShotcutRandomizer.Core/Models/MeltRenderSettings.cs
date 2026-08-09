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
    /// Video codec: CPU ("libx264", "libx265") or hardware
    /// ("h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_qsv", "h264_amf", "hevc_amf").
    /// Hardware encoders are faster but compress less efficiently than x264/x265.
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
    /// Quality: 0-51, lower = better. Applied as CRF for x264/x265,
    /// CQ (quality-based VBR) for NVENC, global_quality for QSV, CQP for AMF.
    /// 18 = visually lossless, 23 = default, 28 = lower quality
    /// </summary>
    public int? Crf { get; set; } = 23;

    /// <summary>
    /// Audio bitrate: "128k", "192k", "256k", etc. Ignored when AudioQuality is set.
    /// </summary>
    public string AudioBitrate { get; set; } = "128k";

    /// <summary>
    /// Audio quality for variable bitrate (melt "aq" property, codec-specific scale:
    /// AAC ~0.1-2 higher = better, MP3/LAME 0-9 lower = better). Null = average bitrate.
    /// </summary>
    public double? AudioQuality { get; set; }

    /// <summary>
    /// Output resolution override. Null = use the project profile.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Output resolution override. Null = use the project profile.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Display aspect ratio override (e.g. 16:9). Null = derived from resolution.
    /// Only applied when Width/Height are set.
    /// </summary>
    public int? DisplayAspectNum { get; set; }
    public int? DisplayAspectDen { get; set; }

    /// <summary>
    /// Frame rate override as a rational (e.g. 30000/1001 for 29.97). Null = project frame rate.
    /// </summary>
    public int? FrameRateNum { get; set; }
    public int? FrameRateDen { get; set; }
}

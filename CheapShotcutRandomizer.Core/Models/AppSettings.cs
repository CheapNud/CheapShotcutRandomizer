namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Application settings that persist across sessions
/// MLT/Shotcut focused - AI upscaling has been moved to CheapUpscaler
/// </summary>
public class AppSettings
{
    // Logging Settings
    public bool VerboseLogging { get; set; } = false;

    // SVP Integration (for FFmpeg detection)
    public bool UseSvpEncoders { get; set; } = true;

    // Path Settings
    public string FFmpegPath { get; set; } = "ffmpeg";
    public string FFprobePath { get; set; } = "ffprobe";
    public string MeltPath { get; set; } = "melt";

    // Render Default Settings
    public string DefaultQuality { get; set; } = "High";
    public string DefaultCodec { get; set; } = "libx264";
    public int DefaultCrf { get; set; } = 23;
    public string DefaultPreset { get; set; } = "medium";

    // Application Behavior
    public int MaxConcurrentRenders { get; set; } = 1;
    public bool AutoStartQueue { get; set; } = false;
    public bool ShowNotificationsOnComplete { get; set; } = true;
}

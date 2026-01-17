namespace CheapShotcutRandomizer.Models;

/// <summary>
/// Types of external dependencies required by the application
/// MLT/Shotcut focused - AI dependencies have been moved to CheapUpscaler
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// FFmpeg - video encoding/decoding tool (required)
    /// Can be sourced from Shotcut or SVP
    /// </summary>
    FFmpeg,

    /// <summary>
    /// FFprobe - video analysis tool (required)
    /// Usually bundled with FFmpeg
    /// </summary>
    FFprobe,

    /// <summary>
    /// Shotcut/MLT Melt - video project renderer (required)
    /// Required for rendering Shotcut projects
    /// </summary>
    Melt
}

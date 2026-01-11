namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Source file type for render job
/// </summary>
public enum RenderType
{
    /// <summary>
    /// Source is an MLT project file (.mlt)
    /// </summary>
    MltSource,

    /// <summary>
    /// Source is a video file (mp4, mkv, etc.)
    /// </summary>
    VideoSource
}

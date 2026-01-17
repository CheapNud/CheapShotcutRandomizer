namespace CheapShotcutRandomizer.Core.Services.Interfaces;

/// <summary>
/// FFmpeg abstraction for video encoding/decoding operations
/// </summary>
public interface IFFmpegService
{
    /// <summary>
    /// Get path to FFmpeg executable
    /// </summary>
    string GetFFmpegPath();

    /// <summary>
    /// Get path to FFprobe executable
    /// </summary>
    string GetFFprobePath();

    /// <summary>
    /// Check if NVENC hardware encoding is available
    /// </summary>
    Task<bool> IsNvencAvailableAsync();

    /// <summary>
    /// Check if NVDEC hardware decoding is available
    /// </summary>
    Task<bool> IsNvdecAvailableAsync();

    /// <summary>
    /// Configure FFMpegCore global options with detected paths
    /// </summary>
    void ConfigureFFmpegCore();

    /// <summary>
    /// Extract frames from video for processing
    /// </summary>
    Task<bool> ExtractFramesAsync(
        string videoPath,
        string outputFolder,
        double fps,
        bool useHardwareDecode = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract audio track losslessly
    /// </summary>
    Task<bool> ExtractAudioAsync(
        string videoPath,
        string audioOutputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reassemble frames into video with audio
    /// </summary>
    Task<bool> ReassembleVideoAsync(
        string framesFolder,
        string? audioPath,
        string outputPath,
        double fps,
        string codec,
        int crf,
        bool useHardwareEncode = true,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get video metadata (duration, fps, resolution, etc.)
    /// </summary>
    Task<VideoMetadata?> GetVideoMetadataAsync(string videoPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Video file metadata
/// </summary>
public record VideoMetadata(
    TimeSpan Duration,
    double FrameRate,
    int Width,
    int Height,
    string Codec,
    long FileSizeBytes,
    int TotalFrames);

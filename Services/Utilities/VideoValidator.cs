using FFMpegCore;
using System.Diagnostics;

namespace CheapShotcutRandomizer.Services.Utilities;

/// <summary>
/// Validates video files and frame sequences
/// </summary>
public class VideoValidator
{
    /// <summary>
    /// Validate that input video exists and is readable
    /// </summary>
    public async Task<VideoValidationResult> ValidateInputVideoAsync(string videoPath)
    {
        var validationResult = new VideoValidationResult { VideoPath = videoPath };

        // Check file exists
        if (!File.Exists(videoPath))
        {
            validationResult.IsValid = false;
            validationResult.ErrorMessage = "Video file not found";
            return validationResult;
        }

        // Check file size
        var fileInfo = new FileInfo(videoPath);
        if (fileInfo.Length == 0)
        {
            validationResult.IsValid = false;
            validationResult.ErrorMessage = "Video file is empty";
            return validationResult;
        }

        validationResult.FileSize = fileInfo.Length;

        try
        {
            // Analyze with FFProbe
            var mediaInfo = await FFProbe.AnalyseAsync(videoPath);

            if (mediaInfo == null)
            {
                validationResult.IsValid = false;
                validationResult.ErrorMessage = "Failed to analyze video";
                return validationResult;
            }

            // Check for video stream
            if (mediaInfo.VideoStreams.Count == 0)
            {
                validationResult.IsValid = false;
                validationResult.ErrorMessage = "No video stream found";
                return validationResult;
            }

            // Check duration
            if (mediaInfo.Duration == TimeSpan.Zero)
            {
                validationResult.IsValid = false;
                validationResult.ErrorMessage = "Video has zero duration";
                return validationResult;
            }

            // Populate validation result
            validationResult.Duration = mediaInfo.Duration;
            validationResult.Width = mediaInfo.PrimaryVideoStream?.Width ?? 0;
            validationResult.Height = mediaInfo.PrimaryVideoStream?.Height ?? 0;
            validationResult.FrameRate = mediaInfo.PrimaryVideoStream?.FrameRate ?? 0;
            validationResult.HasAudio = mediaInfo.AudioStreams.Count > 0;
            validationResult.IsValid = true;

            Debug.WriteLine($"Video validation passed: {videoPath}");
            Debug.WriteLine($"  Duration: {validationResult.Duration}");
            Debug.WriteLine($"  Resolution: {validationResult.Width}x{validationResult.Height}");
            Debug.WriteLine($"  Frame Rate: {validationResult.FrameRate} fps");
            Debug.WriteLine($"  Has Audio: {validationResult.HasAudio}");

            return validationResult;
        }
        catch (Exception ex)
        {
            validationResult.IsValid = false;
            validationResult.ErrorMessage = $"Error analyzing video: {ex.Message}";
            Debug.WriteLine($"Video validation failed: {ex.Message}");
            return validationResult;
        }
    }

    /// <summary>
    /// Validate output video matches expected properties
    /// </summary>
    public async Task<VideoValidationResult> ValidateOutputVideoAsync(
        string videoPath,
        TimeSpan expectedDuration,
        double toleranceSeconds = 2.0)
    {
        var validationResult = await ValidateInputVideoAsync(videoPath);

        if (!validationResult.IsValid)
            return validationResult;

        // Check duration matches (with tolerance)
        var durationDifference = Math.Abs((validationResult.Duration - expectedDuration).TotalSeconds);
        if (durationDifference > toleranceSeconds)
        {
            validationResult.IsValid = false;
            validationResult.ErrorMessage = $"Duration mismatch: expected {expectedDuration}, got {validationResult.Duration} (difference: {durationDifference:F1}s)";
            return validationResult;
        }

        return validationResult;
    }

    /// <summary>
    /// Validate frame sequence in a directory
    /// </summary>
    public FrameValidationResult ValidateFrameSequence(string framesDirectory, int expectedFrameCount)
    {
        var validationResult = new FrameValidationResult
        {
            FramesDirectory = framesDirectory,
            ExpectedFrameCount = expectedFrameCount
        };

        if (!Directory.Exists(framesDirectory))
        {
            validationResult.IsValid = false;
            validationResult.ErrorMessage = "Frames directory not found";
            return validationResult;
        }

        // Count frames (common image formats)
        var frameFiles = Directory.GetFiles(framesDirectory, "*.*")
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        validationResult.ActualFrameCount = frameFiles.Count;

        if (frameFiles.Count == 0)
        {
            validationResult.IsValid = false;
            validationResult.ErrorMessage = "No frame files found";
            return validationResult;
        }

        // Check frame count matches expected (allow off-by-one due to rounding differences)
        var frameDifference = Math.Abs(frameFiles.Count - expectedFrameCount);
        if (frameDifference > 1)
        {
            validationResult.IsValid = false;
            validationResult.ErrorMessage = $"Frame count mismatch: expected {expectedFrameCount}, found {frameFiles.Count} (difference: {frameDifference})";
            Debug.WriteLine($"Frame validation failed: {validationResult.ErrorMessage}");
            return validationResult;
        }
        else if (frameDifference == 1)
        {
            // Allow off-by-one but warn about it
            Debug.WriteLine($"Warning: Frame count slightly off (expected {expectedFrameCount}, found {frameFiles.Count}) - continuing anyway");
        }

        // Check for sequential naming (optional - just warn if missing)
        var hasSequentialNaming = CheckSequentialNaming(frameFiles);
        if (!hasSequentialNaming)
        {
            Debug.WriteLine("Warning: Frame files may not be sequentially named");
        }

        validationResult.IsValid = true;
        Debug.WriteLine($"Frame validation passed: {frameFiles.Count} frames in {framesDirectory}");

        return validationResult;
    }

    /// <summary>
    /// Check if frames are sequentially named
    /// </summary>
    private bool CheckSequentialNaming(List<string> frameFiles)
    {
        if (frameFiles.Count < 2)
            return true;

        // Just check first few frames for sequential pattern
        var checkCount = Math.Min(10, frameFiles.Count);
        for (int i = 0; i < checkCount - 1; i++)
        {
            var current = Path.GetFileNameWithoutExtension(frameFiles[i]);
            var next = Path.GetFileNameWithoutExtension(frameFiles[i + 1]);

            // Try to extract numbers and check if they're sequential
            if (int.TryParse(new string(current.Where(char.IsDigit).ToArray()), out var currentNum) &&
                int.TryParse(new string(next.Where(char.IsDigit).ToArray()), out var nextNum))
            {
                if (nextNum != currentNum + 1)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Calculate expected frame count from video
    /// </summary>
    public async Task<int> CalculateExpectedFrameCountAsync(string videoPath, double fps)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(videoPath);

        // Calculate from duration * fps
        // Use Floor instead of Ceiling to match FFmpeg's behavior
        // FFmpeg extracts exact frames, not rounded up
        var calculatedFrames = (int)Math.Floor(mediaInfo.Duration.TotalSeconds * fps);
        Debug.WriteLine($"Calculated frame count: {calculatedFrames} (duration: {mediaInfo.Duration.TotalSeconds}s, fps: {fps})");
        return calculatedFrames;
    }
}

/// <summary>
/// Result of video validation
/// </summary>
public class VideoValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string VideoPath { get; set; } = "";
    public long FileSize { get; set; }
    public TimeSpan Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public bool HasAudio { get; set; }
}

/// <summary>
/// Result of frame sequence validation
/// </summary>
public class FrameValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string FramesDirectory { get; set; } = "";
    public int ExpectedFrameCount { get; set; }
    public int ActualFrameCount { get; set; }
}

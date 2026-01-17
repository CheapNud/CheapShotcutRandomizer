using System.Diagnostics;

namespace CheapShotcutRandomizer.Services.Utilities;

/// <summary>
/// Parses and handles FFmpeg errors with helpful suggestions
/// </summary>
public class FFmpegErrorHandler
{
    /// <summary>
    /// Parse FFmpeg error output and provide helpful suggestions
    /// </summary>
    public FFmpegError ParseError(string errorOutput)
    {
        if (string.IsNullOrWhiteSpace(errorOutput))
        {
            return new FFmpegError
            {
                ErrorType = "Unknown",
                Message = "Unknown error occurred",
                Suggestion = "Check FFmpeg installation and input files"
            };
        }

        var lowerError = errorOutput.ToLowerInvariant();

        // File not found errors
        if (lowerError.Contains("no such file") || lowerError.Contains("does not exist"))
        {
            return new FFmpegError
            {
                ErrorType = "FileNotFound",
                Message = "Input file not found",
                Suggestion = "Verify the input file path exists and is accessible"
            };
        }

        // Permission errors
        if (lowerError.Contains("permission denied") || lowerError.Contains("access denied"))
        {
            return new FFmpegError
            {
                ErrorType = "PermissionDenied",
                Message = "Permission denied accessing file",
                Suggestion = "Check file permissions or try running with administrator privileges"
            };
        }

        // Corrupt file errors
        if (lowerError.Contains("invalid data") || lowerError.Contains("corrupt") ||
            lowerError.Contains("header missing") || lowerError.Contains("moov atom not found"))
        {
            return new FFmpegError
            {
                ErrorType = "CorruptFile",
                Message = "Input file appears to be corrupted or incomplete",
                Suggestion = "Try re-downloading or re-creating the input file. For partially downloaded files, ensure the download completed successfully."
            };
        }

        // Codec errors
        if (lowerError.Contains("codec not supported") || lowerError.Contains("unknown codec") ||
            lowerError.Contains("decoder not found") || lowerError.Contains("encoder not found"))
        {
            return new FFmpegError
            {
                ErrorType = "CodecNotSupported",
                Message = "Codec not supported by FFmpeg",
                Suggestion = "Install FFmpeg with full codec support or convert the file to a supported format (H.264/H.265)"
            };
        }

        // NVENC errors
        if (lowerError.Contains("nvenc") || lowerError.Contains("cuda"))
        {
            if (lowerError.Contains("driver") || lowerError.Contains("not found"))
            {
                return new FFmpegError
                {
                    ErrorType = "NvencDriverError",
                    Message = "NVIDIA driver issue detected",
                    Suggestion = "Update NVIDIA drivers to the latest version. NVENC requires driver version 471.41 or newer."
                };
            }

            if (lowerError.Contains("out of memory") || lowerError.Contains("vram"))
            {
                return new FFmpegError
                {
                    ErrorType = "NvencOutOfMemory",
                    Message = "GPU out of memory",
                    Suggestion = "Close other GPU-intensive applications or reduce video resolution. RTX 3080 has 10GB VRAM."
                };
            }

            return new FFmpegError
            {
                ErrorType = "NvencError",
                Message = "NVENC hardware acceleration error",
                Suggestion = "Try disabling hardware acceleration (UseHardwareAcceleration = false) or update NVIDIA drivers"
            };
        }

        // Out of memory errors
        if (lowerError.Contains("out of memory") || lowerError.Contains("cannot allocate memory"))
        {
            return new FFmpegError
            {
                ErrorType = "OutOfMemory",
                Message = "System out of memory",
                Suggestion = "Close other applications to free up RAM or process a shorter video segment"
            };
        }

        // Disk space errors
        if (lowerError.Contains("no space left") || lowerError.Contains("disk full"))
        {
            return new FFmpegError
            {
                ErrorType = "DiskFull",
                Message = "Insufficient disk space",
                Suggestion = "Free up disk space on the output drive. Video processing requires significant temporary storage."
            };
        }

        // Frame rate errors
        if (lowerError.Contains("frame rate") || lowerError.Contains("invalid framerate"))
        {
            return new FFmpegError
            {
                ErrorType = "InvalidFrameRate",
                Message = "Invalid frame rate specified",
                Suggestion = "Verify the input video has a valid frame rate (common: 24, 30, 60 fps)"
            };
        }

        // Resolution errors
        if (lowerError.Contains("resolution") || lowerError.Contains("invalid width") ||
            lowerError.Contains("invalid height"))
        {
            return new FFmpegError
            {
                ErrorType = "InvalidResolution",
                Message = "Invalid video resolution",
                Suggestion = "Verify the video resolution is valid (width and height must be positive even numbers)"
            };
        }

        // Generic error
        Debug.WriteLine($"Unrecognized FFmpeg error: {errorOutput}");

        return new FFmpegError
        {
            ErrorType = "Unknown",
            Message = "FFmpeg processing error",
            Suggestion = "Check FFmpeg logs for details. The error output may contain specific information about what went wrong.",
            RawError = errorOutput
        };
    }

    /// <summary>
    /// Get user-friendly error message with suggestions
    /// </summary>
    public string GetUserFriendlyMessage(FFmpegError ffmpegError)
    {
        return $"{ffmpegError.Message}\n\nSuggestion: {ffmpegError.Suggestion}";
    }
}

/// <summary>
/// Represents a parsed FFmpeg error
/// </summary>
public class FFmpegError
{
    /// <summary>
    /// Type of error (FileNotFound, PermissionDenied, etc.)
    /// </summary>
    public string ErrorType { get; set; } = "Unknown";

    /// <summary>
    /// User-friendly error message
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Suggested solution
    /// </summary>
    public string Suggestion { get; set; } = "";

    /// <summary>
    /// Raw error output from FFmpeg (optional)
    /// </summary>
    public string? RawError { get; set; }

    public override string ToString()
    {
        return $"[{ErrorType}] {Message}\nSuggestion: {Suggestion}";
    }
}

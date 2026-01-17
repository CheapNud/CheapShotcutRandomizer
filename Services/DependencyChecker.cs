using System.Diagnostics;
using CheapShotcutRandomizer.Models;
using CheapHelpers.MediaProcessing.Services;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Detects and validates external dependencies required by the application
/// Integrates with existing ExecutableDetectionService and SvpDetectionService
/// MLT-focused: checks FFmpeg, FFprobe, Melt (Shotcut) dependencies
/// </summary>
public class DependencyChecker(
    ExecutableDetectionService executableDetection,
    SvpDetectionService svpDetection)
{
    private readonly ExecutableDetectionService _executableDetection = executableDetection;
    private readonly SvpDetectionService _svpDetection = svpDetection;

    /// <summary>
    /// Check all dependencies and return comprehensive status
    /// MLT-focused: FFmpeg, FFprobe, Melt (Shotcut)
    /// </summary>
    public async Task<DependencyStatus> CheckAllDependenciesAsync()
    {
        var allDependencies = new List<DependencyInfo>();

        // Check MLT-related dependencies only
        allDependencies.Add(await CheckDependencyAsync(DependencyType.FFmpeg));
        allDependencies.Add(await CheckDependencyAsync(DependencyType.FFprobe));
        allDependencies.Add(await CheckDependencyAsync(DependencyType.Melt));

        Debug.WriteLine("=== Dependency Check Complete ===");
        Debug.WriteLine($"Total dependencies: {allDependencies.Count}");
        Debug.WriteLine($"Required installed: {allDependencies.Count(d => d.IsRequired && d.IsInstalled)}/{allDependencies.Count(d => d.IsRequired)}");
        Debug.WriteLine($"Optional installed: {allDependencies.Count(d => !d.IsRequired && d.IsInstalled)}/{allDependencies.Count(d => !d.IsRequired)}");
        Debug.WriteLine("=================================");

        return new DependencyStatus
        {
            AllDependencies = allDependencies
        };
    }

    /// <summary>
    /// Check a specific dependency type
    /// MLT-focused: supports FFmpeg, FFprobe, Melt only
    /// </summary>
    public async Task<DependencyInfo> CheckDependencyAsync(DependencyType type)
    {
        return type switch
        {
            DependencyType.FFmpeg => await CheckFFmpegAsync(),
            DependencyType.FFprobe => await CheckFFprobeAsync(),
            DependencyType.Melt => await CheckMeltAsync(),
            _ => throw new ArgumentException($"Unsupported dependency type for CheapShotcutRandomizer: {type}", nameof(type))
        };
    }

    private async Task<DependencyInfo> CheckFFmpegAsync()
    {
        var ffmpegPath = _executableDetection.DetectFFmpeg(useSvpEncoders: true, customPath: null);
        var isInstalled = ffmpegPath != null;
        string? version = null;

        if (isInstalled)
        {
            version = await GetFFmpegVersionAsync(ffmpegPath!);
        }

        return new DependencyInfo
        {
            Type = DependencyType.FFmpeg,
            Name = "FFmpeg",
            Description = "Video encoding and decoding tool. Required for all video processing operations.",
            IsInstalled = isInstalled,
            IsRequired = true,
            InstalledVersion = version,
            InstalledPath = ffmpegPath,
            DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/",
            ChocolateyPackage = "ffmpeg",
            SupportsAutomatedInstall = true,
            SupportsPortableInstall = true,
            PortableDownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
            InstallInstructions = @"**Option 1: Via Chocolatey**
```
choco install ffmpeg
```

**Option 2: Via Shotcut**
Install Shotcut, which includes FFmpeg: https://shotcut.org/download/

**Option 3: Via SVP 4 Pro**
Install SVP 4 Pro, which includes optimized FFmpeg: https://www.svp-team.com/get/

**Option 4: Portable**
Download FFmpeg essentials from https://www.gyan.dev/ffmpeg/builds/
Extract to a folder and point the app to ffmpeg.exe",
            DetectionMessage = isInstalled
                ? $"FFmpeg found at: {ffmpegPath}"
                : "FFmpeg not found. Install Shotcut, SVP, or download standalone FFmpeg."
        };
    }

    private async Task<DependencyInfo> CheckFFprobeAsync()
    {
        var ffprobePath = _executableDetection.DetectFFprobe(useSvpEncoders: true, customPath: null);
        var isInstalled = ffprobePath != null;
        string? version = null;

        if (isInstalled)
        {
            version = await GetFFprobeVersionAsync(ffprobePath!);
        }

        return new DependencyInfo
        {
            Type = DependencyType.FFprobe,
            Name = "FFprobe",
            Description = "Video analysis and metadata extraction tool. Required for video validation and info retrieval.",
            IsInstalled = isInstalled,
            IsRequired = true,
            InstalledVersion = version,
            InstalledPath = ffprobePath,
            DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/",
            ChocolateyPackage = "ffmpeg", // FFprobe comes with FFmpeg
            SupportsAutomatedInstall = true,
            SupportsPortableInstall = true,
            PortableDownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
            InstallInstructions = "FFprobe is included with FFmpeg. Install FFmpeg to get FFprobe.",
            DetectionMessage = isInstalled
                ? $"FFprobe found at: {ffprobePath}"
                : "FFprobe not found. Usually installed with FFmpeg."
        };
    }

    private async Task<DependencyInfo> CheckMeltAsync()
    {
        var meltPath = _executableDetection.DetectMelt(customPath: null);
        var isInstalled = meltPath != null;
        string? version = null;

        if (isInstalled)
        {
            version = await GetMeltVersionAsync(meltPath!);
        }

        return new DependencyInfo
        {
            Type = DependencyType.Melt,
            Name = "Shotcut Melt",
            Description = "MLT Framework renderer for Shotcut projects. Required to render Shotcut .mlt project files.",
            IsInstalled = isInstalled,
            IsRequired = true,
            InstalledVersion = version,
            InstalledPath = meltPath,
            DownloadUrl = "https://shotcut.org/download/",
            ChocolateyPackage = null, // Shotcut doesn't have a reliable choco package
            SupportsAutomatedInstall = false,
            SupportsPortableInstall = true,
            PortableDownloadUrl = "https://shotcut.org/download/",
            InstallInstructions = @"**Install Shotcut**
Download and install Shotcut from: https://shotcut.org/download/

Shotcut includes the 'melt' executable required for rendering projects.
After installation, melt.exe will be in the Shotcut installation folder.",
            DetectionMessage = isInstalled
                ? $"Melt found at: {meltPath}"
                : "Melt not found. Please install Shotcut."
        };
    }

    // Helper methods for version detection

    private async Task<string?> GetFFmpegVersionAsync(string ffmpegPath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var firstLine = output.Split('\n')[0];
                var versionMatch = System.Text.RegularExpressions.Regex.Match(firstLine, @"ffmpeg version ([\d\.]+)");
                if (versionMatch.Success)
                {
                    return versionMatch.Groups[1].Value;
                }
                return firstLine.Trim();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get FFmpeg version: {ex.Message}");
        }

        return null;
    }

    private async Task<string?> GetFFprobeVersionAsync(string ffprobePath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var firstLine = output.Split('\n')[0];
                var versionMatch = System.Text.RegularExpressions.Regex.Match(firstLine, @"ffprobe version ([\d\.]+)");
                if (versionMatch.Success)
                {
                    return versionMatch.Groups[1].Value;
                }
                return firstLine.Trim();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get FFprobe version: {ex.Message}");
        }

        return null;
    }

    private async Task<string?> GetMeltVersionAsync(string meltPath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = meltPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(output))
            {
                var firstLine = output.Split('\n')[0];
                return firstLine.Trim();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get Melt version: {ex.Message}");
        }

        return null;
    }


    private async Task<string?> GetExecutablePathAsync(string executableName)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = executableName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var firstPath = output.Split('\n')[0].Trim();
                return File.Exists(firstPath) ? firstPath : null;
            }
        }
        catch
        {
            // where command not available or error
        }

        return null;
    }
}

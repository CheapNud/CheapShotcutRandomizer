using System.Diagnostics;
using System.Text.RegularExpressions;
using CheapShotcutRandomizer.Models;
using CheapShotcutRandomizer.Services.Utilities;
using CheapShotcutRandomizer.Core.Models;
using CheapHelpers.Services.DataExchange.Xml;
using CheapHelpers.MediaProcessing.Services.Utilities;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Melt-based rendering service for MLT project files
/// IMPORTANT: Uses CPU multi-threading, NOT NVENC (MLT's NVENC is broken/slow)
/// </summary>
public class MeltRenderService
{
    private static readonly Regex ProgressRegex = new(
        @"Current Frame:\s+(\d+),\s+percentage:\s+(\d+)",
        RegexOptions.Compiled
    );

    private readonly string _meltExecutable;
    private readonly IXmlService _xmlService;
    private readonly ShotcutService _shotcutService;

    public MeltRenderService(string meltExecutable = "melt", IXmlService? xmlService = null, ShotcutService? shotcutService = null)
    {
        _meltExecutable = meltExecutable;
        _xmlService = xmlService ?? throw new ArgumentNullException(nameof(xmlService));
        _shotcutService = shotcutService ?? throw new ArgumentNullException(nameof(shotcutService));
    }

    /// <summary>
    /// Render an MLT project using CPU multi-threading
    /// DO NOT pass UseHardwareAcceleration=true - MLT's NVENC is 2x SLOWER than CPU
    /// </summary>
    /// <param name="inPoint">Optional in point (frame number). If null, render from start.</param>
    /// <param name="outPoint">Optional out point (frame number). If null, render to end.</param>
    /// <param name="selectedVideoTracks">Comma-separated track indices to render video from. If null, render all video tracks.</param>
    /// <param name="selectedAudioTracks">Comma-separated track indices to render audio from. If null, render all audio tracks.</param>
    public async Task<bool> RenderAsync(
        string mltFilePath,
        string outputPath,
        MeltRenderSettings settings,
        IProgress<RenderProgress> progress = null,
        CancellationToken cancellationToken = default,
        int? inPoint = null,
        int? outPoint = null,
        string? selectedVideoTracks = null,
        string? selectedAudioTracks = null)
    {
        if (settings == null)
        {
            Debug.WriteLine("ERROR: MeltRenderSettings is null in RenderAsync");
            return false;
        }

        if (settings.UseHardwareAcceleration)
        {
            Debug.WriteLine("WARNING: Hardware acceleration requested for melt, but it's 2x SLOWER than CPU!");
            Debug.WriteLine("Ignoring UseHardwareAcceleration and using CPU multi-threading instead");
        }

        // Apply track selection if specified, using TemporaryFileManager for cleanup
        // Temp file must be in source directory to preserve relative paths in MLT
        string actualMltPath = mltFilePath;
        TemporaryFileManager? tempManager = null;

        if (!string.IsNullOrEmpty(selectedVideoTracks) || !string.IsNullOrEmpty(selectedAudioTracks))
        {
            var sourceDir = Path.GetDirectoryName(mltFilePath);
            tempManager = new TemporaryFileManager(sourceDir);
            actualMltPath = await ApplyTrackSelectionAsync(mltFilePath, selectedVideoTracks, selectedAudioTracks, tempManager);
        }

        try
        {
            var arguments = BuildMeltArguments(actualMltPath, outputPath, settings, inPoint, outPoint);
            Debug.WriteLine($"melt command: {_meltExecutable} {arguments}");

            var startTime = DateTime.Now;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _meltExecutable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            var tcs = new TaskCompletionSource<bool>();

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                // Parse progress from stderr
                var match = ProgressRegex.Match(e.Data);
                if (match.Success)
                {
                    var currentFrame = int.Parse(match.Groups[1].Value);
                    var percentage = int.Parse(match.Groups[2].Value);

                    progress?.Report(new RenderProgress
                    {
                        CurrentFrame = currentFrame,
                        Percentage = percentage,
                        ElapsedTime = DateTime.Now - startTime
                    });
                }
                else
                {
                    Debug.WriteLine($"melt: {e.Data}");
                }
            };

            process.Exited += (sender, e) =>
            {
                tcs.SetResult(process.ExitCode == 0);
            };

            // Register cancellation with graceful shutdown
            cancellationToken.Register(async () =>
            {
                Debug.WriteLine("Melt render cancelled - initiating graceful shutdown...");

                // Use ProcessManager for graceful shutdown with process tree cleanup
                await ProcessManager.GracefulShutdownAsync(
                    process,
                    gracefulTimeoutMs: 3000,
                    processName: "melt");
            });

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"melt execution error: {ex.Message}");
            return false;
        }
        finally
        {
            // Temp file cleanup handled by TemporaryFileManager
            tempManager?.Dispose();
        }
    }

    /// <summary>
    /// Apply track selection to an MLT project by creating a modified copy
    /// IMPORTANT: System tracks (like "black" background) are NEVER hidden - they're required for rendering
    /// </summary>
    private async Task<string> ApplyTrackSelectionAsync(string mltFilePath, string? selectedVideoTracks, string? selectedAudioTracks, TemporaryFileManager tempManager)
    {
        Debug.WriteLine($"Applying track selection - Video: {selectedVideoTracks}, Audio: {selectedAudioTracks}");

        // Load the MLT project
        var project = await _shotcutService.LoadProjectAsync(mltFilePath);
        if (project == null)
            throw new InvalidOperationException("Failed to load MLT project for track selection");

        // Get all tracks (this already excludes system tracks from user selection)
        var tracks = _shotcutService.GetTracks(project);

        // Parse selected track indices
        var selectedVideoIndices = string.IsNullOrEmpty(selectedVideoTracks)
            ? null
            : selectedVideoTracks.Split(',').Select(int.Parse).ToHashSet();

        var selectedAudioIndices = string.IsNullOrEmpty(selectedAudioTracks)
            ? null
            : selectedAudioTracks.Split(',').Select(int.Parse).ToHashSet();

        // Find the main tractor
        var mainTractor = project.Tractor?.FirstOrDefault(t =>
            t.Property?.Any(p => p.Name == "shotcut") ?? false);

        if (mainTractor?.Track == null)
            throw new InvalidOperationException("No main tractor found in MLT project");

        // Apply hide attributes based on track selection
        foreach (var trackInfo in tracks)
        {
            var track = mainTractor.Track.FirstOrDefault(t => t.Producer == trackInfo.ProducerId);
            if (track == null)
                continue;

            // CRITICAL: Never hide system tracks - they're required for rendering
            if (IsSystemTrack(trackInfo.ProducerId))
            {
                Debug.WriteLine($"System track '{trackInfo.ProducerId}' - always visible (required for rendering)");
                continue;
            }

            bool hideVideo = false;
            bool hideAudio = false;

            // Determine what to hide based on track type and selection
            if (trackInfo.Type == "video")
            {
                // If this video track is NOT selected, hide video
                hideVideo = selectedVideoIndices != null && !selectedVideoIndices.Contains(trackInfo.Index);
            }
            else if (trackInfo.Type == "audio")
            {
                // If this audio track is NOT selected, hide audio
                hideAudio = selectedAudioIndices != null && !selectedAudioIndices.Contains(trackInfo.Index);
            }

            // Set the hide attribute
            if (hideVideo && hideAudio)
            {
                track.Hide = "both";
            }
            else if (hideVideo)
            {
                track.Hide = "video";
            }
            else if (hideAudio)
            {
                track.Hide = "audio";
            }
            else
            {
                track.Hide = null; // Show both
            }

            Debug.WriteLine($"Track {trackInfo.Index} ({trackInfo.Name}): hide={track.Hide ?? "none"}");
        }

        // Save modified project to a temporary file using TemporaryFileManager
        var tempPath = tempManager.GetTempFilePath("melt_tracks", ".mlt");

        await _xmlService.SerializeAsync(tempPath, project);
        Debug.WriteLine($"Created temporary MLT with track selection: {tempPath}");

        return tempPath;
    }

    /// <summary>
    /// Determines if a track is a system track that should never be hidden
    /// System tracks include:
    /// - "black" background track (required for rendering)
    /// - Any other special system producers
    /// </summary>
    private static bool IsSystemTrack(string producerId)
    {
        if (string.IsNullOrEmpty(producerId))
            return false;

        // The "black" producer is the primary system track
        // It provides the background/base layer for rendering
        return producerId.Equals("black", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildMeltArguments(string mltFile, string outputPath, MeltRenderSettings settings, int? inPoint = null, int? outPoint = null)
    {
        var args = new List<string>();

        // Input MLT file
        args.Add($"\"{mltFile}\"");

        // In/Out points for partial rendering
        if (inPoint.HasValue)
        {
            args.Add($"in={inPoint.Value}");
            Debug.WriteLine($"Render starting from frame {inPoint.Value}");
        }

        if (outPoint.HasValue)
        {
            args.Add($"out={outPoint.Value}");
            Debug.WriteLine($"Render ending at frame {outPoint.Value}");
        }

        if (inPoint.HasValue && outPoint.HasValue)
        {
            var frameCount = outPoint.Value - inPoint.Value;
            Debug.WriteLine($"Rendering {frameCount} frames (partial timeline)");
        }
        else if (!inPoint.HasValue && !outPoint.HasValue)
        {
            Debug.WriteLine("Rendering full timeline");
        }

        // Progress reporting (use -progress2 for line-by-line output)
        args.Add("-progress2");

        // Consumer and output
        args.Add($"-consumer avformat:\"{outputPath}\"");

        // Video codec - ALWAYS use CPU codec (libx264 or libx265)
        args.Add($"vcodec={settings.VideoCodec}");

        // Audio codec
        args.Add($"acodec={settings.AudioCodec}");

        // Quality settings (CRF for constant quality)
        if (settings.Crf.HasValue)
        {
            args.Add($"crf={settings.Crf.Value}");
        }

        // Encoding preset (speed vs compression)
        if (!string.IsNullOrEmpty(settings.Preset))
        {
            args.Add($"preset={settings.Preset}");
        }

        // Audio bitrate
        if (!string.IsNullOrEmpty(settings.AudioBitrate))
        {
            args.Add($"ab={settings.AudioBitrate}");
        }

        // CRITICAL: CPU multi-threading
        // Use NEGATIVE value to disable frame dropping (for file rendering)
        // Use all available cores for maximum performance
        var threadCount = settings.ThreadCount > 0
            ? settings.ThreadCount
            : Environment.ProcessorCount;

        args.Add($"real_time=-{threadCount}");

        Debug.WriteLine($"Using CPU multi-threading with {threadCount} cores");

        // MP4 optimization
        if (outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("movflags=+faststart"); // Enable web streaming
        }

        return string.Join(" ", args);
    }
}

public class RenderProgress
{
    public int CurrentFrame { get; set; }
    public int Percentage { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
}

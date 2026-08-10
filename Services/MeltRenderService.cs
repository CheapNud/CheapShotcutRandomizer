using System.Diagnostics;
using System.Text.RegularExpressions;
using CheapShotcutRandomizer.Models;
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
        IProgress<RenderProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int? inPoint = null,
        int? outPoint = null,
        string? selectedVideoTracks = null,
        string? selectedAudioTracks = null,
        Guid? jobId = null,
        RenderProcessRegistry? processRegistry = null)
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

        try
        {
            if (!string.IsNullOrEmpty(selectedVideoTracks) || !string.IsNullOrEmpty(selectedAudioTracks))
            {
                var sourceDir = Path.GetDirectoryName(mltFilePath)
                    ?? throw new ArgumentException("Cannot determine source directory from MLT path", nameof(mltFilePath));
                tempManager = new TemporaryFileManager(sourceDir);
                actualMltPath = await ApplyTrackSelectionAsync(mltFilePath, selectedVideoTracks, selectedAudioTracks, tempManager);
            }
            var arguments = BuildMeltArguments(actualMltPath, outputPath, settings, inPoint, outPoint);
            Debug.WriteLine($"melt command: {_meltExecutable} {arguments}");

            var startTime = DateTime.Now;

            using var process = new Process
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

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
                tcs.TrySetResult(process.ExitCode == 0);
            };

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            // Keep the machine usable while a batch renders in the background
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

            // Register for in-place pause/resume (process suspension)
            if (jobId.HasValue && processRegistry != null)
            {
                processRegistry.Register(jobId.Value, process);
            }

            // Registered after Start so the callback can never run against an unstarted process
            using var cancelRegistration = cancellationToken.Register(() =>
            {
                Debug.WriteLine("Melt render cancelled - initiating graceful shutdown...");

                // Use ProcessManager for graceful shutdown with process tree cleanup
                _ = ProcessManager.GracefulShutdownAsync(
                    process,
                    gracefulTimeoutMs: 3000,
                    processName: "melt");
            });

            var success = await tcs.Task;

            // A cancelled kill must surface as cancellation, not as a failed render —
            // otherwise the queue's retry logic resurrects cancelled/paused jobs
            cancellationToken.ThrowIfCancellationRequested();

            return success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"melt execution error: {ex.Message}");
            return false;
        }
        finally
        {
            if (jobId.HasValue && processRegistry != null)
            {
                processRegistry.Unregister(jobId.Value);
            }

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

        // Render-only XML hardening (Shotcut does the same on export):
        // autoclose frees file handles as the playlist advances - matters for
        // randomized playlists with hundreds of clips
        foreach (var playlist in project.Playlist)
        {
            playlist.Autoclose = "1";
        }

        StripProxyResources(project);

        // Save modified project to a temporary file using TemporaryFileManager
        var tempPath = tempManager.GetTempFilePath("melt_tracks", ".mlt");

        await _xmlService.SerializeAsync(tempPath, project);
        Debug.WriteLine($"Created temporary MLT with track selection: {tempPath}");

        return tempPath;
    }

    /// <summary>
    /// If a producer still points at a Shotcut proxy (shotcut:proxy is set), restore the
    /// original file from shotcut:resource so the render never silently uses 540p proxies.
    /// Normal Shotcut saves are already clean; this guards against export-temp leftovers.
    /// </summary>
    private static void StripProxyResources(Mlt project)
    {
        foreach (var propertyList in project.Chain.Select(c => c.Property)
                     .Append(project.Producer?.Property ?? []))
        {
            var proxyMarker = propertyList.FirstOrDefault(p => p.Name == "shotcut:proxy");
            if (proxyMarker == null)
                continue;

            var originalResource = propertyList.FirstOrDefault(p => p.Name == "shotcut:resource")?.Text;
            var resource = propertyList.FirstOrDefault(p => p.Name == "resource");

            if (resource != null && !string.IsNullOrEmpty(originalResource))
            {
                Debug.WriteLine($"Restoring proxied resource to original: {originalResource}");
                resource.Text = originalResource;
            }

            propertyList.RemoveAll(p => p.Name is "shotcut:proxy" or "shotcut:resource");
        }
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

        // Progress reporting (use -progress2 for line-by-line output);
        // -verbose makes failure logs actionable
        args.Add("-verbose");
        args.Add("-progress2");

        // Consumer and output
        args.Add($"-consumer avformat:\"{outputPath}\"");

        if (settings.PresetProperties is { Count: > 0 })
        {
            // A stock MLT export preset governs format/codec/quality wholesale
            foreach (var (presetKey, presetValue) in settings.PresetProperties)
            {
                if (!presetKey.StartsWith("meta."))
                {
                    args.Add($"{presetKey}={presetValue}");
                }
            }
        }
        else
        {
        // Video codec - CPU (libx264/libx265) or hardware (NVENC/QSV/AMF)
        args.Add($"vcodec={settings.VideoCodec}");

        // Audio codec
        args.Add($"acodec={settings.AudioCodec}");

        // Quality settings - each encoder family takes a different property
        // (verified against Shotcut's encodedock.cpp emissions per encoder)
        if (settings.Crf.HasValue)
        {
            if (settings.VideoCodec.Contains("nvenc"))
            {
                args.Add("rc=vbr");
                args.Add($"cq={settings.Crf.Value}");
            }
            else if (settings.VideoCodec.Contains("qsv"))
            {
                // Shotcut emits MLT's qscale (min 1), not the raw global_quality AVOption
                args.Add($"qscale={Math.Max(1, settings.Crf.Value)}");
            }
            else if (settings.VideoCodec.Contains("amf"))
            {
                args.Add("rc=cqp");
                args.Add($"qp_i={settings.Crf.Value}");
                args.Add($"qp_p={settings.Crf.Value}");
                args.Add($"qp_b={settings.Crf.Value}");
            }
            else if (settings.VideoCodec == "libx265")
            {
                // x265 wants its options via x265-params; the bare crf is kept alongside
                // for readability (Shotcut sets both)
                args.Add($"x265-params=crf={settings.Crf.Value}");
                args.Add($"crf={settings.Crf.Value}");
            }
            else
            {
                args.Add($"crf={settings.Crf.Value}");
            }
        }

        // Encoding preset only applies to the CPU x264/x265 encoders;
        // hardware encoders use their own driver-managed presets
        if (!string.IsNullOrEmpty(settings.Preset) && settings.VideoCodec.StartsWith("libx"))
        {
            args.Add($"preset={settings.Preset}");
        }

        // GOP / B-frames (Shotcut parity: g=round(fps*5), bf=3, bf=0 for some hw encoders)
        if (settings.Gop.HasValue)
        {
            args.Add($"g={settings.Gop.Value}");
        }
        if (settings.BFrames.HasValue)
        {
            args.Add($"bf={settings.BFrames.Value}");
        }

        // Codec threads: MLT's default is 1(!). 0 = codec auto for x264/x265;
        // hardware encoders get cores-1 like Shotcut does
        args.Add(settings.VideoCodec.StartsWith("libx")
            ? "threads=0"
            : $"threads={Math.Max(1, Environment.ProcessorCount - 1)}");

        // Audio: quality-based VBR (aq, codec-specific scale) takes precedence over
        // average bitrate; FLAC is lossless so neither applies. The vbr marker
        // matches Shotcut (on for quality mode, constrained for average bitrate)
        if (settings.AudioCodec != "flac")
        {
            if (settings.AudioQuality.HasValue)
            {
                args.Add("vbr=on");
                args.Add($"aq={settings.AudioQuality.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
            else if (!string.IsNullOrEmpty(settings.AudioBitrate))
            {
                args.Add("vbr=constrained");
                args.Add($"ab={settings.AudioBitrate}");
            }
        }

        // MP4 optimization (presets carry their own movflags)
        if (outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("movflags=+faststart"); // Enable web streaming
        }
        }

        // Scaling/deinterlacing quality - Shotcut's defaults; MLT's own differ
        args.Add("rescale=bilinear");
        args.Add("deinterlacer=bwdif");

        // Optional output profile overrides (default: the project profile) - the same
        // consumer properties Shotcut's export panel emits
        if (settings.Width.HasValue && settings.Height.HasValue)
        {
            args.Add($"width={settings.Width.Value}");
            args.Add($"height={settings.Height.Value}");

            var darNum = settings.DisplayAspectNum ?? settings.Width.Value;
            var darDen = settings.DisplayAspectDen ?? settings.Height.Value;
            args.Add($"display_aspect_num={darNum}");
            args.Add($"display_aspect_den={darDen}");

            // Pixel (sample) aspect = DAR / (W/H), reduced
            var sarNum = darNum * settings.Height.Value;
            var sarDen = darDen * settings.Width.Value;
            var divisor = Gcd(sarNum, sarDen);
            args.Add($"sample_aspect_num={sarNum / divisor}");
            args.Add($"sample_aspect_den={sarDen / divisor}");
        }

        if (settings.FrameRateNum.HasValue)
        {
            args.Add($"frame_rate_num={settings.FrameRateNum.Value}");
            args.Add($"frame_rate_den={settings.FrameRateDen ?? 1}");
        }

        // CRITICAL: negative real_time disables frame dropping (required for file
        // rendering). These are MLT frame-processing threads, each holding full frame
        // buffers - Shotcut caps them at 4 to avoid OOM/artifacts on many-core boxes
        var threadCount = settings.ThreadCount > 0
            ? settings.ThreadCount
            : Environment.ProcessorCount;
        threadCount = Math.Clamp(threadCount, 1, 4);

        args.Add($"real_time=-{threadCount}");

        Debug.WriteLine($"Using {threadCount} MLT processing threads");

        return string.Join(" ", args);
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}

public class RenderProgress
{
    public int CurrentFrame { get; set; }
    public int Percentage { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
}

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

        // Every render goes through a render-only XML copy in the system temp dir
        // (Shotcut's invocation shape): absolute media paths, autoclose, proxy guard,
        // optional track selection, and the avformat consumer embedded as an XML element
        TemporaryFileManager? tempManager = null;

        try
        {
            tempManager = new TemporaryFileManager(Path.GetTempPath());
            var actualMltPath = await PrepareRenderXmlAsync(
                mltFilePath, outputPath, settings, selectedVideoTracks, selectedAudioTracks, tempManager);

            var arguments = BuildMeltArguments(actualMltPath, settings, inPoint, outPoint);
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
    /// Build the render-only XML copy melt actually consumes: track selection applied,
    /// media paths made absolute (the copy lives in system temp), playlists autoclosed,
    /// proxy resources restored, and the avformat consumer embedded as an XML element.
    /// IMPORTANT: System tracks (like "black" background) are NEVER hidden - they're required for rendering
    /// </summary>
    private async Task<string> PrepareRenderXmlAsync(
        string mltFilePath,
        string outputPath,
        MeltRenderSettings settings,
        string? selectedVideoTracks,
        string? selectedAudioTracks,
        TemporaryFileManager tempManager)
    {
        // Load the MLT project
        var project = await _shotcutService.LoadProjectAsync(mltFilePath);
        if (project == null)
            throw new InvalidOperationException("Failed to load MLT project for rendering");

        if (!string.IsNullOrEmpty(selectedVideoTracks) || !string.IsNullOrEmpty(selectedAudioTracks))
        {
            Debug.WriteLine($"Applying track selection - Video: {selectedVideoTracks}, Audio: {selectedAudioTracks}");
            ApplyTrackSelection(project, selectedVideoTracks, selectedAudioTracks);
        }

        // Render-only XML hardening (Shotcut does the same on export):
        // autoclose frees file handles as the playlist advances - matters for
        // randomized playlists with hundreds of clips
        foreach (var playlist in project.Playlist)
        {
            playlist.Autoclose = "1";
        }

        StripProxyResources(project);

        // The render copy lives in system temp, so relative media paths must become
        // absolute against the source project's directory (Shotcut exports absolute too)
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(mltFilePath)) ?? "";
        MakeResourcePathsAbsolute(project, sourceDir);

        var tempPath = tempManager.GetTempFilePath("melt_render", ".mlt");
        await _xmlService.SerializeAsync(tempPath, project);

        // Embed the avformat consumer as an XML element after the profile -
        // the same document shape Shotcut hands to melt
        InjectConsumerElement(tempPath, outputPath, settings);

        Debug.WriteLine($"Created render XML: {tempPath}");
        return tempPath;
    }

    private void ApplyTrackSelection(Mlt project, string? selectedVideoTracks, string? selectedAudioTracks)
    {
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
    }

    /// <summary>
    /// Rewrite relative media resources to absolute paths. Only paths that actually
    /// resolve to an existing file are touched - pseudo-resources ("black", "color",
    /// "%luma" names) stay as-is.
    /// </summary>
    private static void MakeResourcePathsAbsolute(Mlt project, string sourceDir)
    {
        foreach (var propertyList in project.Chain.Select(c => c.Property)
                     .Append(project.Producer?.Property ?? []))
        {
            var resource = propertyList.FirstOrDefault(p => p.Name == "resource");
            if (resource == null || string.IsNullOrEmpty(resource.Text)
                || Path.IsPathRooted(resource.Text)
                || resource.Text.Contains("://"))
            {
                continue;
            }

            var absolutePath = Path.GetFullPath(Path.Combine(sourceDir, resource.Text));
            if (File.Exists(absolutePath))
            {
                resource.Text = absolutePath;
            }
        }
    }

    /// <summary>
    /// Insert the avformat consumer element (with all encoding properties as attributes)
    /// after the profile element - the document shape Shotcut hands to melt.
    /// </summary>
    private static void InjectConsumerElement(string renderXmlPath, string outputPath, MeltRenderSettings settings)
    {
        var document = System.Xml.Linq.XDocument.Load(renderXmlPath);
        if (document.Root == null)
            throw new InvalidOperationException("Render XML has no root element");

        var consumer = new System.Xml.Linq.XElement("consumer",
            new System.Xml.Linq.XAttribute("mlt_service", "avformat"),
            new System.Xml.Linq.XAttribute("target", outputPath));

        foreach (var (name, value) in BuildConsumerProperties(outputPath, settings))
        {
            consumer.SetAttributeValue(name, value);
        }

        var lastProfile = document.Root.Elements("profile").LastOrDefault();
        if (lastProfile != null)
        {
            lastProfile.AddAfterSelf(consumer);
        }
        else
        {
            document.Root.AddFirst(consumer);
        }

        document.Save(renderXmlPath);
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

    private string BuildMeltArguments(string renderXmlPath, MeltRenderSettings settings, int? inPoint = null, int? outPoint = null)
    {
        var args = new List<string>
        {
            // -verbose makes failure logs actionable; -progress2 gives line-based
            // progress on stderr; -abort stops on the first render error
            "-verbose",
            "-progress2",
            "-abort"
        };

        // Producer URL, percent-encoded like Shotcut (survives &, #, spaces in paths).
        // ?multi:1 engages the multi consumer, required when the output geometry or
        // frame rate differs from the embedded project profile.
        var producerUrl = $"xml:{Uri.EscapeDataString(renderXmlPath)}";
        if (settings.Width.HasValue || settings.FrameRateNum.HasValue)
        {
            producerUrl += "?multi:1";
        }
        args.Add(producerUrl);

        // In/Out points bind to the producer (inclusive frame numbers)
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

        return string.Join(" ", args);
    }

    /// <summary>
    /// All avformat consumer properties for this render, keyed for the consumer
    /// XML element. Mirrors what Shotcut's export panel emits per encoder.
    /// </summary>
    private static Dictionary<string, string> BuildConsumerProperties(string outputPath, MeltRenderSettings settings)
    {
        var props = new Dictionary<string, string>();

        if (settings.PresetProperties is { Count: > 0 })
        {
            // A stock MLT export preset governs format/codec/quality wholesale
            foreach (var (presetKey, presetValue) in settings.PresetProperties)
            {
                if (!presetKey.StartsWith("meta."))
                {
                    props[presetKey] = presetValue;
                }
            }
        }
        else
        {
            // Video codec - CPU (libx264/libx265) or hardware (NVENC/QSV/AMF)
            props["vcodec"] = settings.VideoCodec;
            props["acodec"] = settings.AudioCodec;

            // Quality - each encoder family takes a different property
            if (settings.Crf.HasValue)
            {
                if (settings.VideoCodec.Contains("nvenc"))
                {
                    props["rc"] = "vbr";
                    props["cq"] = settings.Crf.Value.ToString();
                }
                else if (settings.VideoCodec.Contains("qsv"))
                {
                    // Shotcut emits MLT's qscale (min 1), not the raw global_quality AVOption
                    props["qscale"] = Math.Max(1, settings.Crf.Value).ToString();
                }
                else if (settings.VideoCodec.Contains("amf"))
                {
                    props["rc"] = "cqp";
                    props["qp_i"] = settings.Crf.Value.ToString();
                    props["qp_p"] = settings.Crf.Value.ToString();
                    props["qp_b"] = settings.Crf.Value.ToString();
                }
                else if (settings.VideoCodec == "libx265")
                {
                    // x265 wants its options via x265-params (Shotcut sets both)
                    props["x265-params"] = $"crf={settings.Crf.Value}";
                    props["crf"] = settings.Crf.Value.ToString();
                }
                else
                {
                    props["crf"] = settings.Crf.Value.ToString();
                }
            }

            // Encoding preset only applies to the CPU x264/x265 encoders
            if (!string.IsNullOrEmpty(settings.Preset) && settings.VideoCodec.StartsWith("libx"))
            {
                props["preset"] = settings.Preset;
            }

            // GOP / B-frames (Shotcut parity)
            if (settings.Gop.HasValue)
            {
                props["g"] = settings.Gop.Value.ToString();
            }
            if (settings.BFrames.HasValue)
            {
                props["bf"] = settings.BFrames.Value.ToString();
            }

            // Codec threads: MLT's default is 1(!). 0 = codec auto for x264/x265;
            // hardware encoders get cores-1 like Shotcut does
            props["threads"] = settings.VideoCodec.StartsWith("libx")
                ? "0"
                : Math.Max(1, Environment.ProcessorCount - 1).ToString();

            // Audio: quality-based VBR takes precedence over average bitrate;
            // FLAC is lossless so neither applies
            if (settings.AudioCodec != "flac")
            {
                if (settings.AudioQuality.HasValue)
                {
                    props["vbr"] = "on";
                    props["aq"] = settings.AudioQuality.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (!string.IsNullOrEmpty(settings.AudioBitrate))
                {
                    props["vbr"] = "constrained";
                    props["ab"] = settings.AudioBitrate;
                }
            }

            // MP4 optimization (presets carry their own movflags)
            if (outputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                props["movflags"] = "+faststart";
            }
        }

        // Scaling/deinterlacing quality - Shotcut's defaults; MLT's own differ
        props["rescale"] = "bilinear";
        props["deinterlacer"] = "bwdif";

        // Optional output profile overrides (default: the project profile)
        if (settings.Width.HasValue && settings.Height.HasValue)
        {
            props["width"] = settings.Width.Value.ToString();
            props["height"] = settings.Height.Value.ToString();

            var darNum = settings.DisplayAspectNum ?? settings.Width.Value;
            var darDen = settings.DisplayAspectDen ?? settings.Height.Value;
            props["display_aspect_num"] = darNum.ToString();
            props["display_aspect_den"] = darDen.ToString();

            // Pixel (sample) aspect = DAR / (W/H), reduced
            var sarNum = darNum * settings.Height.Value;
            var sarDen = darDen * settings.Width.Value;
            var divisor = Gcd(sarNum, sarDen);
            props["sample_aspect_num"] = (sarNum / divisor).ToString();
            props["sample_aspect_den"] = (sarDen / divisor).ToString();
        }

        if (settings.FrameRateNum.HasValue)
        {
            props["frame_rate_num"] = settings.FrameRateNum.Value.ToString();
            props["frame_rate_den"] = (settings.FrameRateDen ?? 1).ToString();
        }

        // CRITICAL: negative real_time disables frame dropping (required for file
        // rendering). MLT frame-processing threads capped at 4 like Shotcut - each
        // holds full frame buffers and more risks OOM/artifacts
        var threadCount = settings.ThreadCount > 0
            ? settings.ThreadCount
            : Environment.ProcessorCount;
        threadCount = Math.Clamp(threadCount, 1, 4);
        props["real_time"] = $"-{threadCount}";

        return props;
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

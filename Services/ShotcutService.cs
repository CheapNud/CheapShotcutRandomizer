using CheapShotcutRandomizer.Core.Models;
using CheapHelpers.Services.DataExchange.Xml;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Result of a grid compilation: tractor track indices and the generated cell
/// playlists, in cell order (skipped unfillable cells are absent from both).
/// </summary>
public record GridCompilationResult(List<int> TrackIndices, List<Playlist> CellPlaylists);

/// <summary>
/// Represents information about a track in an MLT project
/// </summary>
public class TrackInfo
{
    /// <summary>
    /// Track index in the tractor
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Display name of the track
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of track: "video" or "audio"
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Whether this track is currently hidden in Shotcut
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// The producer ID this track references
    /// </summary>
    public string ProducerId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a system track (like the black background)
    /// System tracks are required for rendering but should not be user-selectable
    /// </summary>
    public bool IsSystemTrack { get; set; }
}

public class ShotcutService(IXmlService xmlService)
{
    private readonly IXmlService _xmlService = xmlService;

    public async Task<Mlt?> LoadProjectAsync(string path)
    {
        try
        {
            return await _xmlService.DeserializeAsync<Mlt>(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading project: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Where generated shuffle/random projects live before rendering - our own temp
    /// subfolder, so they can be deleted safely once their job reaches a terminal state.
    /// </summary>
    public static string GeneratedProjectsTempDir =>
        Path.Combine(Path.GetTempPath(), "CheapShotcutRandomizer", "generated");

    public static bool IsGeneratedTempProject(string? path) =>
        !string.IsNullOrEmpty(path) &&
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(GeneratedProjectsTempDir), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rewrite relative media resources to absolute paths. Only paths that actually
    /// resolve to an existing file are touched - pseudo-resources ("black", "color",
    /// "%luma" names) stay as-is.
    /// </summary>
    public static void MakeResourcePathsAbsolute(Mlt project, string sourceDir)
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

    public async Task<string> SaveProjectAsync(Mlt project, string originalPath)
    {
        try
        {
            var newpath = Path.Combine(
                Path.GetDirectoryName(originalPath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(originalPath)}.Random{Guid.NewGuid().ToString()[..8]}{Path.GetExtension(originalPath)}"
            );

            await _xmlService.SerializeAsync(newpath, project);

            return newpath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving project: {ex.Message}");
            throw;
        }
    }

    public void ShufflePlaylist(Mlt project, int playlistIndex, bool avoidConsecutiveSameSource = false)
    {
        if (playlistIndex < 0 || playlistIndex >= project.Playlist.Count)
            throw new ArgumentOutOfRangeException(nameof(playlistIndex));

        // Clear blanks from playlist (legacy approach for backward compatibility)
        project.Playlist[playlistIndex].Blank = [];

        if (avoidConsecutiveSameSource)
        {
            project.Playlist[playlistIndex].Entry = [.. ShuffleWithConstraints(project.Playlist[playlistIndex].Entry)];
        }
        else
        {
            project.Playlist[playlistIndex].Entry = [.. project.Playlist[playlistIndex].Entry.Shuffle()];
        }

        // Update the Items array to reflect changes (removes blanks from ordered timeline)
        project.Playlist[playlistIndex].Items = project.Playlist[playlistIndex].Entry.Cast<object>().ToArray();
    }

    /// <summary>
    /// Remove all blank elements from a playlist, keeping only entries
    /// </summary>
    public void RemoveBlanks(Playlist playlist)
    {
        if (playlist == null)
            throw new ArgumentNullException(nameof(playlist));

        // Clear the Blank list
        playlist.Blank = [];

        // Update Items to only contain entries
        playlist.Items = playlist.Entry.Cast<object>().ToArray();
    }

    private static List<Entry> ShuffleWithConstraints(List<Entry> entries)
    {
        // If we don't have enough clips, just shuffle normally - can't avoid consecutive same source
        if (entries.Count < 2)
            return [.. entries.Shuffle()];

        var random = new Random();
        var remaining = new List<Entry>(entries);
        var result = new List<Entry>();

        // Pick first entry randomly
        var firstIndex = random.Next(remaining.Count);
        result.Add(remaining[firstIndex]);
        remaining.RemoveAt(firstIndex);

        // Keep trying to place remaining clips
        int maxAttempts = remaining.Count * 100; // Prevent infinite loops
        int attempts = 0;

        while (remaining.Count > 0 && attempts < maxAttempts)
        {
            attempts++;
            var lastProducer = result[^1].Producer;

            // Try to find a clip with different producer
            var differentProducerClips = remaining.Where(e => e.Producer != lastProducer).ToList();

            if (differentProducerClips.Count > 0)
            {
                // Pick randomly from clips with different producer
                var nextClip = differentProducerClips[random.Next(differentProducerClips.Count)];
                result.Add(nextClip);
                remaining.Remove(nextClip);
            }
            else
            {
                // All remaining clips are from same producer as last one
                // Just take the next one, we have no choice
                var nextClip = remaining[random.Next(remaining.Count)];
                result.Add(nextClip);
                remaining.Remove(nextClip);
            }
        }

        // If we failed (shouldn't happen), just append remaining
        if (remaining.Count > 0)
        {
            result.AddRange(remaining.Shuffle());
        }

        return result;
    }

    /// <summary>
    /// Run the simulated-annealing selection over the chosen source playlists and
    /// return the shuffled entry sequence for one compilation.
    /// </summary>
    private static List<Entry> SelectRandomEntries(
        Mlt project,
        List<(int PlaylistIndex, int TargetDurationSeconds)> sourcePlaylists,
        double durationWeight,
        double numberOfVideosWeight)
    {
        var allVideos = new List<Entry>();
        int totalTargetDuration = 0;

        foreach (var (playlistIndex, targetDuration) in sourcePlaylists)
        {
            if (playlistIndex < 0 || playlistIndex >= project.Playlist.Count)
                continue;

            int actualTarget = targetDuration == 0
                ? project.Playlist[playlistIndex].Entry.Sum(x => x.Duration)
                : targetDuration;

            totalTargetDuration += actualTarget;

            var selectedVideos = new SimulatedAnnealingVideoSelector(actualTarget, durationWeight, numberOfVideosWeight)
                .SelectVideos([.. project.Playlist[playlistIndex].Entry.Shuffle()])
                .Shuffle()
                .ToList();

            allVideos.AddRange(selectedVideos);
        }

        return new SimulatedAnnealingVideoSelector(totalTargetDuration, durationWeight, numberOfVideosWeight)
            .SelectVideos(allVideos)
            .Shuffle()
            .ToList();
    }

    /// <summary>
    /// Add a generated entry sequence to the project as a new playlist + tractor track.
    /// </summary>
    private (Playlist Playlist, int TrackIndex) AddGeneratedTrack(Mlt project, List<Entry> entries, string trackName)
    {
        var newPlaylist = new Playlist
        {
            Entry = entries,
            Blank = [], // No blanks in generated playlists
            Id = $"playlist{project.Playlist.Count + 1}",
            Property =
            [
                new() { Name = "shotcut:video", Text = "1" },
                new() { Name = "shotcut:name", Text = trackName }
            ]
        };

        // Remove blanks from newly generated playlist (default behavior)
        RemoveBlanks(newPlaylist);

        project.Playlist.Add(newPlaylist);

        var mainTractor = project.Tractor.First(x => x.Property.Any(y => y.Name == "shotcut"));
        var newTrackIndex = mainTractor.Track.Count; // Index where the new track will be added
        mainTractor.Track.Add(new Track { Producer = newPlaylist.Id });

        return (newPlaylist, newTrackIndex);
    }

    public (Playlist Playlist, int TrackIndex) GenerateRandomPlaylist(
        Mlt project,
        List<(int PlaylistIndex, int TargetDurationSeconds)> sourcePlaylists,
        double durationWeight,
        double numberOfVideosWeight)
    {
        var finalVideos = SelectRandomEntries(project, sourcePlaylists, durationWeight, numberOfVideosWeight);
        var (newPlaylist, newTrackIndex) = AddGeneratedTrack(project, finalVideos, "generated");

        // Set render range to match the generated playlist duration
        SetRenderRangeToPlaylistDuration(project, newPlaylist);

        return (newPlaylist, newTrackIndex);
    }

    /// <summary>
    /// Generate a split-screen compilation: N independent random playlists, each shown
    /// in its own cell of a side-by-side (2) or 2x2 (4) grid. Uses the same mechanics
    /// Shotcut's UI produces: a Size Position Rotate filter (MLT affine) per track and
    /// the mix + qtblend transitions Shotcut plants for every video track.
    /// Returns the tractor track indices and playlists of the generated cells.
    /// </summary>
    public GridCompilationResult GenerateGridCompilation(
        Mlt project,
        List<(int PlaylistIndex, int TargetDurationSeconds)> sourcePlaylists,
        double durationWeight,
        double numberOfVideosWeight,
        int cells,
        bool splitSingleCompilation = false,
        List<int>? cellSources = null)
    {
        if (cells is not (2 or 4))
            throw new ArgumentException("Grid layout supports 2 or 4 cells", nameof(cells));
        if (splitSingleCompilation && cellSources is not null)
            throw new ArgumentException("Split mode and per-cell sources are mutually exclusive", nameof(cellSources));
        if (cellSources is not null && cellSources.Count != cells)
            throw new ArgumentException("cellSources must assign one source playlist per cell", nameof(cellSources));

        var profileWidth = int.TryParse(project.Profile?.Width, out var parsedWidth) ? parsedWidth : 1920;
        var profileHeight = int.TryParse(project.Profile?.Height, out var parsedHeight) ? parsedHeight : 1080;
        var cellRects = BuildCellRects(profileWidth, profileHeight, cells);

        // Independent mode: every cell is its own random compilation over all sources.
        // Split mode: ONE compilation carved into consecutive, duration-balanced
        // segments that play simultaneously (a 40 min sequence becomes ~10 min of 4-up).
        // Assigned mode: each cell compiles from exactly one assigned source playlist.
        List<List<Entry>> cellEntries;
        if (splitSingleCompilation)
        {
            var compilation = SelectRandomEntries(project, sourcePlaylists, durationWeight, numberOfVideosWeight);
            cellEntries = SplitEvenlyByDuration(compilation, cells);
        }
        else if (cellSources is not null)
        {
            cellEntries = [.. cellSources.Select(sourceIndex =>
            {
                var targetSeconds = sourcePlaylists
                    .Where(p => p.PlaylistIndex == sourceIndex)
                    .Select(p => p.TargetDurationSeconds)
                    .FirstOrDefault();
                return SelectRandomEntries(project, [(sourceIndex, targetSeconds)], durationWeight, numberOfVideosWeight);
            })];
        }
        else
        {
            cellEntries = [.. Enumerable.Range(0, cells)
                .Select(_ => SelectRandomEntries(project, sourcePlaylists, durationWeight, numberOfVideosWeight))];
        }

        var trackIndices = new List<int>();
        var cellPlaylists = new List<Playlist>();
        Playlist? longestPlaylist = null;
        var longestDuration = -1;
        var cellNames = CellNames(cells);

        for (int cell = 0; cell < cells; cell++)
        {
            // Fewer clips than cells: skip the unfillable cell rather than adding
            // an empty track (the cell simply shows the background)
            if (cellEntries[cell].Count == 0)
            {
                continue;
            }

            var (playlist, trackIndex) = AddGeneratedTrack(project, cellEntries[cell], $"grid {cellNames[cell]}");

            // Size Position Rotate filter placing this track in its grid cell.
            // shotcut:filter marks it so Shotcut's UI shows it as an editable SPR filter.
            playlist.Filter.Add(new Filter
            {
                Id = $"gridCell{trackIndex}",
                Property =
                [
                    new() { Name = "mlt_service", Text = "affine" },
                    new() { Name = "shotcut:filter", Text = "affineSizePosition" },
                    new() { Name = "transition.rect", Text = cellRects[cell] },
                    new() { Name = "transition.valign", Text = "middle" },
                    new() { Name = "transition.halign", Text = "center" },
                    new() { Name = "transition.fill", Text = "1" },
                    new() { Name = "transition.distort", Text = "0" },
                    new() { Name = "background", Text = "color:#00000000" }
                ]
            });

            EnsureTrackTransitions(project, trackIndex);
            trackIndices.Add(trackIndex);
            cellPlaylists.Add(playlist);

            var duration = playlist.Entry.Sum(e => e.Duration);
            if (duration > longestDuration)
            {
                longestDuration = duration;
                longestPlaylist = playlist;
            }
        }

        // Render range spans the longest cell; shorter cells hold black once exhausted
        if (longestPlaylist != null)
        {
            SetRenderRangeToPlaylistDuration(project, longestPlaylist);
        }

        return new GridCompilationResult(trackIndices, cellPlaylists);
    }

    /// <summary>Position names per cell, in the same order as BuildCellRects.</summary>
    public static string[] CellNames(int cells) => cells == 2
        ? ["left", "right"]
        : ["top left", "top right", "bottom left", "bottom right"];

    /// <summary>
    /// Split an entry sequence into N consecutive chunks balanced by duration
    /// (greedy contiguous partition; the last chunk absorbs any remainder).
    /// </summary>
    private static List<List<Entry>> SplitEvenlyByDuration(List<Entry> entries, int parts)
    {
        var chunks = Enumerable.Range(0, parts).Select(_ => new List<Entry>()).ToList();
        var totalDuration = entries.Sum(e => e.Duration);
        var targetPerPart = totalDuration / (double)parts;

        var part = 0;
        double accumulated = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            var remainingEntries = entries.Count - i;
            var partsAfterCurrent = parts - part - 1;

            // Advance once this part has its duration share - but never past a part
            // that got nothing (a single long clip must not vault over chunks), and
            // force-advance when the tail entries are just enough to fill the rest
            var shouldAdvance = part < parts - 1
                && chunks[part].Count > 0
                && (accumulated >= targetPerPart * (part + 1) || remainingEntries <= partsAfterCurrent);

            if (shouldAdvance)
            {
                part++;
            }

            chunks[part].Add(entries[i]);
            accumulated += entries[i].Duration;
        }

        return chunks;
    }

    private static List<string> BuildCellRects(int width, int height, int cells)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;

        // rect format: "x y w h opacity"
        return cells == 2
            ? [
                // Side by side, vertically centered
                $"0 {height / 4} {halfWidth} {halfHeight} 1",
                $"{halfWidth} {height / 4} {halfWidth} {halfHeight} 1"
              ]
            : [
                $"0 0 {halfWidth} {halfHeight} 1",
                $"{halfWidth} 0 {halfWidth} {halfHeight} 1",
                $"0 {halfHeight} {halfWidth} {halfHeight} 1",
                $"{halfWidth} {halfHeight} {halfWidth} {halfHeight} 1"
              ];
    }

    /// <summary>
    /// Plant the two per-track transitions Shotcut creates for every video track
    /// (audio mix against track 0, qtblend video compositing against the track below)
    /// so added tracks actually blend instead of replacing the output.
    /// </summary>
    public void EnsureTrackTransitions(Mlt project, int trackIndex)
    {
        var mainTractor = project.Tractor.First(x => x.Property.Any(y => y.Name == "shotcut"));

        bool HasTransition(string mltService) => mainTractor.Transition.Any(t =>
            t.Property.Any(p => p.Name == "mlt_service" && p.Text == mltService) &&
            t.Property.Any(p => p.Name == "b_track" && p.Text == trackIndex.ToString()));

        if (!HasTransition("mix"))
        {
            mainTractor.Transition.Add(new Transition
            {
                Id = $"transition_mix_{trackIndex}",
                Property =
                [
                    new() { Name = "a_track", Text = "0" },
                    new() { Name = "b_track", Text = trackIndex.ToString() },
                    new() { Name = "mlt_service", Text = "mix" },
                    new() { Name = "always_active", Text = "1" },
                    new() { Name = "sum", Text = "1" }
                ]
            });
        }

        if (!HasTransition("qtblend"))
        {
            mainTractor.Transition.Add(new Transition
            {
                Id = $"transition_blend_{trackIndex}",
                Property =
                [
                    // a_track=0 (composite onto the base) for every blend: verified with
                    // melt that chaining a_track to the previous track loses lower cells
                    new() { Name = "a_track", Text = "0" },
                    new() { Name = "b_track", Text = trackIndex.ToString() },
                    new() { Name = "mlt_service", Text = "qtblend" },
                    new() { Name = "threads", Text = "0" }
                ]
            });
        }
    }

    /// <summary>
    /// Sets the main tractor's in/out points to match the duration of the specified playlist.
    /// This limits the render output to only the playlist's content.
    /// </summary>
    public void SetRenderRangeToPlaylistDuration(Mlt project, Playlist playlist)
    {
        if (project == null)
            throw new ArgumentNullException(nameof(project));
        if (playlist == null)
            throw new ArgumentNullException(nameof(playlist));

        var mainTractor = project.Tractor?.FirstOrDefault(t =>
            t.Property?.Any(p => p.Name == "shotcut") ?? false);

        if (mainTractor == null)
            return;

        // Calculate total duration of playlist entries in seconds
        var totalDurationSeconds = playlist.Entry.Sum(e => e.Duration);

        // Get frame rate from project profile with validation
        var frameRate = project.GetFrameRate();
        if (frameRate <= 0)
            frameRate = 30.0; // Fallback to standard frame rate

        // Convert to frames (ceiling to avoid cutting off final frame)
        var totalFrames = (int)Math.Ceiling(totalDurationSeconds * frameRate);

        // Set the render range on the main tractor
        // in="0" starts from the beginning
        // out is the total frames (exclusive, so total-1 would be last frame but MLT uses total)
        mainTractor.In = "0";
        mainTractor.Out = totalFrames.ToString();
    }

    /// <summary>
    /// Get the track index for a given playlist ID
    /// Returns the tractor track index that references the playlist, or -1 if not found
    /// </summary>
    public int GetTrackIndexForPlaylist(Mlt project, int playlistIndex)
    {
        if (project == null)
            throw new ArgumentNullException(nameof(project));

        if (playlistIndex < 0 || playlistIndex >= project.Playlist.Count)
            return -1;

        var playlistId = project.Playlist[playlistIndex].Id;

        var mainTractor = project.Tractor?.FirstOrDefault(t =>
            t.Property?.Any(p => p.Name == "shotcut") ?? false);

        if (mainTractor?.Track == null)
            return -1;

        for (int i = 0; i < mainTractor.Track.Count; i++)
        {
            if (mainTractor.Track[i].Producer == playlistId)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Gets user-visible tracks (excludes system tracks like black background)
    /// System tracks are required for rendering and should not be user-selectable
    /// </summary>
    public List<TrackInfo> GetTracks(Mlt project)
    {
        if (project == null)
            throw new ArgumentNullException(nameof(project));

        var tracks = new List<TrackInfo>();

        // Find the main tractor (the one with shotcut properties)
        var mainTractor = project.Tractor?.FirstOrDefault(t =>
            t.Property?.Any(p => p.Name == "shotcut") ?? false);

        if (mainTractor?.Track == null || mainTractor.Track.Count == 0)
            return tracks;

        // In Shotcut MLT files:
        // - First track is usually "black" (background) - this is a SYSTEM TRACK
        // - Following tracks are the actual timeline tracks (V1, V2, A1, A2, etc.)
        // - hide="video" means audio-only track
        // - hide="audio" means video-only track
        // - hide="both" or missing means both enabled

        for (int i = 0; i < mainTractor.Track.Count; i++)
        {
            var track = mainTractor.Track[i];
            var producerId = track.Producer;

            // Check if this is a system track
            bool isSystemTrack = IsSystemTrack(producerId);

            // Skip system tracks - they should not be in the user-visible track list
            if (isSystemTrack)
                continue;

            // Find the corresponding playlist
            var playlist = project.Playlist?.FirstOrDefault(p => p.Id == producerId);
            var trackName = playlist?.Name ?? producerId;

            // Determine track type based on hide attribute
            string trackType;
            bool isHidden = false;

            if (string.IsNullOrEmpty(track.Hide))
            {
                // No hide attribute means both video and audio are enabled
                // Determine type by playlist properties
                var hasVideo = playlist?.Property?.Any(p => p.Name == "shotcut:video" && p.Text == "1") ?? false;
                var hasAudio = playlist?.Property?.Any(p => p.Name == "shotcut:audio" && p.Text == "1") ?? false;

                if (hasVideo && !hasAudio)
                    trackType = "video";
                else if (!hasVideo && hasAudio)
                    trackType = "audio";
                else
                    trackType = "video"; // Default to video for mixed tracks
            }
            else if (track.Hide == "video")
            {
                trackType = "audio"; // Video is hidden, so it's an audio track
            }
            else if (track.Hide == "audio")
            {
                trackType = "video"; // Audio is hidden, so it's a video track
            }
            else if (track.Hide == "both")
            {
                isHidden = true;
                trackType = "video"; // Default when both are hidden
            }
            else
            {
                trackType = "video"; // Default
            }

            tracks.Add(new TrackInfo
            {
                Index = i,
                Name = trackName,
                Type = trackType,
                IsHidden = isHidden,
                ProducerId = producerId,
                IsSystemTrack = false // Already filtered out system tracks above
            });
        }

        return tracks;
    }

    /// <summary>
    /// Determines if a track is a system track that should not be user-selectable
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
}

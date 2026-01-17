using CheapShotcutRandomizer.Core.Models;
using CheapHelpers.Services.DataExchange.Xml;

namespace CheapShotcutRandomizer.Services;

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

    public (Playlist Playlist, int TrackIndex) GenerateRandomPlaylist(
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

        var finalVideos = new SimulatedAnnealingVideoSelector(totalTargetDuration, durationWeight, numberOfVideosWeight)
            .SelectVideos(allVideos)
            .Shuffle()
            .ToList();

        var newPlaylist = new Playlist
        {
            Entry = finalVideos,
            Blank = [], // No blanks in generated playlists
            Id = $"playlist{project.Playlist.Count + 1}",
            Property =
            [
                new() { Name = "shotcut:video", Text = "1" },
                new() { Name = "shotcut:name", Text = "generated" }
            ]
        };

        // Remove blanks from newly generated playlist (default behavior)
        RemoveBlanks(newPlaylist);

        project.Playlist.Add(newPlaylist);

        var mainTractor = project.Tractor.First(x => x.Property.Any(y => y.Name == "shotcut"));
        var newTrackIndex = mainTractor.Track.Count; // Index where the new track will be added
        mainTractor.Track.Add(new Track { Producer = newPlaylist.Id });

        // Set render range to match the generated playlist duration
        SetRenderRangeToPlaylistDuration(project, newPlaylist);

        return (newPlaylist, newTrackIndex);
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

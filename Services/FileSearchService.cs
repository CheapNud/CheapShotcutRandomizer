using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Service for searching files within MLT project tracks
/// </summary>
public class FileSearchService
{
    /// <summary>
    /// Get the resource path from a producer
    /// </summary>
    private static string? GetProducerResourcePath(Producer producer)
    {
        var resourceProp = producer.Property?.FirstOrDefault(p => p.Name == "resource");
        return resourceProp?.Text;
    }

    /// <summary>
    /// Search for files matching a partial filename across all playlists
    /// </summary>
    public List<FileSearchResult> SearchByPartialFilename(Mlt project, string partialFilename, List<int>? playlistIndices = null)
    {
        var results = new List<FileSearchResult>();

        if (string.IsNullOrWhiteSpace(partialFilename))
            return results;

        // Build producer ID to file path mapping
        var producerPaths = new Dictionary<string, string>();
        if (project.Producer != null)
        {
            var path = GetProducerResourcePath(project.Producer);
            if (path != null)
                producerPaths[project.Producer.Id] = path;
        }

        // Get frame rate for blank duration calculation
        var frameRate = project.GetFrameRate();

        // Search in playlists (playlists are also producers in MLT)
        var playlistsToSearch = playlistIndices ?? Enumerable.Range(0, project.Playlist.Count).ToList();

        foreach (var playlistIndex in playlistsToSearch)
        {
            if (playlistIndex < 0 || playlistIndex >= project.Playlist.Count)
                continue;

            var playlist = project.Playlist[playlistIndex];

            // Calculate timeline positions using ordered items (respects Entry/Blank order)
            var cumulativeTime = TimeSpan.Zero;
            var entryIndex = 0; // Track Entry index separately

            foreach (var item in playlist.OrderedItems)
            {
                if (item is Entry entry)
                {
                    var producerId = entry.Producer;

                    // Calculate start and end times for this entry
                    var startTime = cumulativeTime;
                    var clipDuration = TimeSpan.FromSeconds(entry.Duration);
                    var endTime = startTime + clipDuration;

                    // Try to find the producer's resource path
                    string? resourcePath = null;

                    // Check if we already have it
                    if (producerPaths.TryGetValue(producerId, out var cachedPath))
                    {
                        resourcePath = cachedPath;
                    }
                    else
                    {
                        // Search for the producer definition
                        // In MLT, producers might be defined at project level or in chains
                        if (project.Chain != null)
                        {
                            foreach (var chain in project.Chain)
                            {
                                if (chain.Id == producerId)
                                {
                                    var resProp = chain.Property?.FirstOrDefault(p => p.Name == "resource");
                                    if (resProp != null)
                                    {
                                        resourcePath = resProp.Text;
                                        producerPaths[producerId] = resourcePath;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Check if this resource matches the search
                    if (resourcePath != null && resourcePath.Contains(partialFilename, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new FileSearchResult
                        {
                            ProducerId = producerId,
                            FilePath = resourcePath,
                            PlaylistIndex = playlistIndex,
                            PlaylistName = playlist.Name,
                            EntryIndex = entryIndex,
                            EntryInPoint = entry.In,
                            EntryOutPoint = entry.Out,
                            StartTime = startTime,
                            EndTime = endTime
                        });
                    }

                    // Add this entry's duration to cumulative time
                    cumulativeTime = endTime;
                    entryIndex++;
                }
                else if (item is Blank blank)
                {
                    // Add blank duration to cumulative time but don't search
                    var blankDuration = TimeSpan.FromSeconds(blank.GetDurationSeconds(frameRate));
                    cumulativeTime += blankDuration;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Search for a specific producer ID across playlists
    /// </summary>
    public List<FileSearchResult> SearchByProducerId(Mlt project, string producerId, List<int>? playlistIndices = null)
    {
        var results = new List<FileSearchResult>();
        var playlistsToSearch = playlistIndices ?? Enumerable.Range(0, project.Playlist.Count).ToList();

        // Get frame rate for blank duration calculation
        var frameRate = project.GetFrameRate();

        foreach (var playlistIndex in playlistsToSearch)
        {
            if (playlistIndex < 0 || playlistIndex >= project.Playlist.Count)
                continue;

            var playlist = project.Playlist[playlistIndex];

            // Calculate timeline positions using ordered items (respects Entry/Blank order)
            var cumulativeTime = TimeSpan.Zero;
            var entryIndex = 0; // Track Entry index separately

            foreach (var item in playlist.OrderedItems)
            {
                if (item is Entry entry)
                {
                    // Calculate start and end times for this entry
                    var startTime = cumulativeTime;
                    var clipDuration = TimeSpan.FromSeconds(entry.Duration);
                    var endTime = startTime + clipDuration;

                    if (entry.Producer == producerId)
                    {
                        results.Add(new FileSearchResult
                        {
                            ProducerId = producerId,
                            PlaylistIndex = playlistIndex,
                            PlaylistName = playlist.Name,
                            EntryIndex = entryIndex,
                            EntryInPoint = entry.In,
                            EntryOutPoint = entry.Out,
                            StartTime = startTime,
                            EndTime = endTime
                        });
                    }

                    // Add this entry's duration to cumulative time
                    cumulativeTime = endTime;
                    entryIndex++;
                }
                else if (item is Blank blank)
                {
                    // Add blank duration to cumulative time but don't search
                    var blankDuration = TimeSpan.FromSeconds(blank.GetDurationSeconds(frameRate));
                    cumulativeTime += blankDuration;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Get all unique files used in specified playlists
    /// </summary>
    public List<UniqueFileInfo> GetUniqueFiles(Mlt project, List<int>? playlistIndices = null)
    {
        var playlistsToSearch = playlistIndices ?? Enumerable.Range(0, project.Playlist.Count).ToList();

        // First, build a map of producer IDs to file paths
        var producerToFilePath = new Dictionary<string, string>();

        // Map all producers to their file paths
        if (project.Chain != null)
        {
            foreach (var chain in project.Chain)
            {
                var resProp = chain.Property?.FirstOrDefault(p => p.Name == "resource");
                if (resProp?.Text != null)
                    producerToFilePath[chain.Id] = resProp.Text;
            }
        }

        if (project.Producer != null)
        {
            var resProp = project.Producer.Property?.FirstOrDefault(p => p.Name == "resource");
            if (resProp?.Text != null)
                producerToFilePath[project.Producer.Id] = resProp.Text;
        }

        // Count usage by FILE PATH (not producer ID)
        var filePathUsage = new Dictionary<string, int>();
        var filePathToProducerId = new Dictionary<string, string>(); // Keep one producer ID per file for reference

        // Collect all producer IDs and map to file paths
        foreach (var playlistIndex in playlistsToSearch)
        {
            if (playlistIndex < 0 || playlistIndex >= project.Playlist.Count)
                continue;

            var playlist = project.Playlist[playlistIndex];
            foreach (var entry in playlist.Entry)
            {
                var producerId = entry.Producer;

                // Get the file path for this producer
                if (producerToFilePath.TryGetValue(producerId, out var filePath))
                {
                    // Count by file path
                    if (!filePathUsage.ContainsKey(filePath))
                    {
                        filePathUsage[filePath] = 0;
                        filePathToProducerId[filePath] = producerId; // Keep first producer ID
                    }

                    filePathUsage[filePath]++;
                }
                else
                {
                    // Unknown producer - count separately
                    var unknownKey = $"[Unknown: {producerId}]";
                    if (!filePathUsage.ContainsKey(unknownKey))
                    {
                        filePathUsage[unknownKey] = 0;
                        filePathToProducerId[unknownKey] = producerId;
                    }
                    filePathUsage[unknownKey]++;
                }
            }
        }

        // Create result list grouped by file path
        var results = new List<UniqueFileInfo>();

        foreach (var (filePath, usageCount) in filePathUsage)
        {
            results.Add(new UniqueFileInfo
            {
                ProducerId = filePathToProducerId[filePath],
                FilePath = filePath,
                UsageCount = usageCount
            });
        }

        return results.OrderByDescending(f => f.UsageCount).ThenBy(f => Path.GetFileName(f.FilePath)).ToList();
    }
}

/// <summary>
/// Result of a file search operation
/// </summary>
public class FileSearchResult
{
    public string ProducerId { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public int PlaylistIndex { get; set; }
    public string PlaylistName { get; set; } = string.Empty;
    public int EntryIndex { get; set; }
    public string EntryInPoint { get; set; } = string.Empty;
    public string EntryOutPoint { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public string DisplayName => FilePath != null ? Path.GetFileName(FilePath) : ProducerId;
    public string TimelineDisplay => $"{StartTime:hh\\:mm\\:ss} \u2192 {EndTime:hh\\:mm\\:ss}";
}

/// <summary>
/// Information about a unique file in the project
/// </summary>
public class UniqueFileInfo
{
    public string ProducerId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int UsageCount { get; set; }

    public string FileName => Path.GetFileName(FilePath);
}

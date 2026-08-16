using System.Globalization;
using System.Xml;
using System.Xml.Serialization;

namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Base interface for playlist timeline items (Entry and Blank)
/// </summary>
public interface IPlaylistItem
{
    /// <summary>
    /// Gets the duration of this item in seconds
    /// </summary>
    double GetDurationSeconds(double frameRate);

    /// <summary>
    /// Gets a display name for this item
    /// </summary>
    string GetDisplayName();
}

[XmlRoot(ElementName = "profile")]
public class Profile
{
    [XmlAttribute(AttributeName = "description")]
    public string Description { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "width")]
    public string Width { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "height")]
    public string Height { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "progressive")]
    public string Progressive { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "sample_aspect_num")]
    public string Sample_aspect_num { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "sample_aspect_den")]
    public string Sample_aspect_den { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "display_aspect_num")]
    public string Display_aspect_num { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "display_aspect_den")]
    public string Display_aspect_den { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "frame_rate_num")]
    public string Frame_rate_num { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "frame_rate_den")]
    public string Frame_rate_den { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "colorspace")]
    public string Colorspace { get; set; } = string.Empty;
}

[XmlRoot(ElementName = "property")]
public class Property
{
    [XmlAttribute(AttributeName = "name")]
    public string Name { get; set; } = string.Empty;
    [XmlText]
    public string Text { get; set; } = string.Empty;
}

[XmlRoot(ElementName = "chain")]
public class Chain
{
    [XmlElement(ElementName = "property")]
    public List<Property> Property { get; set; } = [];
    [XmlAttribute(AttributeName = "id")]
    public string Id { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "out")]
    public string Out { get; set; } = string.Empty;
}

[XmlRoot(ElementName = "entry")]
public class Entry : IPlaylistItem
{
    [XmlAttribute(AttributeName = "producer")]
    public string Producer { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "in")]
    public string In { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "out")]
    public string Out { get; set; } = string.Empty;

    /// <summary>
    /// Duration in seconds. Entries written without in/out attributes yield 0
    /// instead of throwing FormatException out of every duration sum.
    /// </summary>
    public int Duration
    {
        get
        {
            if (!TimeSpan.TryParse(In, System.Globalization.CultureInfo.InvariantCulture, out var inTime) ||
                !TimeSpan.TryParse(Out, System.Globalization.CultureInfo.InvariantCulture, out var outTime))
            {
                return 0;
            }

            return Convert.ToInt32(Math.Floor((outTime - inTime).TotalSeconds));
        }
    }

    public double GetDurationSeconds(double frameRate) => Duration;

    public string GetDisplayName() => $"Entry [{Producer}]";
}

[XmlRoot(ElementName = "filter")]
public class Filter
{
    [XmlElement(ElementName = "property")]
    public List<Property> Property { get; set; } = [];
    [XmlAttribute(AttributeName = "id")]
    public string Id { get; set; } = string.Empty;
}

[XmlRoot(ElementName = "playlist")]
public class Playlist
{
    [XmlElement(ElementName = "property")]
    public List<Property> Property { get; set; } = [];

    /// <summary>
    /// Filters attached to this playlist/track (e.g. Size Position Rotate for grid cells)
    /// </summary>
    [XmlElement(ElementName = "filter")]
    public List<Filter> Filter { get; set; } = [];

    // Document-order backing store; entries and blanks interleave on a real timeline,
    // so losing this order shifts clips and moves the gaps to the end on re-serialization
    private List<object>? _orderedItems;
    private List<Entry> _entry = [];
    private List<Blank> _blank = [];

    [XmlElement("entry", typeof(Entry))]
    [XmlElement("blank", typeof(Blank))]
    public object[] Items
    {
        get
        {
            // Preserve document order when we have it (deserialized or explicitly assigned);
            // fall back to entries-then-blanks for playlists built via the legacy properties
            if (_orderedItems != null)
                return [.. _orderedItems];

            var items = new List<object>();
            items.AddRange(_entry);
            items.AddRange(_blank);
            return [.. items];
        }
        set
        {
            _orderedItems = [.. value];
            _entry = value.OfType<Entry>().ToList();
            _blank = value.OfType<Blank>().ToList();
        }
    }

    // Legacy views; assigning either redefines the playlist content, so the stored order is dropped
    [XmlIgnore]
    public List<Entry> Entry
    {
        get => _entry;
        set { _entry = value; _orderedItems = null; }
    }

    [XmlIgnore]
    public List<Blank> Blank
    {
        get => _blank;
        set { _blank = value; _orderedItems = null; }
    }

    [XmlAttribute(AttributeName = "id")]
    public string Id { get; set; } = string.Empty;

    [XmlAttribute(AttributeName = "title")]
    public string? Title { get; set; }

    // Prevent XmlSerializer from writing empty title attribute
    public bool ShouldSerializeTitle() => !string.IsNullOrEmpty(Title);

    /// <summary>
    /// "1" lets MLT close each producer's file handles once the playlist has moved
    /// past it - set on render-only XML (Shotcut does the same on export)
    /// </summary>
    [XmlAttribute(AttributeName = "autoclose")]
    public string? Autoclose { get; set; }

    public bool ShouldSerializeAutoclose() => !string.IsNullOrEmpty(Autoclose);

    /// <summary>
    /// Get ordered timeline items (Entry and Blank in sequential order)
    /// </summary>
    [XmlIgnore]
    public List<IPlaylistItem> OrderedItems => Items.Cast<IPlaylistItem>().ToList();

    public string Name => Property?.FirstOrDefault(x => x.Name == @"shotcut:name")?.Text ?? "system track";
}

[XmlRoot(ElementName = "producer")]
public class Producer
{
    [XmlElement(ElementName = "property")]
    public List<Property> Property { get; set; } = [];
    [XmlAttribute(AttributeName = "id")]
    public string Id { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "in")]
    public string In { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "out")]
    public string Out { get; set; } = string.Empty;
}

[XmlRoot(ElementName = "blank")]
public class Blank : IPlaylistItem
{
    [XmlAttribute(AttributeName = "length")]
    public string Length { get; set; } = string.Empty;

    public double GetDurationSeconds(double frameRate)
    {
        // Blank length is in frames, convert to seconds
        // Length format can be either frames (e.g., "75") or timecode
        if (string.IsNullOrEmpty(Length))
            return 0;

        // Try parsing as integer (frames)
        if (int.TryParse(Length, out int frames))
        {
            return frames / frameRate;
        }

        // Try parsing as timecode
        if (TimeSpan.TryParse(Length, out TimeSpan timespan))
        {
            return timespan.TotalSeconds;
        }

        return 0;
    }

    public string GetDisplayName() => $"Blank [{Length}]";
}

[XmlRoot(ElementName = "track")]
public class Track
{
    [XmlAttribute(AttributeName = "producer")]
    public string Producer { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "in")]
    public string? In { get; set; }
    [XmlAttribute(AttributeName = "out")]
    public string? Out { get; set; }
    [XmlAttribute(AttributeName = "hide")]
    public string? Hide { get; set; }

    // Prevent XmlSerializer from writing empty attributes
    public bool ShouldSerializeIn() => !string.IsNullOrEmpty(In);
    public bool ShouldSerializeOut() => !string.IsNullOrEmpty(Out);
    public bool ShouldSerializeHide() => !string.IsNullOrEmpty(Hide);
}

[XmlRoot(ElementName = "transition")]
public class Transition
{
    [XmlElement(ElementName = "property")]
    public List<Property> Property { get; set; } = [];
    [XmlAttribute(AttributeName = "id")]
    public string Id { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "out")]
    public string? Out { get; set; }

    // Prevent XmlSerializer from writing empty out attribute
    public bool ShouldSerializeOut() => !string.IsNullOrEmpty(Out);
}

[XmlRoot(ElementName = "tractor")]
public class Tractor
{
    [XmlElement(ElementName = "property")]
    public List<Property> Property { get; set; } = [];
    [XmlElement(ElementName = "track")]
    public List<Track> Track { get; set; } = [];
    [XmlElement(ElementName = "transition")]
    public List<Transition> Transition { get; set; } = [];
    [XmlAttribute(AttributeName = "id")]
    public string Id { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "in")]
    public string In { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "out")]
    public string Out { get; set; } = string.Empty;
    [XmlElement(ElementName = "properties")]
    public Properties? Properties { get; set; }
    [XmlAttribute(AttributeName = "title")]
    public string? Title { get; set; }

    // Prevent XmlSerializer from writing empty attributes
    public bool ShouldSerializeIn() => !string.IsNullOrEmpty(In);
    public bool ShouldSerializeOut() => !string.IsNullOrEmpty(Out);
    public bool ShouldSerializeTitle() => !string.IsNullOrEmpty(Title);
}

[XmlRoot(ElementName = "properties")]
public class Properties
{
    [XmlElement(ElementName = "property")]
    public List<Property> Property { get; set; } = [];
    [XmlAttribute(AttributeName = "name")]
    public string Name { get; set; } = string.Empty;
}

[XmlRoot(ElementName = "mlt")]
public class Mlt
{
    [XmlElement(ElementName = "profile")]
    public Profile? Profile { get; set; }
    [XmlElement(ElementName = "chain")]
    public List<Chain> Chain { get; set; } = [];
    [XmlElement(ElementName = "playlist")]
    public List<Playlist> Playlist { get; set; } = [];
    [XmlElement(ElementName = "producer")]
    public Producer? Producer { get; set; }
    [XmlAttribute(AttributeName = "producer")]
    public string _Producer { get; set; } = string.Empty;
    [XmlElement(ElementName = "tractor")]
    public List<Tractor> Tractor { get; set; } = [];
    [XmlAttribute(AttributeName = "LC_NUMERIC")]
    public string LC_NUMERIC { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "version")]
    public string Version { get; set; } = string.Empty;
    [XmlAttribute(AttributeName = "title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// In point marker (frame number or null for full timeline)
    /// Parsed from main tractor's "in" attribute
    /// </summary>
    [XmlIgnore]
    public int? InMarker { get; private set; }

    /// <summary>
    /// Out point marker (frame number or null for full timeline)
    /// Parsed from main tractor's "out" attribute
    /// </summary>
    [XmlIgnore]
    public int? OutMarker { get; private set; }

    /// <summary>
    /// Calculate the frame rate from profile information
    /// </summary>
    public double GetFrameRate()
    {
        if (Profile == null)
            return 30.0; // Default fallback

        if (double.TryParse(Profile.Frame_rate_num, out double num) &&
            double.TryParse(Profile.Frame_rate_den, out double den) &&
            den != 0)
        {
            return num / den;
        }

        return 30.0; // Default fallback
    }

    /// <summary>
    /// Convert frame number to timecode string (HH:MM:SS.mmm)
    /// </summary>
    public string FramesToTimecode(int frames)
    {
        var frameRate = GetFrameRate();
        var totalSeconds = frames / frameRate;
        var timeSpan = TimeSpan.FromSeconds(totalSeconds);

        // Format as HH:MM:SS.mmm
        return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}.{timeSpan.Milliseconds:D3}";
    }

    /// <summary>
    /// Convert timecode string to frame number
    /// Formats supported:
    /// - HH:MM:SS or HH:MM:SS.mmm (standard timecode)
    /// - MM,SS or MM.SS (shorthand: 30 = 30 min, 0.3 = 30 sec, 0.03 = 3 sec)
    /// - Simple integer (treated as MINUTES)
    /// </summary>
    public int TimecodeToFrames(string timecode)
    {
        var frameRate = GetFrameRate();

        // Shorthand MM.SS or MM,SS format (e.g., "30" = 30 min, "0.3" = 30 sec, "1.15" = 1 min 15 sec)
        if (timecode.Contains('.') || timecode.Contains(','))
        {
            var normalized = timecode.Replace(',', '.');
            if (double.TryParse(normalized, CultureInfo.InvariantCulture, out double minutesDecimal))
            {
                // Split into minutes and fractional part
                var minutes = (int)Math.Floor(minutesDecimal);
                var fractionalPart = minutesDecimal - minutes;

                // Fractional part represents seconds (0.3 = 30 sec, 0.03 = 3 sec)
                // Multiply by 100 to convert decimal to seconds (0.3 * 100 = 30)
                var seconds = (int)Math.Round(fractionalPart * 100);

                var totalSeconds = (minutes * 60) + seconds;
                return (int)(totalSeconds * frameRate);
            }
        }

        // Simple integer - treat as MINUTES
        if (int.TryParse(timecode, out int simpleMinutes))
        {
            return (int)(simpleMinutes * 60 * frameRate);
        }

        // Standard HH:MM:SS or HH:MM:SS.mmm format
        var parts = timecode.Split(':');
        if (parts.Length != 3)
            throw new FormatException("Timecode must be HH:MM:SS, HH:MM:SS.mmm, MM.SS, MM,SS, or a number representing minutes");

        var hours = int.Parse(parts[0]);
        var minutes2 = int.Parse(parts[1]);

        // Handle optional milliseconds
        var secondsParts = parts[2].Split('.');
        var seconds2 = int.Parse(secondsParts[0]);
        var milliseconds = secondsParts.Length > 1 ? int.Parse(secondsParts[1]) : 0;

        var timeSpan = new TimeSpan(0, hours, minutes2, seconds2, milliseconds);

        return (int)(timeSpan.TotalSeconds * frameRate);
    }

    /// <summary>
    /// Parse in/out markers from the main tractor
    /// Call this after deserialization to populate InMarker/OutMarker
    /// </summary>
    public void ParseMarkers()
    {
        // Find the main tractor (usually the last one or one with id "main_bin")
        var mainTractor = Tractor?.LastOrDefault();

        if (mainTractor == null)
            return;

        // Parse "in" attribute
        if (!string.IsNullOrEmpty(mainTractor.In) && int.TryParse(mainTractor.In, out int inFrame))
        {
            InMarker = inFrame;
        }

        // Parse "out" attribute
        if (!string.IsNullOrEmpty(mainTractor.Out) && int.TryParse(mainTractor.Out, out int outFrame))
        {
            OutMarker = outFrame;
        }
    }

    /// <summary>
    /// Get the total duration of the timeline in frames
    /// </summary>
    public int? GetTotalDurationFrames()
    {
        // Get the main tractor's Out attribute which represents timeline length
        var mainTractor = Tractor?.LastOrDefault();

        if (mainTractor == null || string.IsNullOrEmpty(mainTractor.Out))
            return null;

        if (int.TryParse(mainTractor.Out, out int totalFrames))
            return totalFrames;

        return null;
    }

    /// <summary>
    /// Get the total duration of the timeline as a timecode string (HH:MM:SS.mmm)
    /// </summary>
    public string? GetTotalDurationTimecode()
    {
        var totalFrames = GetTotalDurationFrames();
        if (!totalFrames.HasValue)
            return null;

        return FramesToTimecode(totalFrames.Value);
    }

    /// <summary>
    /// Get a human-readable description of the render range
    /// </summary>
    public string GetRenderRangeDescription()
    {
        if (InMarker == null && OutMarker == null)
            return "Full Timeline";

        if (InMarker.HasValue && OutMarker.HasValue)
        {
            var startTime = FramesToTimecode(InMarker.Value);
            var endTime = FramesToTimecode(OutMarker.Value);
            return $"Render: {startTime} to {endTime}";
        }
        else if (InMarker.HasValue)
        {
            var startTime = FramesToTimecode(InMarker.Value);
            return $"Render: From {startTime} to end";
        }
        else if (OutMarker.HasValue)
        {
            var endTime = FramesToTimecode(OutMarker.Value);
            return $"Render: Start to {endTime}";
        }

        return "Full Timeline";
    }
}

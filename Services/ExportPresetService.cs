using System.Diagnostics;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// A stock MLT export preset (key=value consumer properties), e.g. "YouTube".
/// </summary>
public class ExportPreset
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required Dictionary<string, string> Properties { get; init; }

    public string Key => string.IsNullOrEmpty(Category) ? Name : $"{Category}/{Name}";
    public string DisplayName => string.IsNullOrEmpty(Category) ? Name : $"{Category} — {Name}";
    public string? Extension => Properties.GetValueOrDefault("meta.preset.extension");
    public string? Note => Properties.GetValueOrDefault("meta.preset.note");

    /// <summary>Consumer properties without the meta.* entries.</summary>
    public Dictionary<string, string> ConsumerProperties =>
        Properties.Where(p => !p.Key.StartsWith("meta.")).ToDictionary(p => p.Key, p => p.Value);
}

/// <summary>
/// Loads the stock export presets that ship with MLT/Shotcut from
/// share/mlt/presets/consumer/avformat next to the melt executable.
/// Same files Shotcut's export panel lists - no preset data ships in this app.
/// </summary>
public class ExportPresetService
{
    public List<ExportPreset> LoadPresets(string? meltExecutablePath)
    {
        var presetDir = FindPresetDirectory(meltExecutablePath);
        if (presetDir == null)
        {
            Debug.WriteLine("MLT preset directory not found - preset list will be empty");
            return [];
        }

        var presets = new List<ExportPreset>();

        try
        {
            // Top-level files are uncategorized presets; subdirectories are categories
            foreach (var file in Directory.GetFiles(presetDir))
            {
                AddPreset(presets, file, category: "");
            }

            foreach (var categoryDir in Directory.GetDirectories(presetDir))
            {
                var category = Path.GetFileName(categoryDir);
                foreach (var file in Directory.GetFiles(categoryDir))
                {
                    AddPreset(presets, file, category);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading MLT presets: {ex.Message}");
        }

        return presets
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToList();
    }

    private static void AddPreset(List<ExportPreset> presets, string filePath, string category)
    {
        try
        {
            var properties = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(filePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                properties[trimmed[..separatorIndex]] = trimmed[(separatorIndex + 1)..];
            }

            if (properties.Count == 0 || properties.ContainsKey("meta.preset.hidden"))
                return;

            // A real consumer preset always names a format or codec - filters out
            // stray non-preset files (READMEs etc.) living in the directory
            if (!properties.ContainsKey("f") && !properties.ContainsKey("vcodec") && !properties.ContainsKey("acodec"))
                return;

            presets.Add(new ExportPreset
            {
                Name = Path.GetFileName(filePath),
                Category = category,
                Properties = properties
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Skipping unreadable preset {filePath}: {ex.Message}");
        }
    }

    private static string? FindPresetDirectory(string? meltExecutablePath)
    {
        if (string.IsNullOrEmpty(meltExecutablePath))
            return null;

        var meltDir = Path.GetDirectoryName(meltExecutablePath);
        if (meltDir == null)
            return null;

        // Shotcut installs melt.exe beside share\; standalone MLT uses bin\ + ..\share
        string[] candidates =
        [
            Path.Combine(meltDir, "share", "mlt", "presets", "consumer", "avformat"),
            Path.Combine(meltDir, "..", "share", "mlt", "presets", "consumer", "avformat")
        ];

        return candidates.FirstOrDefault(Directory.Exists) is { } found
            ? Path.GetFullPath(found)
            : null;
    }
}

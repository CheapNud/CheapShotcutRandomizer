using System.Text.Json;
using System.Diagnostics;
using CheapShotcutRandomizer.Models;
using CheapHelpers.MediaProcessing.Services;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Service for loading and saving application settings to a JSON file in AppData
/// </summary>
public class SettingsService(SvpDetectionService svpDetection)
{
    private readonly string _settingsFilePath = InitializeSettingsPath();
    private readonly SvpDetectionService _svpDetection = svpDetection;
    private AppSettings? _cachedSettings;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static string InitializeSettingsPath()
    {
        // Store settings in user's AppData folder
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "CheapShotcutRandomizer");

        // Ensure directory exists
        Directory.CreateDirectory(appFolder);

        var settingsFilePath = Path.Combine(appFolder, "settings.json");
        Debug.WriteLine($"Settings file path: {settingsFilePath}");

        return settingsFilePath;
    }

    /// <summary>
    /// Loads settings from file, or creates default settings with auto-detected paths if file doesn't exist
    /// </summary>
    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);

                if (loadedSettings != null)
                {
                    _cachedSettings = loadedSettings;
                    Debug.WriteLine("Settings loaded successfully from file");
                    return loadedSettings;
                }
            }

            Debug.WriteLine("No settings file found, creating default settings with auto-detection");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading settings: {ex.Message}");
        }

        // Create default settings with auto-detected executable paths
        _cachedSettings = CreateDefaultSettingsWithDetection();

        // Save the auto-detected settings to file
        await SaveSettingsAsync(_cachedSettings);

        return _cachedSettings;
    }

    /// <summary>
    /// Creates default settings with auto-detected executable paths
    /// </summary>
    private AppSettings CreateDefaultSettingsWithDetection()
    {
        var defaultSettings = new AppSettings();
        var detector = new ExecutableDetectionService(_svpDetection);
        var detected = detector.DetectAll();

        // Populate with detected paths (or leave defaults if not found)
        if (detected.FFmpegPath != null)
        {
            defaultSettings.FFmpegPath = detected.FFmpegPath;
        }

        if (detected.FFprobePath != null)
        {
            defaultSettings.FFprobePath = detected.FFprobePath;
        }
        else
        {
            Debug.WriteLine("WARNING: FFprobe not found! RIFE video validation will be unavailable.");
        }

        if (detected.MeltPath != null)
        {
            defaultSettings.MeltPath = detected.MeltPath;
        }
        else
        {
            // Critical: melt is required for rendering
            Debug.WriteLine("WARNING: Melt not found! Application will not be able to render.");
        }

        // Auto-select NVENC codec if available
        var svp = _svpDetection.DetectSvpInstallation();
        if (svp.FFmpegHasNvenc)
        {
            defaultSettings.DefaultCodec = "hevc_nvenc"; // Better quality and compression than h264_nvenc
            Debug.WriteLine("NVENC detected - defaulting to hevc_nvenc codec");
        }

        return defaultSettings;
    }

    /// <summary>
    /// Saves settings to file
    /// </summary>
    public async Task SaveSettingsAsync(AppSettings updatedSettings)
    {
        await _saveLock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(updatedSettings, options);
            await File.WriteAllTextAsync(_settingsFilePath, json);

            _cachedSettings = updatedSettings;

            if (updatedSettings.VerboseLogging)
            {
                Debug.WriteLine($"Settings saved to: {_settingsFilePath}");
                Debug.WriteLine($"Settings JSON: {json}");
            }
            else
            {
                Debug.WriteLine("Settings saved successfully");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving settings: {ex.Message}");
            throw;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Gets the currently loaded settings without reading from disk
    /// </summary>
    public AppSettings GetCurrentSettings()
    {
        return _cachedSettings ?? new AppSettings();
    }

    /// <summary>
    /// Resets settings to defaults with auto-detection
    /// </summary>
    public async Task ResetToDefaultsAsync()
    {
        var defaultSettings = CreateDefaultSettingsWithDetection();
        await SaveSettingsAsync(defaultSettings);
        Debug.WriteLine("Settings reset to defaults with auto-detection");
    }

    /// <summary>
    /// Gets the settings file path for debugging purposes
    /// </summary>
    public string GetSettingsFilePath() => _settingsFilePath;
}

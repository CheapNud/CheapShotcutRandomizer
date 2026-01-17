using System.Diagnostics;
using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Services.Utilities;

/// <summary>
/// Centralized debug logging utility that respects VerboseLogging setting
/// Always logs: Errors, warnings, lifecycle events (startup/shutdown)
/// Verbose only: Detailed operations, file paths, progress updates, subprocess output
///
/// Note: Uses lazy initialization - loads settings on first use
/// </summary>
public static class DebugLogger
{
    private static readonly Lazy<AppSettings?> _lazySettings = new(() =>
    {
        try
        {
            // Load settings synchronously on first access
            var settingsPath = GetSettingsFilePath();
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch
        {
            // If settings can't be loaded, default to disabled
        }
        return null;
    });

    private static bool? _cachedVerboseLogging;

    /// <summary>
    /// Get settings file path (same logic as SettingsService)
    /// </summary>
    private static string GetSettingsFilePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "CheapShotcutRandomizer");
        return Path.Combine(appFolder, "settings.json");
    }

    /// <summary>
    /// Check if verbose logging is enabled (cached for performance)
    /// </summary>
    private static bool IsVerboseEnabled()
    {
        // Return cached value if available
        if (_cachedVerboseLogging.HasValue)
            return _cachedVerboseLogging.Value;

        // Load from settings file
        var settings = _lazySettings.Value;
        _cachedVerboseLogging = settings?.VerboseLogging ?? false; // Default to false if no settings

        return _cachedVerboseLogging.Value;
    }

    /// <summary>
    /// Force refresh of verbose logging setting (call after settings change)
    /// </summary>
    public static void RefreshSettings()
    {
        _cachedVerboseLogging = null;
    }

    /// <summary>
    /// Always logs: errors, warnings, and critical lifecycle events
    /// </summary>
    public static void Log(string message)
    {
        Debug.WriteLine(message);
    }

    /// <summary>
    /// Always logs: errors, warnings, and critical lifecycle events
    /// </summary>
    public static void Log(string format, params object?[] args)
    {
        Debug.WriteLine(string.Format(format, args));
    }

    /// <summary>
    /// Only logs if VerboseLogging is enabled: detailed operations, paths, progress
    /// </summary>
    public static void LogVerbose(string message)
    {
        if (IsVerboseEnabled())
        {
            Debug.WriteLine(message);
        }
    }

    /// <summary>
    /// Only logs if VerboseLogging is enabled: detailed operations, paths, progress
    /// </summary>
    public static void LogVerbose(string format, params object?[] args)
    {
        if (IsVerboseEnabled())
        {
            Debug.WriteLine(string.Format(format, args));
        }
    }

    /// <summary>
    /// Always logs errors with [ERROR] prefix
    /// </summary>
    public static void LogError(string message)
    {
        Debug.WriteLine($"[ERROR] {message}");
    }

    /// <summary>
    /// Always logs errors with [ERROR] prefix
    /// </summary>
    public static void LogError(string format, params object?[] args)
    {
        Debug.WriteLine($"[ERROR] {string.Format(format, args)}");
    }

    /// <summary>
    /// Always logs warnings with [WARNING] prefix
    /// </summary>
    public static void LogWarning(string message)
    {
        Debug.WriteLine($"[WARNING] {message}");
    }

    /// <summary>
    /// Always logs warnings with [WARNING] prefix
    /// </summary>
    public static void LogWarning(string format, params object?[] args)
    {
        Debug.WriteLine($"[WARNING] {string.Format(format, args)}");
    }
}

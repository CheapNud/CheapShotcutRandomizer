namespace CheapShotcutRandomizer.Models;

/// <summary>
/// Detailed information about a single dependency
/// </summary>
public class DependencyInfo
{
    /// <summary>
    /// Type of dependency
    /// </summary>
    public DependencyType Type { get; set; }

    /// <summary>
    /// Human-readable name of the dependency
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of what this dependency provides
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this dependency is currently installed
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Whether this dependency is required for basic functionality
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Detected version string (if installed)
    /// </summary>
    public string? InstalledVersion { get; set; }

    /// <summary>
    /// Full path to the installed executable or directory
    /// </summary>
    public string? InstalledPath { get; set; }

    /// <summary>
    /// Official download URL for manual installation
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Manual installation instructions (markdown format)
    /// </summary>
    public string? InstallInstructions { get; set; }

    /// <summary>
    /// Whether automated installation is available
    /// </summary>
    public bool SupportsAutomatedInstall { get; set; }

    /// <summary>
    /// Chocolatey package name (if available)
    /// </summary>
    public string? ChocolateyPackage { get; set; }

    /// <summary>
    /// Whether this dependency can be installed as a portable version
    /// </summary>
    public bool SupportsPortableInstall { get; set; }

    /// <summary>
    /// URL for portable download (if available)
    /// </summary>
    public string? PortableDownloadUrl { get; set; }

    /// <summary>
    /// List of alternative dependency names that satisfy this requirement
    /// For example, VapourSynthSourcePlugin can be satisfied by BestSource, L-SMASH, or FFMS2
    /// </summary>
    public List<string> Alternatives { get; set; } = [];

    /// <summary>
    /// Detection status message (for debugging)
    /// </summary>
    public string? DetectionMessage { get; set; }

    /// <summary>
    /// Get a user-friendly status string
    /// </summary>
    public string StatusText => IsInstalled
        ? $"Installed{(InstalledVersion != null ? $" ({InstalledVersion})" : "")}"
        : IsRequired ? "Required - Not Installed" : "Optional - Not Installed";

    /// <summary>
    /// Get icon color based on status
    /// </summary>
    public string StatusColor => IsInstalled
        ? "success"
        : IsRequired ? "error" : "warning";
}

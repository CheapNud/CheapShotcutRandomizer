namespace CheapShotcutRandomizer.Models;

/// <summary>
/// Strategies for installing dependencies
/// </summary>
public enum InstallationStrategy
{
    /// <summary>
    /// Use Chocolatey package manager (Windows)
    /// Requires Chocolatey to be installed
    /// </summary>
    Chocolatey,

    /// <summary>
    /// Download portable version and extract to app directory
    /// No admin rights required
    /// </summary>
    Portable,

    /// <summary>
    /// Download installer and run with silent flags
    /// May require admin rights
    /// </summary>
    Installer,

    /// <summary>
    /// User must manually download and install
    /// Provides instructions and download link
    /// </summary>
    Manual
}

/// <summary>
/// Result of a dependency installation attempt
/// </summary>
public class InstallationResult
{
    /// <summary>
    /// Whether the installation succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Strategy that was used
    /// </summary>
    public InstallationStrategy Strategy { get; set; }

    /// <summary>
    /// User-friendly message about the installation
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Detailed error message (if failed)
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// Path where dependency was installed (if successful)
    /// </summary>
    public string? InstalledPath { get; set; }

    /// <summary>
    /// Whether a system restart is required
    /// </summary>
    public bool RequiresRestart { get; set; }

    /// <summary>
    /// Whether admin elevation was declined by user
    /// </summary>
    public bool AdminDeclined { get; set; }

    /// <summary>
    /// Progress percentage (0-100) for download/install operations
    /// </summary>
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// Create a success result
    /// </summary>
    public static InstallationResult CreateSuccess(InstallationStrategy strategy, string message, string? installedPath = null)
    {
        return new InstallationResult
        {
            Success = true,
            Strategy = strategy,
            Message = message,
            InstalledPath = installedPath,
            ProgressPercentage = 100
        };
    }

    /// <summary>
    /// Create a failure result
    /// </summary>
    public static InstallationResult CreateFailure(InstallationStrategy strategy, string message, string? errorDetails = null)
    {
        return new InstallationResult
        {
            Success = false,
            Strategy = strategy,
            Message = message,
            ErrorDetails = errorDetails,
            ProgressPercentage = 0
        };
    }
}

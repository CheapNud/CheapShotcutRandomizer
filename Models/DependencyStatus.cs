namespace CheapShotcutRandomizer.Models;

/// <summary>
/// Overall dependency status for the application
/// </summary>
public class DependencyStatus
{
    /// <summary>
    /// All dependencies (required and optional)
    /// </summary>
    public List<DependencyInfo> AllDependencies { get; set; } = [];

    /// <summary>
    /// Required dependencies only
    /// </summary>
    public List<DependencyInfo> Required => AllDependencies.Where(d => d.IsRequired).ToList();

    /// <summary>
    /// Optional dependencies only
    /// </summary>
    public List<DependencyInfo> Optional => AllDependencies.Where(d => !d.IsRequired).ToList();

    /// <summary>
    /// Missing dependencies (not installed)
    /// </summary>
    public List<DependencyInfo> Missing => AllDependencies.Where(d => !d.IsInstalled).ToList();

    /// <summary>
    /// Missing required dependencies (critical)
    /// </summary>
    public List<DependencyInfo> MissingRequired => AllDependencies
        .Where(d => d.IsRequired && !d.IsInstalled)
        .ToList();

    /// <summary>
    /// Installed dependencies
    /// </summary>
    public List<DependencyInfo> Installed => AllDependencies.Where(d => d.IsInstalled).ToList();

    /// <summary>
    /// Whether all required dependencies are installed
    /// </summary>
    public bool AllRequiredInstalled => !MissingRequired.Any();

    /// <summary>
    /// Whether all dependencies (including optional) are installed
    /// </summary>
    public bool AllInstalled => !Missing.Any();

    /// <summary>
    /// Overall health percentage (0-100)
    /// </summary>
    public int HealthPercentage
    {
        get
        {
            if (AllDependencies.Count == 0) return 100;

            var requiredWeight = 0.7; // Required dependencies are 70% of health
            var optionalWeight = 0.3; // Optional dependencies are 30% of health

            var requiredCount = Required.Count;
            var optionalCount = Optional.Count;
            var installedRequiredCount = Required.Count(d => d.IsInstalled);
            var installedOptionalCount = Optional.Count(d => d.IsInstalled);

            var requiredHealth = requiredCount > 0 ? (double)installedRequiredCount / requiredCount : 1.0;
            var optionalHealth = optionalCount > 0 ? (double)installedOptionalCount / optionalCount : 1.0;

            return (int)((requiredHealth * requiredWeight + optionalHealth * optionalWeight) * 100);
        }
    }

    /// <summary>
    /// Get health status color
    /// </summary>
    public string HealthColor => HealthPercentage switch
    {
        >= 90 => "success",
        >= 70 => "warning",
        >= 50 => "warning",
        _ => "error"
    };

    /// <summary>
    /// Get user-friendly summary message
    /// </summary>
    public string SummaryMessage
    {
        get
        {
            if (AllRequiredInstalled && AllInstalled)
                return "All dependencies are installed and ready to use.";

            if (AllRequiredInstalled)
                return $"Core functionality available. {Missing.Count} optional dependency(ies) not installed.";

            return $"{MissingRequired.Count} required dependency(ies) missing. Application functionality is limited.";
        }
    }
}

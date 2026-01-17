using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Singleton service to maintain project state across page navigations
/// </summary>
public class ProjectStateService
{
    private Mlt? _currentProject;
    private string _currentProjectPath = string.Empty;

    /// <summary>
    /// Currently loaded project
    /// </summary>
    public Mlt? CurrentProject
    {
        get => _currentProject;
        set
        {
            _currentProject = value;
            OnProjectChanged?.Invoke();
        }
    }

    /// <summary>
    /// Path to the currently loaded project file
    /// </summary>
    public string CurrentProjectPath
    {
        get => _currentProjectPath;
        set
        {
            _currentProjectPath = value;
            OnProjectChanged?.Invoke();
        }
    }

    /// <summary>
    /// Event raised when the project is loaded or changed
    /// </summary>
    public event Action? OnProjectChanged;

    /// <summary>
    /// Check if a project is currently loaded
    /// </summary>
    public bool IsProjectLoaded => _currentProject != null;

    /// <summary>
    /// Clear the currently loaded project
    /// </summary>
    public void ClearProject()
    {
        _currentProject = null;
        _currentProjectPath = string.Empty;
        OnProjectChanged?.Invoke();
    }
}

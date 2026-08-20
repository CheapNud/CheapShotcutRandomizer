using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Velopack auto-update: checks the forge's releases for a newer version at startup,
/// downloads it in the background, and lets the UI offer a restart-to-apply.
/// No-ops when the app runs portable (zip/dev) instead of Velopack-installed.
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "http://192.168.1.15:3000/cheapnud/CheapShotcutRandomizer";

    private UpdateManager? _updateManager;
    private UpdateInfo? _pendingUpdate;

    /// <summary>An update is downloaded and ready; restart applies it.</summary>
    public bool UpdateReady { get; private set; }

    public string? PendingVersion => _pendingUpdate?.TargetFullRelease?.Version?.ToString();

    /// <summary>Raised when UpdateReady changes (from a background thread).</summary>
    public event Action? StateChanged;

    public async Task CheckAndDownloadAsync()
    {
        try
        {
            var updateManager = new UpdateManager(new GiteaSource(RepoUrl, null, false));

            // Portable zip or dev run - Velopack isn't managing this install
            if (!updateManager.IsInstalled)
            {
                Debug.WriteLine("UpdateService: not a Velopack install, skipping update check");
                return;
            }

            var updateInfo = await updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                Debug.WriteLine("UpdateService: no update available");
                return;
            }

            Debug.WriteLine($"UpdateService: downloading {updateInfo.TargetFullRelease?.Version}");
            await updateManager.DownloadUpdatesAsync(updateInfo);

            _updateManager = updateManager;
            _pendingUpdate = updateInfo;
            UpdateReady = true;
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // Update checking is best-effort - never bother the user about it
            Debug.WriteLine($"UpdateService: check failed: {ex.Message}");
        }
    }

    public void ApplyAndRestart()
    {
        if (_updateManager != null && _pendingUpdate != null)
        {
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
        }
    }
}

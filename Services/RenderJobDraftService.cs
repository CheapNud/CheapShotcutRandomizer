namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Carries a pre-filled render job from the Randomizer flows (shuffle/generate)
/// to the Add Render Job stepper page. Singleton; cleared once consumed.
/// </summary>
public class RenderJobDraftService
{
    public string? SourcePath { get; set; }
    public bool ShowTrackSelection { get; set; } = true;
    public bool ShowRenderRange { get; set; } = true;
    public string? PreSelectedVideoTracks { get; set; }
    public string? PreSelectedAudioTracks { get; set; }

    public void Clear()
    {
        SourcePath = null;
        ShowTrackSelection = true;
        ShowRenderRange = true;
        PreSelectedVideoTracks = null;
        PreSelectedAudioTracks = null;
    }
}

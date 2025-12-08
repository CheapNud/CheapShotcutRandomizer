namespace CheapShotcutRandomizer.Core.Models;

/// <summary>
/// Application settings that persist across sessions
/// </summary>
public class AppSettings
{
    // Logging Settings
    public bool VerboseLogging { get; set; } = false;

    // SVP Integration
    public bool UseSvpEncoders { get; set; } = true; // Default to true - SVP has better FFmpeg builds

    // Path Settings
    public string FFmpegPath { get; set; } = "ffmpeg";
    public string FFprobePath { get; set; } = "ffprobe"; // Separate path for ffprobe (usually same directory as ffmpeg)
    public string MeltPath { get; set; } = "melt";
    public string RifePath { get; set; } = "rife-ncnn-vulkan.exe";
    public string RifeVariant { get; set; } = "Vulkan"; // "Vulkan" or "TensorRT"

    // Helper property to get the correct RIFE executable name based on variant
    public string GetRifeExecutableName() =>
        RifeVariant == "TensorRT" ? "rife-tensorrt.exe" : "rife-ncnn-vulkan.exe";

    // Render Default Settings
    public string DefaultQuality { get; set; } = "High";
    public string DefaultCodec { get; set; } = "libx264";
    public int DefaultCrf { get; set; } = 23;
    public string DefaultPreset { get; set; } = "medium";

    // RIFE Default Settings
    public int DefaultRifeModel { get; set; } = 46; // 4.6 (balanced default)
    public int DefaultRifeThreads { get; set; } = 2;
    public bool DefaultRifeUhdMode { get; set; } = false;
    public bool DefaultRifeTtaMode { get; set; } = false;

    // === Advanced RIFE Configuration ===

    // Neural network engine (0=TensorRT, 1=Vulkan, 2=NCNN)
    public int DefaultRifeEngine { get; set; } = 0; // TensorRT

    // GPU threads (parallel GPU streams)
    public int DefaultRifeGpuThreads { get; set; } = 2;

    // AI model name
    public string DefaultRifeModelName { get; set; } = "4.6";

    // GPU device ID
    public int DefaultRifeGpuId { get; set; } = 0;

    // Scene change detection (0=SVP Motion Vectors, 1=Disabled, 2=Threshold-based)
    public int DefaultRifeSceneDetection { get; set; } = 0; // SVP Motion Vectors

    // Scene change processing (0=Repeat Frame, 1=Blend Frames, 2=Interpolate Normally)
    public int DefaultRifeSceneProcessing { get; set; } = 1; // Blend Frames

    // Duplicate frames removal (0=Do Not Remove, 1=Remove Duplicates, 2=Smart Detection)
    public int DefaultRifeDuplicateRemoval { get; set; } = 2; // Smart Detection

    // Frame height for processing (0=Auto)
    public int DefaultRifeFrameHeight { get; set; } = 0; // Auto

    // === Real-ESRGAN AI Upscaling Configuration ===

    // AI Model for upscaling
    public string DefaultEsrganModelName { get; set; } = "RealESRGAN_x4plus";

    // Scale factor (2x or 4x)
    public int DefaultEsrganScaleFactor { get; set; } = 4;

    // Tile size for VRAM management (0=disabled)
    public int DefaultEsrganTileSize { get; set; } = 512;

    // Tile padding to reduce edge artifacts
    public int DefaultEsrganTilePad { get; set; } = 10;

    // GPU device ID
    public int DefaultEsrganGpuId { get; set; } = 0;

    // Use FP16 (half precision) for 50% speed boost
    public bool DefaultEsrganUseFp16 { get; set; } = true;

    // Enable tile mode for VRAM management
    public bool DefaultEsrganTileMode { get; set; } = true;

    // === Real-CUGAN AI Upscaling Configuration (10-13x faster than ESRGAN) ===

    // Denoising level (-1=none, 0=conservative, 1=light, 2=medium, 3=aggressive)
    public int DefaultCuganNoise { get; set; } = -1;

    // Scale factor (2x, 3x, or 4x)
    public int DefaultCuganScale { get; set; } = 2;

    // Backend (0=TensorRT, 1=CUDA, 2=CPU)
    public int DefaultCuganBackend { get; set; } = 0;

    // Use FP16 (half precision) for faster processing
    public bool DefaultCuganUseFp16 { get; set; } = true;

    // Number of parallel GPU streams
    public int DefaultCuganNumStreams { get; set; } = 2;

    // GPU device ID
    public int DefaultCuganGpuId { get; set; } = 0;

    // Application Behavior
    public int MaxConcurrentRenders { get; set; } = 1;
    public bool AutoStartQueue { get; set; } = false;
    public bool ShowNotificationsOnComplete { get; set; } = true;
}

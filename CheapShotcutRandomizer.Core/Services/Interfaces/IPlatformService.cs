namespace CheapShotcutRandomizer.Core.Services.Interfaces;

/// <summary>
/// Platform abstraction for detecting runtime environment and hardware capabilities
/// </summary>
public interface IPlatformService
{
    /// <summary>
    /// Platform identifier (Windows, Linux, Docker)
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// True if running inside a Docker container
    /// </summary>
    bool IsDocker { get; }

    /// <summary>
    /// True if running on Windows
    /// </summary>
    bool IsWindows { get; }

    /// <summary>
    /// Check if NVIDIA GPU is available
    /// </summary>
    Task<bool> IsNvidiaGpuAvailableAsync();

    /// <summary>
    /// Get GPU information (name, VRAM, driver version)
    /// </summary>
    Task<GpuInfo?> GetGpuInfoAsync();

    /// <summary>
    /// Get temporary directory for processing
    /// </summary>
    string GetTempDirectory();

    /// <summary>
    /// Get application data directory
    /// </summary>
    string GetAppDataDirectory();

    /// <summary>
    /// Get CPU information
    /// </summary>
    Task<CpuInfo?> GetCpuInfoAsync();
}

/// <summary>
/// GPU hardware information
/// </summary>
public record GpuInfo(
    string Name,
    long VramBytes,
    string DriverVersion,
    bool SupportsTensorRT,
    bool SupportsNvenc);

/// <summary>
/// CPU hardware information
/// </summary>
public record CpuInfo(
    string Name,
    int CoreCount,
    int ThreadCount);

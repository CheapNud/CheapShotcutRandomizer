using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Hosted service that initializes FFmpegRenderService on application startup
/// This ensures FFMpegCore is configured before any FFProbe/FFMpeg operations
/// </summary>
public class FFmpegInitializationService(FFmpegRenderService ffmpegService) : IHostedService
{
    private readonly FFmpegRenderService _ffmpegService = ffmpegService;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Debug.WriteLine("=== FFmpeg Initialization Service ===");
        Debug.WriteLine("FFmpegRenderService has been initialized.");
        Debug.WriteLine("FFMpegCore is now configured and ready for FFProbe/FFMpeg operations.");
        Debug.WriteLine("======================================");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

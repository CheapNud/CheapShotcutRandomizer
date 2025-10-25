using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using CheapShotcutRandomizer.Data;
using System.Diagnostics;

namespace CheapShotcutRandomizer.Services;

/// <summary>
/// Hosted service that initializes the database on startup
/// </summary>
public class DatabaseInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseInitializationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RenderJobDbContext>();

        // Check if database exists and has correct schema
        var databaseExists = await db.Database.CanConnectAsync(cancellationToken);

        if (databaseExists)
        {
            // Verify schema is up to date by checking for new columns
            try
            {
                // Try to query with new columns - will fail if schema is old
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT InPoint, OutPoint, SelectedVideoTracks, SelectedAudioTracks, FrameRate, IsTwoStageRender, IntermediatePath, OutputFileSizeBytes, IntermediateFileSizeBytes, CurrentStage, IsThreeStageRender, IntermediatePath2, IntermediateFileSizeBytes2, UseRifeInterpolation, UseRealCugan, RealCuganOptionsJson, UseRealEsrgan, RealEsrganOptionsJson, TargetUpscaleResolution, UseNonAiUpscaling, NonAiUpscalingAlgorithm, NonAiUpscalingScaleFactor FROM RenderJobs LIMIT 1",
                    cancellationToken);

                Debug.WriteLine("Database schema is up to date");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Database schema outdated: {ex.Message}");
                Debug.WriteLine("Recreating database with new schema...");

                // Delete old database and recreate
                await db.Database.EnsureDeletedAsync(cancellationToken);
                await db.Database.EnsureCreatedAsync(cancellationToken);

                Debug.WriteLine("Database recreated successfully");
            }
        }
        else
        {
            // Create new database
            await db.Database.EnsureCreatedAsync(cancellationToken);
            Debug.WriteLine("Database created successfully");
        }

        await db.EnableWalModeAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

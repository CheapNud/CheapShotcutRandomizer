using Microsoft.EntityFrameworkCore;
using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Data;

/// <summary>
/// Database context for render job queue
/// </summary>
public class RenderJobDbContext(DbContextOptions<RenderJobDbContext> options) : DbContext(options)
{
    public DbSet<RenderJob> RenderJobs => Set<RenderJob>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Enable SQLite Write-Ahead Logging (WAL) mode for better concurrency
        // WAL allows multiple readers and one writer simultaneously
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=renderjobs.db",
                sqliteOptions =>
                {
                    sqliteOptions.CommandTimeout(60);
                });
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RenderJob>(entity =>
        {
            // Primary key
            entity.HasKey(e => e.Id);

            // Ignore complex properties (not stored in database)
            entity.Ignore(e => e.MeltSettings);
            entity.Ignore(e => e.FFmpegSettings);
            entity.Ignore(e => e.RifeSettings);

            // Unique index on JobId for fast lookups
            entity.HasIndex(e => e.JobId)
                .IsUnique();

            // Index on Status for quick filtering of pending/active jobs
            entity.HasIndex(e => e.Status);

            // Index on ProcessId and MachineName for crash recovery
            entity.HasIndex(e => new { e.ProcessId, e.MachineName });

            // Index on CreatedAt for chronological ordering
            entity.HasIndex(e => e.CreatedAt);

            // Configure string lengths to prevent excessive storage
            entity.Property(e => e.SourceVideoPath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.OutputPath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.RenderSettings)
                .IsRequired();

            entity.Property(e => e.LastError)
                .HasMaxLength(2000);

            entity.Property(e => e.MachineName)
                .HasMaxLength(100);

            entity.Property(e => e.SelectedVideoTracks)
                .HasMaxLength(200);

            entity.Property(e => e.SelectedAudioTracks)
                .HasMaxLength(200);

            entity.Property(e => e.CurrentStage)
                .HasMaxLength(100);

            entity.Property(e => e.RealEsrganOptionsJson)
                .HasMaxLength(2000);

            entity.Property(e => e.IntermediatePath2)
                .HasMaxLength(500);

            // In/Out points for partial rendering (nullable)
            entity.Property(e => e.InPoint);
            entity.Property(e => e.OutPoint);

            // Set default values
            entity.Property(e => e.Status)
                .HasDefaultValue(RenderJobStatus.Pending);

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("datetime('now')");

            entity.Property(e => e.LastUpdatedAt)
                .HasDefaultValueSql("datetime('now')");

            entity.Property(e => e.MaxRetries)
                .HasDefaultValue(3);

            entity.Property(e => e.RetryCount)
                .HasDefaultValue(0);

            entity.Property(e => e.ProgressPercentage)
                .HasDefaultValue(0.0);

            entity.Property(e => e.CurrentFrame)
                .HasDefaultValue(0);
        });
    }

    /// <summary>
    /// Configure SQLite connection to use WAL mode
    /// Must be called after database creation
    /// </summary>
    public async Task EnableWalModeAsync()
    {
        try
        {
            await Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
            await Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
            System.Diagnostics.Debug.WriteLine("SQLite WAL mode enabled successfully");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enable WAL mode: {ex.Message}");
        }
    }
}

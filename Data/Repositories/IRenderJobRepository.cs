using CheapShotcutRandomizer.Core.Models;

namespace CheapShotcutRandomizer.Data.Repositories;

/// <summary>
/// Repository interface for render job operations
/// </summary>
public interface IRenderJobRepository
{
    /// <summary>
    /// Get a job by its unique ID
    /// </summary>
    Task<RenderJob?> GetAsync(Guid jobId);

    /// <summary>
    /// Get all jobs
    /// </summary>
    Task<List<RenderJob>> GetAllAsync();

    /// <summary>
    /// Get jobs by status
    /// </summary>
    Task<List<RenderJob>> GetByStatusAsync(RenderJobStatus status);

    /// <summary>
    /// Get all active jobs (Pending, Running, Paused)
    /// </summary>
    Task<List<RenderJob>> GetActiveJobsAsync();

    /// <summary>
    /// Atomically claim the next pending job for processing
    /// Updates status to Running and sets ProcessId/MachineName
    /// </summary>
    Task<RenderJob?> ClaimNextJobAsync(int processId, string machineName);

    /// <summary>
    /// Add a new job to the database
    /// </summary>
    Task AddAsync(RenderJob renderJob);

    /// <summary>
    /// Update an existing job
    /// </summary>
    Task UpdateAsync(RenderJob renderJob);

    /// <summary>
    /// Update job progress (throttled for performance)
    /// </summary>
    Task UpdateProgressAsync(Guid jobId, double percentage, int currentFrame);

    /// <summary>
    /// Get jobs that were running but appear to have crashed
    /// (Status=Running but ProcessId doesn't match current process)
    /// </summary>
    Task<List<RenderJob>> GetCrashedJobsAsync(int currentProcessId, string machineName);

    /// <summary>
    /// Delete a job from the database
    /// </summary>
    Task DeleteAsync(Guid jobId);
}

using Microsoft.EntityFrameworkCore;
using CheapShotcutRandomizer.Core.Models;
using System.Diagnostics;

namespace CheapShotcutRandomizer.Data.Repositories;

/// <summary>
/// Repository implementation for render job operations
/// </summary>
public class RenderJobRepository(RenderJobDbContext context) : IRenderJobRepository
{
    private readonly RenderJobDbContext _context = context;

    public async Task<RenderJob?> GetAsync(Guid jobId)
    {
        return await _context.RenderJobs
            .FirstOrDefaultAsync(j => j.JobId == jobId);
    }

    public async Task<List<RenderJob>> GetAllAsync()
    {
        return await _context.RenderJobs
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<RenderJob>> GetByStatusAsync(RenderJobStatus status)
    {
        return await _context.RenderJobs
            .Where(j => j.Status == status)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<RenderJob>> GetActiveJobsAsync()
    {
        return await _context.RenderJobs
            .Where(j => j.Status == RenderJobStatus.Pending ||
                       j.Status == RenderJobStatus.Running ||
                       j.Status == RenderJobStatus.Paused)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> TryClaimJobAsync(Guid jobId, int processId, string machineName)
    {
        // Atomic Pending -> Running transition; the WHERE clause makes concurrent
        // claims (stale work items, cancel races) lose cleanly with 0 rows affected
        var claimedRows = await _context.RenderJobs
            .Where(j => j.JobId == jobId && j.Status == RenderJobStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.Status, RenderJobStatus.Running)
                .SetProperty(j => j.ProcessId, processId)
                .SetProperty(j => j.MachineName, machineName)
                .SetProperty(j => j.StartedAt, DateTime.UtcNow)
                .SetProperty(j => j.LastUpdatedAt, DateTime.UtcNow));

        return claimedRows > 0;
    }

    public async Task AddAsync(RenderJob renderJob)
    {
        renderJob.CreatedAt = DateTime.UtcNow;
        renderJob.LastUpdatedAt = DateTime.UtcNow;
        renderJob.QueuedAt = DateTime.UtcNow;

        await _context.RenderJobs.AddAsync(renderJob);
        await _context.SaveChangesAsync();

        Debug.WriteLine($"Added job {renderJob.JobId} to queue");
    }

    public async Task UpdateAsync(RenderJob renderJob)
    {
        renderJob.LastUpdatedAt = DateTime.UtcNow;

        _context.RenderJobs.Update(renderJob);
        await _context.SaveChangesAsync();

        Debug.WriteLine($"Updated job {renderJob.JobId}, Status: {renderJob.Status}");
    }

    public async Task UpdateProgressAsync(Guid jobId, double percentage, int currentFrame)
    {
        // Efficient update without loading the entire entity
        var affectedRows = await _context.RenderJobs
            .Where(j => j.JobId == jobId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(j => j.ProgressPercentage, percentage)
                .SetProperty(j => j.CurrentFrame, currentFrame)
                .SetProperty(j => j.LastUpdatedAt, DateTime.UtcNow));

        if (affectedRows == 0)
        {
            Debug.WriteLine($"Warning: UpdateProgressAsync found no job with ID {jobId}");
        }
    }

    public async Task<List<RenderJob>> GetCrashedJobsAsync(int currentProcessId, string machineName)
    {
        return await _context.RenderJobs
            .Where(j => j.Status == RenderJobStatus.Running &&
                       j.MachineName == machineName &&
                       j.ProcessId != null &&
                       j.ProcessId != currentProcessId)
            .ToListAsync();
    }

    public async Task DeleteAsync(Guid jobId)
    {
        var job = await _context.RenderJobs
            .FirstOrDefaultAsync(j => j.JobId == jobId);

        if (job != null)
        {
            _context.RenderJobs.Remove(job);
            await _context.SaveChangesAsync();

            Debug.WriteLine($"Deleted job {jobId}");
        }
        else
        {
            Debug.WriteLine($"Warning: DeleteAsync found no job with ID {jobId}");
        }
    }
}

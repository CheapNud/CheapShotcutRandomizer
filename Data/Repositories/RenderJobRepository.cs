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

    public async Task<RenderJob?> ClaimNextJobAsync(int processId, string machineName)
    {
        // Use a transaction to atomically claim the next job
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Find the oldest pending job
            var nextJob = await _context.RenderJobs
                .Where(j => j.Status == RenderJobStatus.Pending)
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefaultAsync();

            if (nextJob == null)
            {
                await transaction.CommitAsync();
                return null;
            }

            // Claim the job
            nextJob.Status = RenderJobStatus.Running;
            nextJob.ProcessId = processId;
            nextJob.MachineName = machineName;
            nextJob.StartedAt = DateTime.UtcNow;
            nextJob.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            Debug.WriteLine($"Claimed job {nextJob.JobId} for processing");
            return nextJob;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Debug.WriteLine($"Failed to claim job: {ex.Message}");
            throw;
        }
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

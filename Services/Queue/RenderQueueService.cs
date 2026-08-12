using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json;
using CheapShotcutRandomizer.Models;
using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Data;
using CheapShotcutRandomizer.Data.Repositories;
using CheapHelpers.MediaProcessing.Services;

namespace CheapShotcutRandomizer.Services.Queue;

/// <summary>
/// Main render queue service - processes render jobs in the background
/// </summary>
public class RenderQueueService : BackgroundService, IRenderQueueService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly SemaphoreSlim _concurrencyLimit;
    private readonly int _maxConcurrentRenders;
    private readonly Dictionary<Guid, CancellationTokenSource> _runningJobs = new();
    private readonly object _runningJobsLock = new();

    // Queue control - starts paused by default to prevent immediate encoding
    private volatile bool _queuePaused = true;

    public event EventHandler<RenderProgressEventArgs>? ProgressChanged;
    public event EventHandler<RenderProgressEventArgs>? StatusChanged;
    public event EventHandler<bool>? QueueStatusChanged;

    // Expose queue status
    public bool IsQueuePaused => _queuePaused;

    public RenderQueueService(
        IServiceProvider serviceProvider,
        IBackgroundTaskQueue taskQueue,
        int maxConcurrentRenders = 1)
    {
        _serviceProvider = serviceProvider;
        _taskQueue = taskQueue;
        _maxConcurrentRenders = maxConcurrentRenders;
        _concurrencyLimit = new SemaphoreSlim(_maxConcurrentRenders, _maxConcurrentRenders);

        Debug.WriteLine($"RenderQueueService initialized with max {_maxConcurrentRenders} concurrent renders");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Debug.WriteLine("RenderQueueService starting... (Queue initially PAUSED)");

        // Honor the Auto-start Render Queue setting (GetService: absent in unit tests)
        try
        {
            using var startupScope = _serviceProvider.CreateScope();
            var settingsService = startupScope.ServiceProvider.GetService<SettingsService>();
            if (settingsService != null)
            {
                var appSettings = await settingsService.LoadSettingsAsync();
                if (appSettings.AutoStartQueue)
                {
                    StartQueue();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not read AutoStartQueue setting: {ex.Message}");
        }

        // Sweep generated temp projects left behind by crashes - anything older than
        // 3 days can't belong to a live queue entry worth keeping
        try
        {
            if (Directory.Exists(ShotcutService.GeneratedProjectsTempDir))
            {
                foreach (var staleFile in Directory.GetFiles(ShotcutService.GeneratedProjectsTempDir, "*.mlt")
                             .Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-3)))
                {
                    File.Delete(staleFile);
                    Debug.WriteLine($"Swept stale generated project {staleFile}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Stale temp sweep failed: {ex.Message}");
        }

        // Perform crash recovery on startup
        await RecoverCrashedJobsAsync();

        // Main processing loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // If queue is paused, wait for it to be resumed
                // ponytail: simple poll gate — the old semaphore leaked permits on Start/Stop/Start
                while (_queuePaused)
                {
                    await Task.Delay(250, stoppingToken);
                }

                // Dequeue the next work item
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);

                // Re-check: Pause may have been clicked while we were blocked in DequeueAsync
                if (_queuePaused)
                {
                    await _taskQueue.QueueBackgroundWorkItemAsync(workItem);
                    continue;
                }

                // Wait for available slot (semaphore controls concurrency)
                await _concurrencyLimit.WaitAsync(stoppingToken);

                // Execute work item in background (don't await - allows concurrent processing)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await workItem(stoppingToken);
                    }
                    finally
                    {
                        _concurrencyLimit.Release();
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("RenderQueueService stopping...");
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in RenderQueueService main loop: {ex.Message}");
            }
        }

        Debug.WriteLine("RenderQueueService stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Debug.WriteLine("=== RenderQueueService: Graceful shutdown initiated ===");

        // Thaw any suspended melt processes so cancellation can terminate them
        _serviceProvider.GetService<RenderProcessRegistry>()?.ResumeAll();

        // Cancel all running jobs
        List<Guid> runningJobIds;
        lock (_runningJobsLock)
        {
            runningJobIds = _runningJobs.Keys.ToList();
        }

        if (runningJobIds.Count > 0)
        {
            Debug.WriteLine($"Cancelling {runningJobIds.Count} running render job(s)...");

            foreach (var jobId in runningJobIds)
            {
                try
                {
                    Debug.WriteLine($"Cancelling job {jobId}...");
                    await CancelJobAsync(jobId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cancelling job {jobId}: {ex.Message}");
                }
            }

            // Wait briefly for cancellations to process (max 5 seconds)
            var waitStart = DateTime.UtcNow;
            while ((DateTime.UtcNow - waitStart).TotalSeconds < 5)
            {
                lock (_runningJobsLock)
                {
                    if (_runningJobs.Count == 0)
                    {
                        Debug.WriteLine("All jobs cancelled successfully");
                        break;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            // Force cleanup any remaining jobs
            lock (_runningJobsLock)
            {
                if (_runningJobs.Count > 0)
                {
                    Debug.WriteLine($"WARNING: {_runningJobs.Count} job(s) did not cancel gracefully, forcing cleanup...");
                    foreach (var kvp in _runningJobs.ToList())
                    {
                        try
                        {
                            kvp.Value.Cancel();
                            kvp.Value.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error force-cancelling job {kvp.Key}: {ex.Message}");
                        }
                    }
                    _runningJobs.Clear();
                }
            }
        }
        else
        {
            Debug.WriteLine("No running jobs to cancel");
        }

        Debug.WriteLine("=== RenderQueueService: Graceful shutdown complete ===");

        // Call base implementation to stop the background service
        await base.StopAsync(cancellationToken);
    }

    public async Task<Guid> AddJobAsync(RenderJob renderJob)
    {
        // Add job to database
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        await repository.AddAsync(renderJob);

        // Queue the work item
        await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
        {
            await ProcessJobAsync(renderJob.JobId, ct);
        });

        FireStatusChanged(renderJob.JobId, RenderJobStatus.Pending, 0, 0);

        Debug.WriteLine($"Enqueued job {renderJob.JobId}");
        return renderJob.JobId;
    }

    public async Task<List<RenderJob>> GetCompletedJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();
        return await repository.GetByStatusAsync(RenderJobStatus.Completed);
    }

    public async Task<List<RenderJob>> GetFailedJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        // Get both Failed and DeadLetter jobs
        var failed = await repository.GetByStatusAsync(RenderJobStatus.Failed);
        var deadLetter = await repository.GetByStatusAsync(RenderJobStatus.DeadLetter);

        return failed.Concat(deadLetter).OrderByDescending(j => j.CreatedAt).ToList();
    }

    public async Task<RenderJob?> GetJobAsync(Guid jobId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();
        return await repository.GetAsync(jobId);
    }

    public async Task<List<RenderJob>> GetAllJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();
        return await repository.GetAllAsync();
    }

    public async Task<List<RenderJob>> GetActiveJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();
        return await repository.GetActiveJobsAsync();
    }

    public async Task<int> ClearAllJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        // Cancel all running jobs first
        List<Guid> runningJobIds;
        lock (_runningJobsLock)
        {
            runningJobIds = _runningJobs.Keys.ToList();
        }

        foreach (var jobId in runningJobIds)
        {
            await CancelJobAsync(jobId);
        }

        // Get all jobs and delete them
        var allJobs = await repository.GetAllAsync();
        var jobCount = allJobs.Count;

        foreach (var renderJob in allJobs)
        {
            CleanupGeneratedSource(renderJob);
            await repository.DeleteAsync(renderJob.JobId);
        }

        Debug.WriteLine($"Cleared {jobCount} jobs from queue");
        return jobCount;
    }

    public async Task<bool> CancelJobAsync(Guid jobId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        var renderJob = await repository.GetAsync(jobId);
        if (renderJob == null)
            return false;

        // A suspended process must be thawed first or the graceful shutdown can't reach it
        _serviceProvider.GetService<RenderProcessRegistry>()?.TryResume(jobId);

        // Cancel running job
        lock (_runningJobsLock)
        {
            if (_runningJobs.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
                _runningJobs.Remove(jobId);
            }
        }

        // Update status
        renderJob.Status = RenderJobStatus.Cancelled;
        renderJob.CompletedAt = DateTime.UtcNow;
        await repository.UpdateAsync(renderJob);

        CleanupGeneratedSource(renderJob);

        FireStatusChanged(jobId, RenderJobStatus.Cancelled, renderJob.ProgressPercentage, renderJob.CurrentFrame);

        Debug.WriteLine($"Cancelled job {jobId}");
        return true;
    }

    public async Task<bool> PauseJobAsync(Guid jobId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        var renderJob = await repository.GetAsync(jobId);
        if (renderJob == null || renderJob.Status != RenderJobStatus.Running)
            return false;

        // Preferred: freeze the melt process in place so no progress is lost.
        // The CTS and concurrency slot stay held - resume just thaws the process.
        var registry = _serviceProvider.GetService<RenderProcessRegistry>();
        if (registry != null && registry.TrySuspend(jobId))
        {
            Debug.WriteLine($"Suspended melt process for job {jobId}");
        }
        else
        {
            // Fallback (process not started yet / registry missing): kill-based pause
            lock (_runningJobsLock)
            {
                if (_runningJobs.TryGetValue(jobId, out var cts))
                {
                    cts.Cancel();
                    _runningJobs.Remove(jobId);
                }
            }
        }

        renderJob.Status = RenderJobStatus.Paused;
        await repository.UpdateAsync(renderJob);

        FireStatusChanged(jobId, RenderJobStatus.Paused, renderJob.ProgressPercentage, renderJob.CurrentFrame);

        Debug.WriteLine($"Paused job {jobId}");
        return true;
    }

    public async Task<bool> ResumeJobAsync(Guid jobId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        var renderJob = await repository.GetAsync(jobId);
        if (renderJob == null || renderJob.Status != RenderJobStatus.Paused)
            return false;

        // Preferred: thaw the suspended melt process and continue where it left off
        var registry = _serviceProvider.GetService<RenderProcessRegistry>();
        if (registry != null && registry.TryResume(jobId))
        {
            renderJob.Status = RenderJobStatus.Running;
            await repository.UpdateAsync(renderJob);

            FireStatusChanged(jobId, RenderJobStatus.Running, renderJob.ProgressPercentage, renderJob.CurrentFrame);

            Debug.WriteLine($"Resumed suspended melt process for job {jobId}");
            return true;
        }

        // Fallback (paused via kill or app was restarted): re-enqueue from scratch
        renderJob.Status = RenderJobStatus.Pending;
        await repository.UpdateAsync(renderJob);

        await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
        {
            await ProcessJobAsync(jobId, ct);
        });

        FireStatusChanged(jobId, RenderJobStatus.Pending, renderJob.ProgressPercentage, renderJob.CurrentFrame);

        Debug.WriteLine($"Resumed job {jobId}");
        return true;
    }

    public async Task<bool> RetryJobAsync(Guid jobId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        var renderJob = await repository.GetAsync(jobId);
        if (renderJob == null)
            return false;

        if (renderJob.Status != RenderJobStatus.Failed && renderJob.Status != RenderJobStatus.DeadLetter)
            return false;

        // Reset job for retry
        renderJob.Status = RenderJobStatus.Pending;
        renderJob.RetryCount = 0;
        renderJob.ProgressPercentage = 0;
        renderJob.CurrentFrame = 0;
        renderJob.LastError = null;
        renderJob.ErrorStackTrace = null;
        await repository.UpdateAsync(renderJob);

        await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
        {
            await ProcessJobAsync(jobId, ct);
        });

        FireStatusChanged(jobId, RenderJobStatus.Pending, 0, 0);

        Debug.WriteLine($"Retrying job {jobId}");
        return true;
    }

    /// <summary>
    /// Start the render queue to begin processing jobs
    /// </summary>
    public void StartQueue()
    {
        if (!_queuePaused)
        {
            Debug.WriteLine("Queue is already running");
            return;
        }

        Debug.WriteLine("Starting render queue...");
        _queuePaused = false;
        QueueStatusChanged?.Invoke(this, false); // false = not paused = running
        Debug.WriteLine("Render queue started");
    }

    /// <summary>
    /// Stop/pause the render queue to prevent processing new jobs
    /// NOTE: Currently running jobs will continue to completion
    /// </summary>
    public void StopQueue()
    {
        if (_queuePaused)
        {
            Debug.WriteLine("Queue is already paused");
            return;
        }

        Debug.WriteLine("Pausing render queue...");
        _queuePaused = true;
        QueueStatusChanged?.Invoke(this, true); // true = paused
        Debug.WriteLine("Render queue paused");
    }

    /// <summary>
    /// Get current queue statistics
    /// </summary>
    public async Task<QueueStatistics> GetQueueStatisticsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        var allJobs = await repository.GetAllAsync();

        int runningCount;
        lock (_runningJobsLock)
        {
            runningCount = _runningJobs.Count;
        }

        return new QueueStatistics
        {
            IsQueuePaused = _queuePaused,
            PendingCount = allJobs.Count(j => j.Status == RenderJobStatus.Pending),
            RunningCount = runningCount,
            CompletedCount = allJobs.Count(j => j.Status == RenderJobStatus.Completed),
            FailedCount = allJobs.Count(j => j.Status == RenderJobStatus.Failed || j.Status == RenderJobStatus.DeadLetter),
            TotalCount = allJobs.Count
        };
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

        RenderJob? renderJob = null;
        CancellationTokenSource? jobCts = null;

        try
        {
            // Get the job
            renderJob = await repository.GetAsync(jobId);
            if (renderJob == null)
            {
                Debug.WriteLine($"Job {jobId} not found");
                return;
            }

            // Register the CTS before claiming so a cancel arriving mid-claim always has something to cancel
            jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_runningJobsLock)
            {
                _runningJobs[jobId] = jobCts;
            }

            // Atomic Pending -> Running claim; loses cleanly to concurrent cancels and stale work items
            var claimed = await repository.TryClaimJobAsync(jobId, Environment.ProcessId, Environment.MachineName);
            if (!claimed)
            {
                Debug.WriteLine($"Job {jobId} is not pending (status: {renderJob.Status}) - skipping");
                return;
            }

            // Keep the local entity in sync so the completion update doesn't clobber the claim
            renderJob.Status = RenderJobStatus.Running;
            renderJob.StartedAt = DateTime.UtcNow;
            renderJob.ProcessId = Environment.ProcessId;
            renderJob.MachineName = Environment.MachineName;

            FireStatusChanged(jobId, RenderJobStatus.Running, 0, 0);

            // Execute MLT render (single-stage pipeline)
            bool renderSuccess = await ExecuteMltRenderAsync(renderJob, jobCts.Token, scope, jobId);
            // Update final status
            if (renderSuccess)
            {
                renderJob.Status = RenderJobStatus.Completed;
                renderJob.ProgressPercentage = 100;
                renderJob.CompletedAt = DateTime.UtcNow;

                // Record output file size
                if (File.Exists(renderJob.OutputPath))
                {
                    var fileInfo = new FileInfo(renderJob.OutputPath);
                    renderJob.OutputFileSizeBytes = fileInfo.Length;
                    Debug.WriteLine($"Output file size: {renderJob.GetOutputFileSizeFormatted()}");
                }

                await repository.UpdateAsync(renderJob);

                await RunPostActionAsync(renderJob, repository);
                CleanupGeneratedSource(renderJob);

                FireStatusChanged(jobId, RenderJobStatus.Completed, 100, renderJob.CurrentFrame);
                Debug.WriteLine($"Job {jobId} completed successfully");
            }
            else
            {
                throw new Exception("Render failed");
            }
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Job {jobId} was cancelled");
            // Status already updated by CancelJobAsync or PauseJobAsync
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Job {jobId} failed: {ex.Message}");

            if (renderJob != null)
            {
                renderJob.LastError = ex.Message;
                renderJob.ErrorStackTrace = ex.StackTrace;
                renderJob.RetryCount++;

                // Determine if we should retry or move to dead letter
                if (renderJob.RetryCount >= renderJob.MaxRetries)
                {
                    renderJob.Status = RenderJobStatus.DeadLetter;
                    renderJob.CompletedAt = DateTime.UtcNow;
                    await repository.UpdateAsync(renderJob);

                    // No source cleanup here: dead-lettered jobs can be manually retried,
                    // which still needs the generated file. Clear All or the startup
                    // sweep reclaims it eventually.
                    FireStatusChanged(jobId, RenderJobStatus.DeadLetter, renderJob.ProgressPercentage,
                        renderJob.CurrentFrame, ex.Message);

                    Debug.WriteLine($"Job {jobId} moved to dead letter queue after {renderJob.RetryCount} retries");
                }
                else
                {
                    // Retry in safe mode: parallel frame processing (real_time < -1) is
                    // the most common melt failure cause, so force single-threaded MLT
                    // processing on the retry (encoder threads are unaffected)
                    try
                    {
                        var retrySettings = JsonSerializer.Deserialize<MeltRenderSettings>(renderJob.RenderSettings);
                        if (retrySettings != null && retrySettings.ThreadCount != 1)
                        {
                            retrySettings.ThreadCount = 1;
                            renderJob.RenderSettings = JsonSerializer.Serialize(retrySettings);
                            Debug.WriteLine($"Job {jobId} retry will run with single-threaded MLT processing");
                        }
                    }
                    catch (Exception settingsEx)
                    {
                        Debug.WriteLine($"Could not adjust retry settings: {settingsEx.Message}");
                    }

                    // Retry with exponential backoff
                    renderJob.Status = RenderJobStatus.Pending;
                    await repository.UpdateAsync(renderJob);

                    var delaySeconds = Math.Pow(2, renderJob.RetryCount);
                    Debug.WriteLine($"Job {jobId} will retry in {delaySeconds} seconds (attempt {renderJob.RetryCount}/{renderJob.MaxRetries})");

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);

                    // Re-enqueue
                    await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
                    {
                        await ProcessJobAsync(jobId, ct);
                    });

                    FireStatusChanged(jobId, RenderJobStatus.Pending, renderJob.ProgressPercentage,
                        renderJob.CurrentFrame, $"Retry {renderJob.RetryCount}/{renderJob.MaxRetries}");
                }
            }
        }
        finally
        {
            // Clean up cancellation token
            lock (_runningJobsLock)
            {
                _runningJobs.Remove(jobId);
            }
            jobCts?.Dispose();
        }
    }

    /// <summary>
    /// Delete a generated shuffle/random temp project once its job reaches a terminal
    /// state. Only ever touches files inside our own temp subfolder - never user files.
    /// </summary>
    private static void CleanupGeneratedSource(RenderJob renderJob)
    {
        try
        {
            if (ShotcutService.IsGeneratedTempProject(renderJob.SourceVideoPath)
                && File.Exists(renderJob.SourceVideoPath))
            {
                File.Delete(renderJob.SourceVideoPath);
                Debug.WriteLine($"Deleted generated temp project {renderJob.SourceVideoPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not delete generated temp project: {ex.Message}");
        }
    }

    /// <summary>
    /// Post-completion action (move output / show in Explorer). Best-effort:
    /// a failed action logs but never fails the completed job.
    /// </summary>
    private static async Task RunPostActionAsync(RenderJob renderJob, IRenderJobRepository repository)
    {
        try
        {
            switch (renderJob.PostAction)
            {
                case "move" when !string.IsNullOrEmpty(renderJob.PostActionTarget):
                    Directory.CreateDirectory(renderJob.PostActionTarget);

                    var fileName = Path.GetFileName(renderJob.OutputPath);
                    var destination = Path.Combine(renderJob.PostActionTarget, fileName);

                    // Never overwrite an existing file - suffix instead
                    var suffix = 1;
                    while (File.Exists(destination))
                    {
                        destination = Path.Combine(renderJob.PostActionTarget,
                            $"{Path.GetFileNameWithoutExtension(fileName)}_{suffix++}{Path.GetExtension(fileName)}");
                    }

                    File.Move(renderJob.OutputPath, destination);
                    Debug.WriteLine($"Moved output to {destination}");

                    // Keep the job record pointing at the real file location
                    renderJob.OutputPath = destination;
                    await repository.UpdateAsync(renderJob);
                    break;

                case "open-folder":
                    using (Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{renderJob.OutputPath}\""
                    })) { }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Post-completion action '{renderJob.PostAction}' failed: {ex.Message}");
        }
    }

    private async Task<bool> ExecuteMltRenderAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Executing MLT render for job {jobId}");

        // Deserialize MLT settings
        var settings = JsonSerializer.Deserialize<MeltRenderSettings>(renderJob.RenderSettings);
        if (settings == null)
        {
            throw new InvalidOperationException("Failed to deserialize MLT render settings");
        }

        // Create progress reporter
        var progress = CreateRenderProgressReporter(jobId);

        // Execute the render
        var xmlService = scope.ServiceProvider.GetRequiredService<CheapHelpers.Services.DataExchange.Xml.IXmlService>();
        var shotcutService = scope.ServiceProvider.GetRequiredService<ShotcutService>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var appSettings = await settingsService.LoadSettingsAsync();

        var meltService = new MeltRenderService(
            meltExecutable: appSettings.MeltPath,
            xmlService: xmlService,
            shotcutService: shotcutService);

        Debug.WriteLine($"MLT rendering to: {renderJob.OutputPath}");

        var success = await meltService.RenderAsync(
            renderJob.SourceVideoPath,
            renderJob.OutputPath,
            settings,
            progress,
            cancellationToken,
            renderJob.InPoint,
            renderJob.OutPoint,
            renderJob.SelectedVideoTracks,
            renderJob.SelectedAudioTracks,
            jobId,
            _serviceProvider.GetService<RenderProcessRegistry>());

        return success;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    private IProgress<RenderProgress> CreateRenderProgressReporter(Guid jobId)
    {
        // Baseline at the first progress report, not job start - melt/MLT startup cost
        // otherwise poisons the ETA for the first minutes of every render
        DateTime? encodeStart = null;
        var lastProgressUpdate = DateTime.UtcNow;
        var lastEventFired = DateTime.UtcNow; // Track UI event throttling

        return new Progress<RenderProgress>(renderProgress =>
        {
            var now = DateTime.UtcNow;
            encodeStart ??= now;

            // Reset the system idle timer so the machine doesn't sleep mid-render
            SetThreadExecutionState(ES_SYSTEM_REQUIRED);

            // Throttle database updates to every 1 second
            if ((now - lastProgressUpdate).TotalSeconds >= 1)
            {
                lastProgressUpdate = now;

                // Update database (fire and forget for performance)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var updateScope = _serviceProvider.CreateScope();
                        var updateRepo = updateScope.ServiceProvider.GetRequiredService<IRenderJobRepository>();
                        await updateRepo.UpdateProgressAsync(jobId, renderProgress.Percentage, renderProgress.CurrentFrame);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating progress: {ex.Message}");
                    }
                });
            }

            // Throttle UI event firing to 100ms (10 fps max) to prevent progress bar glitching
            if ((now - lastEventFired).TotalMilliseconds < 100)
                return;

            lastEventFired = now;

            var elapsed = now - encodeStart.Value;
            TimeSpan? remaining = null;
            // Below 2% the rate estimate is noise - suppress like Shotcut does
            if (renderProgress.Percentage > 2)
            {
                var totalEstimated = elapsed.TotalSeconds / (renderProgress.Percentage / 100.0);
                remaining = TimeSpan.FromSeconds(totalEstimated - elapsed.TotalSeconds);
            }

            FireProgressChanged(jobId, RenderJobStatus.Running, renderProgress.Percentage,
                renderProgress.CurrentFrame, null, elapsed, remaining);
        });
    }

    private async Task RecoverCrashedJobsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

            var crashedJobs = await repository.GetCrashedJobsAsync(
                Environment.ProcessId,
                Environment.MachineName);

            if (crashedJobs.Count == 0)
            {
                Debug.WriteLine("No crashed jobs found");
                return;
            }

            Debug.WriteLine($"Found {crashedJobs.Count} crashed jobs, recovering...");

            foreach (var crashedJob in crashedJobs)
            {
                crashedJob.Status = RenderJobStatus.Pending;
                crashedJob.RetryCount++;
                crashedJob.ProcessId = null;
                crashedJob.MachineName = null;
                crashedJob.LastError = "Job recovered after process crash";

                // Move to dead letter if too many retries
                if (crashedJob.RetryCount >= crashedJob.MaxRetries)
                {
                    crashedJob.Status = RenderJobStatus.DeadLetter;
                    crashedJob.CompletedAt = DateTime.UtcNow;
                }

                await repository.UpdateAsync(crashedJob);

                // No enqueue here - the Pending sweep below picks these up, and
                // enqueueing in both places produced duplicate work items
                Debug.WriteLine($"Recovered crashed job {crashedJob.JobId}, status: {crashedJob.Status}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during crash recovery: {ex.Message}");
        }

        // Re-enqueue Pending jobs: after a restart no work item exists for them,
        // so without this they'd sit as Pending in the UI forever
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();

            var pendingJobs = await repository.GetByStatusAsync(RenderJobStatus.Pending);
            foreach (var pendingJob in pendingJobs)
            {
                await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
                {
                    await ProcessJobAsync(pendingJob.JobId, ct);
                });
                Debug.WriteLine($"Re-enqueued pending job {pendingJob.JobId} after restart");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error re-enqueueing pending jobs: {ex.Message}");
        }
    }

    private void FireProgressChanged(Guid jobId, RenderJobStatus status, double percentage,
        int currentFrame, int? totalFrames, TimeSpan? elapsed, TimeSpan? remaining)
    {
        ProgressChanged?.Invoke(this, new RenderProgressEventArgs
        {
            JobId = jobId,
            Status = status,
            ProgressPercentage = percentage,
            CurrentFrame = currentFrame,
            TotalFrames = totalFrames ?? 0,
            ElapsedTime = elapsed,
            EstimatedTimeRemaining = remaining
        });
    }

    private void FireStatusChanged(Guid jobId, RenderJobStatus status, double percentage,
        int currentFrame, string? errorMessage = null)
    {
        StatusChanged?.Invoke(this, new RenderProgressEventArgs
        {
            JobId = jobId,
            Status = status,
            ProgressPercentage = percentage,
            CurrentFrame = currentFrame,
            ErrorMessage = errorMessage
        });
    }
}

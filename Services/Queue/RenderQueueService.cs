using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json;
using CheapShotcutRandomizer.Models;
using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Data;
using CheapShotcutRandomizer.Data.Repositories;
using Polly;
using Polly.Retry;
using CheapShotcutRandomizer.Services.Utilities;
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
    private readonly ResiliencePipeline _retryPipeline;

    // Queue control - starts paused by default to prevent immediate encoding
    private volatile bool _queuePaused = true;
    private readonly SemaphoreSlim _pauseSemaphore = new(0); // Starts with 0 available slots (paused)

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

        // Configure retry policy with exponential backoff
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1, // We handle retries manually in ProcessJobAsync, set to 1 to satisfy Polly validation
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();

        Debug.WriteLine($"RenderQueueService initialized with max {_maxConcurrentRenders} concurrent renders");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Debug.WriteLine("RenderQueueService starting... (Queue initially PAUSED)");

        // Perform crash recovery on startup
        await RecoverCrashedJobsAsync();

        // Main processing loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // If queue is paused, wait for it to be resumed
                if (_queuePaused)
                {
                    Debug.WriteLine("Queue is paused. Waiting for resume signal...");
                    await _pauseSemaphore.WaitAsync(stoppingToken);

                    // Double-check we weren't stopped while waiting
                    if (stoppingToken.IsCancellationRequested)
                        break;
                }

                // Dequeue the next work item
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);

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
        return await EnqueueJobAsync(renderJob);
    }

    public async Task<Guid> EnqueueJobAsync(RenderJob renderJob)
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

        // Cancel the running job (will be handled as pause)
        lock (_runningJobsLock)
        {
            if (_runningJobs.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
                _runningJobs.Remove(jobId);
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

        // Reset to pending and re-enqueue
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
        _pauseSemaphore.Release(); // Signal the processing loop to continue
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

            // Skip if not pending
            if (renderJob.Status != RenderJobStatus.Pending)
            {
                Debug.WriteLine($"Job {jobId} is not pending (status: {renderJob.Status})");
                return;
            }

            // Create cancellation token for this job
            jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_runningJobsLock)
            {
                _runningJobs[jobId] = jobCts;
            }

            // Update status to running
            renderJob.Status = RenderJobStatus.Running;
            renderJob.StartedAt = DateTime.UtcNow;
            renderJob.ProcessId = Environment.ProcessId;
            renderJob.MachineName = Environment.MachineName;

            // Set initial stage for multi-stage renders
            // Pipeline order: MLT → UPSCALING (AI or non-AI) → RIFE (upscaling before RIFE for performance!)
            if (renderJob.IsThreeStageRender)
            {
                renderJob.CurrentStage = "Stage 1: MLT Render";
            }
            else if (renderJob.IsTwoStageRender)
            {
                if (renderJob.RenderType == RenderType.MltSource)
                    renderJob.CurrentStage = "Stage 1: MLT Render";
                else if (renderJob.UseRealCugan)
                    renderJob.CurrentStage = "Stage 1: Real-CUGAN Upscaling";
                else if (renderJob.UseRealEsrgan)
                    renderJob.CurrentStage = "Stage 1: Real-ESRGAN Upscaling";
                else if (renderJob.UseNonAiUpscaling)
                    renderJob.CurrentStage = "Stage 1: Non-AI Upscaling";
                else
                    renderJob.CurrentStage = "Stage 1: RIFE Interpolation";
            }

            await repository.UpdateAsync(renderJob);

            FireStatusChanged(jobId, RenderJobStatus.Running, 0, 0);

            // Execute render pipeline based on source type and processing options
            // IMPORTANT: Upscaling (AI or non-AI) before RIFE to minimize frames processed
            bool renderSuccess = true;

            // Step 1: MLT Render (if source is MLT)
            if (renderJob.RenderType == RenderType.MltSource)
            {
                renderSuccess = await ExecuteMltRenderAsync(renderJob, jobCts.Token, scope, jobId);
                if (!renderSuccess) goto RenderComplete;
            }

            // Step 2: Upscaling (if enabled) - BEFORE RIFE to minimize frame count!
            // Use either AI (Real-CUGAN, Real-ESRGAN) or Non-AI (xBR/Lanczos/HQx) upscaling
            if (renderJob.UseRealCugan && renderSuccess)
            {
                renderSuccess = await ApplyRealCuganPostProcessingAsync(renderJob, jobCts.Token, scope, jobId);
                if (!renderSuccess) goto RenderComplete;
            }
            else if (renderJob.UseRealEsrgan && renderSuccess)
            {
                renderSuccess = await ApplyRealEsrganPostProcessingAsync(renderJob, jobCts.Token, scope, jobId);
                if (!renderSuccess) goto RenderComplete;
            }
            else if (renderJob.UseNonAiUpscaling && renderSuccess)
            {
                renderSuccess = await ApplyNonAiUpscalingPostProcessingAsync(renderJob, jobCts.Token, scope, jobId);
                if (!renderSuccess) goto RenderComplete;
            }

            // Step 3: RIFE Interpolation (if enabled) - AFTER upscaling for optimal performance
            if (renderJob.UseRifeInterpolation && renderSuccess)
            {
                // RIFE reads from upscaling output (IntermediatePath2) if upscaling ran, else from MLT or source
                renderSuccess = await ExecuteRifeRenderAsync(renderJob, jobCts.Token, scope, jobId);
            }

        RenderComplete:
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

                    FireStatusChanged(jobId, RenderJobStatus.DeadLetter, renderJob.ProgressPercentage,
                        renderJob.CurrentFrame, ex.Message);

                    Debug.WriteLine($"Job {jobId} moved to dead letter queue after {renderJob.RetryCount} retries");
                }
                else
                {
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

        // Determine output path:
        // - If RIFE/ESRGAN/CUGAN will run next: write to IntermediatePath
        // - Otherwise: write directly to OutputPath
        string mltOutputPath = (renderJob.UseRifeInterpolation || renderJob.UseRealEsrgan || renderJob.UseRealCugan)
            ? renderJob.IntermediatePath ?? renderJob.OutputPath
            : renderJob.OutputPath;

        Debug.WriteLine($"MLT rendering to: {mltOutputPath}");

        var success = await meltService.RenderAsync(
            renderJob.SourceVideoPath,
            mltOutputPath,
            settings,
            progress,
            cancellationToken,
            renderJob.InPoint,
            renderJob.OutPoint,
            renderJob.SelectedVideoTracks,
            renderJob.SelectedAudioTracks);

        // Update IntermediatePath if post-processing will run
        if (success && (renderJob.UseRifeInterpolation || renderJob.UseRealEsrgan || renderJob.UseRealCugan))
        {
            renderJob.IntermediatePath = mltOutputPath;
            Debug.WriteLine($"Set IntermediatePath for post-processing: {mltOutputPath}");
        }

        return success;
    }

    private async Task<bool> ExecuteRifeRenderAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Executing RIFE render for job {jobId}");

        // Update stage text (RIFE now runs as Stage 3 after ESRGAN, or Stage 2 if no ESRGAN)
        renderJob.CurrentStage = renderJob.IsThreeStageRender
            ? "Stage 3: RIFE Interpolation"
            : renderJob.IsTwoStageRender
                ? "Stage 2: RIFE Interpolation"
                : "RIFE Interpolation";

        // Save stage update to database so UI shows correct stage
        var db = scope.ServiceProvider.GetRequiredService<RenderJobDbContext>();
        db.RenderJobs.Update(renderJob);
        await db.SaveChangesAsync(cancellationToken);
        Debug.WriteLine($"Updated job stage to: {renderJob.CurrentStage}");

        // Deserialize FFmpeg settings
        var ffmpegSettings = JsonSerializer.Deserialize<FFmpegRenderSettings>(renderJob.RenderSettings);
        if (ffmpegSettings == null)
        {
            throw new InvalidOperationException("Failed to deserialize FFmpeg render settings");
        }

        // Ensure FFmpeg path is set
        if (string.IsNullOrEmpty(ffmpegSettings.FFmpegPath))
        {
            // Try to get FFmpeg path from settings or auto-detect
            var settingsService = scope.ServiceProvider.GetService<SettingsService>();
            if (settingsService != null)
            {
                var settings = await settingsService.LoadSettingsAsync();
                ffmpegSettings.FFmpegPath = settings.FFmpegPath;
            }

            // Fallback to SVP's FFmpeg if available
            if (string.IsNullOrEmpty(ffmpegSettings.FFmpegPath))
            {
                ffmpegSettings.FFmpegPath = @"C:\Program Files (x86)\SVP 4\utils\ffmpeg.exe";
                if (!File.Exists(ffmpegSettings.FFmpegPath))
                {
                    ffmpegSettings.FFmpegPath = "ffmpeg"; // Hope it's in PATH
                }
            }
        }

        // Get RIFE services
        var rifePipeline = scope.ServiceProvider.GetRequiredService<RifeVideoProcessingPipeline>();

        // Create RIFE pipeline options
        var pipelineOptions = new RifePipelineOptions
        {
            RifeOptions = new RifeOptions
            {
                InterpolationPasses = renderJob.RifeSettings?.InterpolationMultiplier switch
                {
                    2 => 1,
                    4 => 2,
                    8 => 3,
                    _ => 1
                }
            },
            FFmpegSettings = ffmpegSettings,
            InputFps = (int)renderJob.FrameRate,
            ValidateFrameCounts = true,
            KeepTemporaryFiles = false,
            UseHardwareDecode = true
        };

        // Create progress reporter (convert VideoProcessingProgress to RenderProgress)
        var lastRifeSingleStageUpdate = DateTime.UtcNow;
        var progress = new Progress<VideoProcessingProgress>(vProgress =>
        {
            // Throttle to 100ms (10 fps max) to prevent progress bar glitching
            var now = DateTime.UtcNow;
            if ((now - lastRifeSingleStageUpdate).TotalMilliseconds < 100)
                return;

            lastRifeSingleStageUpdate = now;

            var percentage = vProgress.OverallProgress;
            FireProgressChanged(jobId, RenderJobStatus.Running, percentage, 0, null,
                TimeSpan.Zero, null);
        });

        // Determine input path (ESRGAN now runs before RIFE):
        // - If ESRGAN ran: read from IntermediatePath2 (ESRGAN output)
        // - Else if MLT ran: read from IntermediatePath (MLT output)
        // - Otherwise: read from SourceVideoPath (direct video source)
        string rifeInputPath;
        if (!string.IsNullOrEmpty(renderJob.IntermediatePath2) && File.Exists(renderJob.IntermediatePath2))
        {
            rifeInputPath = renderJob.IntermediatePath2;
            Debug.WriteLine($"RIFE reading from ESRGAN output: {rifeInputPath}");
        }
        else if (renderJob.RenderType == RenderType.MltSource && !string.IsNullOrEmpty(renderJob.IntermediatePath))
        {
            rifeInputPath = renderJob.IntermediatePath;
            Debug.WriteLine($"RIFE reading from MLT output: {rifeInputPath}");
        }
        else
        {
            rifeInputPath = renderJob.SourceVideoPath;
            Debug.WriteLine($"RIFE reading from source video: {rifeInputPath}");
        }

        Debug.WriteLine($"RIFE writing to: {renderJob.OutputPath}");

        return await rifePipeline.ProcessVideoAsync(
            rifeInputPath,
            renderJob.OutputPath,
            pipelineOptions,
            progress,
            cancellationToken);
    }

    private async Task<bool> ExecuteRealEsrganRenderAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Executing Real-ESRGAN upscaling for job {jobId}");

        // Deserialize Real-ESRGAN options
        RealEsrganOptions? esrganOptions = null;
        if (!string.IsNullOrEmpty(renderJob.RealEsrganOptionsJson))
        {
            esrganOptions = JsonSerializer.Deserialize<RealEsrganOptions>(renderJob.RealEsrganOptionsJson);
        }

        // Use recommended settings if not specified
        if (esrganOptions == null)
        {
            Debug.WriteLine("No Real-ESRGAN options found, using recommended settings for 720p");
            esrganOptions = RealEsrganOptions.GetRecommendedSettings(720);
        }

        Debug.WriteLine($"Real-ESRGAN settings: Model={esrganOptions.ModelName}, Scale={esrganOptions.ScaleFactor}x, Tile={esrganOptions.TileSize}px");

        // Get FFmpeg path from settings
        var settingsService = scope.ServiceProvider.GetService<SettingsService>();
        string? ffmpegPath = null;
        if (settingsService != null)
        {
            var appSettings = await settingsService.LoadSettingsAsync();
            ffmpegPath = appSettings.FFmpegPath;
        }

        // Fallback to SVP's FFmpeg if available
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            ffmpegPath = @"C:\Program Files (x86)\SVP 4\utils\ffmpeg.exe";
            if (!File.Exists(ffmpegPath))
            {
                ffmpegPath = "ffmpeg"; // Hope it's in PATH
            }
        }

        // Create progress reporter
        var lastProgressUpdate = DateTime.UtcNow;
        var progress = new Progress<double>(esrProgress =>
        {
            // Throttle to 100ms (10 fps max) to prevent progress bar glitching
            var now = DateTime.UtcNow;
            if ((now - lastProgressUpdate).TotalMilliseconds < 100)
                return;

            lastProgressUpdate = now;

            FireProgressChanged(jobId, RenderJobStatus.Running, esrProgress, 0, null,
                TimeSpan.Zero, null);
        });

        // Execute upscaling
        var esrganService = scope.ServiceProvider.GetRequiredService<Services.RealESRGAN.RealEsrganService>();

        return await esrganService.UpscaleVideoAsync(
            renderJob.SourceVideoPath,
            renderJob.OutputPath,
            esrganOptions,
            progress,
            cancellationToken,
            ffmpegPath);
    }

    private async Task<bool> ApplyRealEsrganPostProcessingAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Applying Real-ESRGAN post-processing for job {jobId}");

        // ESRGAN reads from the previous stage's output (now runs BEFORE RIFE):
        // - If MLT ran: read from IntermediatePath (MLT output)
        // - Otherwise: read from SourceVideoPath (direct video source)
        string inputPath;
        if (!string.IsNullOrEmpty(renderJob.IntermediatePath) && File.Exists(renderJob.IntermediatePath))
        {
            // MLT wrote to IntermediatePath
            inputPath = renderJob.IntermediatePath;
            Debug.WriteLine($"ESRGAN reading from MLT output: {inputPath}");
        }
        else
        {
            // Direct video source (no prior MLT processing)
            inputPath = renderJob.SourceVideoPath;
            Debug.WriteLine($"ESRGAN reading from source video: {inputPath}");
        }

        // Create temporary output path for upscaled result
        var tempOutput = Path.Combine(Path.GetTempPath(), $"esrgan_temp_{Guid.NewGuid().ToString()[..8]}.mp4");

        // Deserialize Real-ESRGAN options
        RealEsrganOptions? esrganOptions = null;
        if (!string.IsNullOrEmpty(renderJob.RealEsrganOptionsJson))
        {
            esrganOptions = JsonSerializer.Deserialize<RealEsrganOptions>(renderJob.RealEsrganOptionsJson);
        }

        // Use recommended settings if not specified
        esrganOptions ??= RealEsrganOptions.GetRecommendedSettings(720);

        Debug.WriteLine($"Real-ESRGAN settings: Model={esrganOptions.ModelName}, Scale={esrganOptions.ScaleFactor}x, Tile={esrganOptions.TileSize}px");
        Debug.WriteLine($"Input: {inputPath}, Temp Output: {tempOutput}");

        // Get FFmpeg path from settings
        var settingsService = scope.ServiceProvider.GetService<SettingsService>();
        string? ffmpegPath = null;
        if (settingsService != null)
        {
            var appSettings = await settingsService.LoadSettingsAsync();
            ffmpegPath = appSettings.FFmpegPath;
        }

        // Fallback to SVP's FFmpeg if available
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            ffmpegPath = @"C:\Program Files (x86)\SVP 4\utils\ffmpeg.exe";
            if (!File.Exists(ffmpegPath))
            {
                ffmpegPath = "ffmpeg";
            }
        }

        // Update stage text (ESRGAN now runs as Stage 2, before RIFE)
        renderJob.CurrentStage = renderJob.IsThreeStageRender
            ? "Stage 2: Real-ESRGAN Upscaling"
            : renderJob.IsTwoStageRender
                ? "Stage 2: Real-ESRGAN Upscaling"
                : "Real-ESRGAN Upscaling";

        // Save stage update to database so UI shows correct stage
        var db = scope.ServiceProvider.GetRequiredService<RenderJobDbContext>();
        db.RenderJobs.Update(renderJob);
        await db.SaveChangesAsync(cancellationToken);
        Debug.WriteLine($"Updated job stage to: {renderJob.CurrentStage}");

        // Create progress reporter
        var lastProgressUpdate = DateTime.UtcNow;
        var progress = new Progress<double>(esrProgress =>
        {
            // Throttle to 100ms (10 fps max) to prevent progress bar glitching
            var now = DateTime.UtcNow;
            if ((now - lastProgressUpdate).TotalMilliseconds < 100)
                return;

            lastProgressUpdate = now;

            FireProgressChanged(jobId, RenderJobStatus.Running, esrProgress, 0, null,
                TimeSpan.Zero, null);
        });

        // Execute upscaling
        var esrganService = scope.ServiceProvider.GetRequiredService<Services.RealESRGAN.RealEsrganService>();

        var success = await esrganService.UpscaleVideoAsync(
            inputPath,
            tempOutput,
            esrganOptions,
            progress,
            cancellationToken,
            ffmpegPath);

        if (success && File.Exists(tempOutput))
        {
            // Determine where to write ESRGAN output:
            // - If RIFE will run next: write to IntermediatePath2 (RIFE will read from here)
            // - Otherwise: write to final OutputPath
            string esrganOutputPath = renderJob.UseRifeInterpolation
                ? (renderJob.IntermediatePath2 ?? Path.Combine(Path.GetDirectoryName(renderJob.OutputPath) ?? Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(renderJob.OutputPath) + "_esrgan.mp4"))
                : renderJob.OutputPath;

            // Move temp output to appropriate location
            if (File.Exists(esrganOutputPath))
            {
                File.Delete(esrganOutputPath);
            }
            File.Move(tempOutput, esrganOutputPath);

            // Update paths for next stage
            if (renderJob.UseRifeInterpolation)
            {
                // RIFE will read from IntermediatePath2
                renderJob.IntermediatePath2 = esrganOutputPath;
                Debug.WriteLine($"ESRGAN output (intermediate for RIFE): {esrganOutputPath}");
            }
            else
            {
                // This is final output
                Debug.WriteLine($"ESRGAN output (final): {esrganOutputPath}");
            }

            Debug.WriteLine($"Real-ESRGAN post-processing completed. Final output: {renderJob.OutputPath}");
        }
        else if (File.Exists(tempOutput))
        {
            // Clean up temp file if processing failed
            File.Delete(tempOutput);
        }

        return success;
    }

    private async Task<bool> ApplyNonAiUpscalingPostProcessingAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Applying Non-AI upscaling post-processing for job {jobId}");

        // Non-AI upscaling reads from the previous stage's output (runs BEFORE RIFE):
        // - If MLT ran: read from IntermediatePath (MLT output)
        // - Otherwise: read from SourceVideoPath (direct video source)
        string inputPath;
        if (!string.IsNullOrEmpty(renderJob.IntermediatePath) && File.Exists(renderJob.IntermediatePath))
        {
            // MLT wrote to IntermediatePath
            inputPath = renderJob.IntermediatePath;
            Debug.WriteLine($"Non-AI upscaling reading from MLT output: {inputPath}");
        }
        else
        {
            // Direct video source (no prior MLT processing)
            inputPath = renderJob.SourceVideoPath;
            Debug.WriteLine($"Non-AI upscaling reading from source video: {inputPath}");
        }

        // Create temporary output path for upscaled result
        var tempOutput = Path.Combine(Path.GetTempPath(), $"nonai_upscale_temp_{Guid.NewGuid().ToString()[..8]}.mp4");

        // Get algorithm and scale factor
        var algorithm = renderJob.NonAiUpscalingAlgorithm ?? "lanczos";
        var scaleFactor = renderJob.NonAiUpscalingScaleFactor;

        Debug.WriteLine($"Non-AI upscaling: Algorithm={algorithm}, Scale={scaleFactor}x");
        Debug.WriteLine($"Input: {inputPath}, Temp Output: {tempOutput}");

        // Update stage text (Non-AI upscaling now runs as Stage 2, before RIFE)
        renderJob.CurrentStage = renderJob.IsThreeStageRender
            ? "Stage 2: Non-AI Upscaling"
            : renderJob.IsTwoStageRender
                ? "Stage 2: Non-AI Upscaling"
                : "Non-AI Upscaling";

        // Save stage update to database so UI shows correct stage
        var db = scope.ServiceProvider.GetRequiredService<RenderJobDbContext>();
        db.RenderJobs.Update(renderJob);
        await db.SaveChangesAsync(cancellationToken);
        Debug.WriteLine($"Updated job stage to: {renderJob.CurrentStage}");

        // Create progress reporter
        var lastProgressUpdate = DateTime.UtcNow;
        var progressReporter = new Progress<double>(upscaleProgress =>
        {
            // Throttle to 100ms (10 fps max) to prevent progress bar glitching
            var now = DateTime.UtcNow;
            if ((now - lastProgressUpdate).TotalMilliseconds < 100)
                return;

            lastProgressUpdate = now;

            FireProgressChanged(jobId, RenderJobStatus.Running, upscaleProgress, 0, null,
                TimeSpan.Zero, null);
        });

        // Execute non-AI upscaling
        var settingsService = scope.ServiceProvider.GetService<SettingsService>();
        var svpDetection = scope.ServiceProvider.GetRequiredService<SvpDetectionService>();
        var upscalingService = new Services.Upscaling.NonAiUpscalingService(svpDetection, settingsService);

        var success = await upscalingService.UpscaleVideoAsync(
            inputPath,
            tempOutput,
            algorithm,
            scaleFactor,
            progressReporter,
            cancellationToken);

        if (success && File.Exists(tempOutput))
        {
            // Determine where to write Non-AI upscaling output:
            // - If RIFE will run next: write to IntermediatePath2 (RIFE will read from here)
            // - Otherwise: write to final OutputPath
            string upscaleOutputPath = renderJob.UseRifeInterpolation
                ? (renderJob.IntermediatePath2 ?? Path.Combine(Path.GetDirectoryName(renderJob.OutputPath) ?? Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(renderJob.OutputPath) + "_upscaled.mp4"))
                : renderJob.OutputPath;

            // Move temp output to appropriate location
            if (File.Exists(upscaleOutputPath))
            {
                File.Delete(upscaleOutputPath);
            }
            File.Move(tempOutput, upscaleOutputPath);

            // Update paths for next stage
            if (renderJob.UseRifeInterpolation)
            {
                // RIFE will read from IntermediatePath2
                renderJob.IntermediatePath2 = upscaleOutputPath;
                Debug.WriteLine($"Non-AI upscaling output (intermediate for RIFE): {upscaleOutputPath}");
            }
            else
            {
                // This is final output
                Debug.WriteLine($"Non-AI upscaling output (final): {upscaleOutputPath}");
            }

            Debug.WriteLine($"Non-AI upscaling post-processing completed. Final output: {renderJob.OutputPath}");
        }
        else if (File.Exists(tempOutput))
        {
            // Clean up temp file if processing failed
            File.Delete(tempOutput);
        }

        return success;
    }

    private async Task<bool> ApplyRealCuganPostProcessingAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Applying Real-CUGAN post-processing for job {jobId}");

        // Real-CUGAN reads from the previous stage's output (runs BEFORE RIFE):
        // - If MLT ran: read from IntermediatePath (MLT output)
        // - Otherwise: read from SourceVideoPath (direct video source)
        string inputPath;
        if (!string.IsNullOrEmpty(renderJob.IntermediatePath) && File.Exists(renderJob.IntermediatePath))
        {
            // MLT wrote to IntermediatePath
            inputPath = renderJob.IntermediatePath;
            Debug.WriteLine($"Real-CUGAN reading from MLT output: {inputPath}");
        }
        else
        {
            // Direct video source (no prior MLT processing)
            inputPath = renderJob.SourceVideoPath;
            Debug.WriteLine($"Real-CUGAN reading from source video: {inputPath}");
        }

        // Create temporary output path for upscaled result
        var tempOutput = Path.Combine(Path.GetTempPath(), $"realcugan_temp_{Guid.NewGuid().ToString()[..8]}.mp4");

        // Deserialize Real-CUGAN options
        RealCuganOptions? cuganOptions = null;
        if (!string.IsNullOrEmpty(renderJob.RealCuganOptionsJson))
        {
            cuganOptions = JsonSerializer.Deserialize<RealCuganOptions>(renderJob.RealCuganOptionsJson);
        }

        // Use recommended settings if not specified
        cuganOptions ??= RealCuganOptions.GetRecommendedSettings(720);

        Debug.WriteLine($"Real-CUGAN settings: Noise={cuganOptions.Noise}, Scale={cuganOptions.Scale}x, Backend={RealCuganOptions.GetBackendDisplayName(cuganOptions.Backend)}, FP16={cuganOptions.UseFp16}");
        Debug.WriteLine($"Input: {inputPath}, Temp Output: {tempOutput}");

        // Get FFmpeg path from settings
        var settingsService = scope.ServiceProvider.GetService<SettingsService>();
        string? ffmpegPath = null;
        if (settingsService != null)
        {
            var appSettings = await settingsService.LoadSettingsAsync();
            ffmpegPath = appSettings.FFmpegPath;
        }

        // Fallback to SVP's FFmpeg if available
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            ffmpegPath = @"C:\Program Files (x86)\SVP 4\utils\ffmpeg.exe";
            if (!File.Exists(ffmpegPath))
            {
                ffmpegPath = "ffmpeg";
            }
        }

        // Update stage text (Real-CUGAN now runs as Stage 2, before RIFE)
        renderJob.CurrentStage = renderJob.IsThreeStageRender
            ? "Stage 2: Real-CUGAN Upscaling"
            : renderJob.IsTwoStageRender
                ? "Stage 2: Real-CUGAN Upscaling"
                : "Real-CUGAN Upscaling";

        // Save stage update to database so UI shows correct stage
        var db = scope.ServiceProvider.GetRequiredService<RenderJobDbContext>();
        db.RenderJobs.Update(renderJob);
        await db.SaveChangesAsync(cancellationToken);
        Debug.WriteLine($"Updated job stage to: {renderJob.CurrentStage}");

        // Create progress reporter
        var lastProgressUpdate = DateTime.UtcNow;
        var progress = new Progress<double>(cuganProgress =>
        {
            // Throttle to 100ms (10 fps max) to prevent progress bar glitching
            var now = DateTime.UtcNow;
            if ((now - lastProgressUpdate).TotalMilliseconds < 100)
                return;

            lastProgressUpdate = now;

            FireProgressChanged(jobId, RenderJobStatus.Running, cuganProgress, 0, null,
                TimeSpan.Zero, null);
        });

        // Execute upscaling
        var cuganService = scope.ServiceProvider.GetRequiredService<Services.RealCUGAN.RealCuganService>();

        var success = await cuganService.UpscaleVideoAsync(
            inputPath,
            tempOutput,
            cuganOptions,
            progress,
            cancellationToken,
            ffmpegPath);

        if (success && File.Exists(tempOutput))
        {
            // Determine where to write Real-CUGAN output:
            // - If RIFE will run next: write to IntermediatePath2 (RIFE will read from here)
            // - Otherwise: write to final OutputPath
            string cuganOutputPath = renderJob.UseRifeInterpolation
                ? (renderJob.IntermediatePath2 ?? Path.Combine(Path.GetDirectoryName(renderJob.OutputPath) ?? Path.GetTempPath(),
                    Path.GetFileNameWithoutExtension(renderJob.OutputPath) + "_cugan.mp4"))
                : renderJob.OutputPath;

            // Move temp output to appropriate location
            if (File.Exists(cuganOutputPath))
            {
                File.Delete(cuganOutputPath);
            }
            File.Move(tempOutput, cuganOutputPath);

            // Update paths for next stage
            if (renderJob.UseRifeInterpolation)
            {
                // RIFE will read from IntermediatePath2
                renderJob.IntermediatePath2 = cuganOutputPath;
                Debug.WriteLine($"Real-CUGAN output (intermediate for RIFE): {cuganOutputPath}");
            }
            else
            {
                // This is final output
                Debug.WriteLine($"Real-CUGAN output (final): {cuganOutputPath}");
            }

            Debug.WriteLine($"Real-CUGAN post-processing completed. Final output: {renderJob.OutputPath}");
        }
        else if (File.Exists(tempOutput))
        {
            // Clean up temp file if processing failed
            File.Delete(tempOutput);
        }

        return success;
    }

    private async Task<bool> ExecuteTwoStageRenderAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Executing two-stage render (MLT → RIFE) for job {jobId}");

        if (string.IsNullOrEmpty(renderJob.IntermediatePath))
        {
            throw new InvalidOperationException("IntermediatePath not set for two-stage render");
        }

        try
        {
            // STAGE 1: Render MLT to temporary file
            Debug.WriteLine($"Stage 1/2: Rendering MLT to temp file: {renderJob.IntermediatePath}");

            // Deserialize combined settings (MeltSettings, FFmpegSettings, RifeSettings)
            // For two-stage renders, all settings are stored together in RenderSettings JSON
            var settingsDoc = JsonSerializer.Deserialize<JsonDocument>(renderJob.RenderSettings);
            if (settingsDoc == null)
            {
                throw new InvalidOperationException("Failed to deserialize render settings for two-stage render");
            }

            // Extract FFmpegSettings from combined JSON
            if (settingsDoc.RootElement.TryGetProperty("FFmpegSettings", out var ffmpegElement))
            {
                renderJob.FFmpegSettings = JsonSerializer.Deserialize<FFmpegRenderSettings>(ffmpegElement.GetRawText());
            }

            // Extract MeltSettings from combined JSON
            if (settingsDoc.RootElement.TryGetProperty("MeltSettings", out var meltElement))
            {
                renderJob.MeltSettings = JsonSerializer.Deserialize<MeltRenderSettings>(meltElement.GetRawText());
            }

            // Extract RifeSettings from combined JSON
            if (settingsDoc.RootElement.TryGetProperty("RifeSettings", out var rifeElement))
            {
                renderJob.RifeSettings = JsonSerializer.Deserialize<RifeSettings>(rifeElement.GetRawText());
            }

            // Check if we should use hardware acceleration for Stage 1
            // If FFmpegSettings exists and has hardware acceleration enabled, use NVENC
            bool useHardwareAcceleration = renderJob.FFmpegSettings?.UseHardwareAcceleration ?? false;

            if (useHardwareAcceleration && renderJob.FFmpegSettings != null)
            {
                Debug.WriteLine("Stage 1: Using FFmpeg with NVENC hardware acceleration for MLT render");

                // Use FFmpeg to render the MLT file with hardware acceleration
                var xmlService = scope.ServiceProvider.GetRequiredService<CheapHelpers.Services.DataExchange.Xml.IXmlService>();
                var shotcutService = scope.ServiceProvider.GetRequiredService<ShotcutService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var appSettings = await settingsService.LoadSettingsAsync();

                var meltService = new MeltRenderService(
                    meltExecutable: appSettings.MeltPath,
                    xmlService: xmlService,
                    shotcutService: shotcutService);

                // Create hardware-accelerated settings for Stage 1 using FFmpegSettings
                var stage1Settings = new MeltRenderSettings
                {
                    ThreadCount = Environment.ProcessorCount,
                    // Use NVENC preset (p-series) from FFmpegSettings, fallback to medium
                    Preset = renderJob.FFmpegSettings.NvencPreset ?? "medium",
                    Crf = renderJob.FFmpegSettings.Quality,
                    // Use NVENC codec from FFmpegSettings
                    VideoCodec = renderJob.FFmpegSettings.VideoCodec switch
                    {
                        "h264_nvenc" => "h264_nvenc",
                        "hevc_nvenc" => "hevc_nvenc",
                        _ => "hevc_nvenc" // Default to HEVC NVENC
                    },
                    AudioCodec = "aac",
                    AudioBitrate = "128k",
                    UseHardwareAcceleration = true // Enable hardware acceleration
                };

                Debug.WriteLine($"Stage 1 using NVENC: codec={stage1Settings.VideoCodec}, preset={stage1Settings.Preset}, quality={stage1Settings.Crf}");

                // Stage 1 progress: 0-50%
                var lastStage1ProgressUpdateNvenc = DateTime.UtcNow;
                var stage1Progress = new Progress<RenderProgress>(rProgress =>
                {
                    // Throttle to 100ms (10 fps max) to prevent progress bar glitching
                    var now = DateTime.UtcNow;
                    if ((now - lastStage1ProgressUpdateNvenc).TotalMilliseconds < 100)
                        return;

                    lastStage1ProgressUpdateNvenc = now;

                    var adjustedPercentage = rProgress.Percentage * 0.5; // Scale to 0-50%
                    FireProgressChanged(jobId, RenderJobStatus.Running, adjustedPercentage, rProgress.CurrentFrame,
                        null, TimeSpan.Zero, null);
                });

                var stage1Success = await meltService.RenderAsync(
                    renderJob.SourceVideoPath,
                    renderJob.IntermediatePath,
                    stage1Settings,
                    stage1Progress,
                    cancellationToken,
                    renderJob.InPoint,
                    renderJob.OutPoint,
                    renderJob.SelectedVideoTracks,
                    renderJob.SelectedAudioTracks);

                if (!stage1Success)
                {
                    Debug.WriteLine("Stage 1 (MLT render with NVENC) failed");
                    return false;
                }
            }
            else
            {
                Debug.WriteLine("Stage 1: Using Melt with CPU encoding for MLT render");

                // MeltSettings already deserialized from combined JSON above
                if (renderJob.MeltSettings == null)
                {
                    throw new InvalidOperationException("MeltSettings not found in two-stage render settings");
                }

                var xmlService = scope.ServiceProvider.GetRequiredService<CheapHelpers.Services.DataExchange.Xml.IXmlService>();
                var shotcutService = scope.ServiceProvider.GetRequiredService<ShotcutService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var appSettings = await settingsService.LoadSettingsAsync();

                var meltService = new MeltRenderService(
                    meltExecutable: appSettings.MeltPath,
                    xmlService: xmlService,
                    shotcutService: shotcutService);

                // Stage 1 progress: 0-50%
                var lastStage1ProgressUpdateCpu = DateTime.UtcNow;
                var stage1Progress = new Progress<RenderProgress>(rProgress =>
                {
                    // Throttle to 100ms (10 fps max) to prevent progress bar glitching
                    var now = DateTime.UtcNow;
                    if ((now - lastStage1ProgressUpdateCpu).TotalMilliseconds < 100)
                        return;

                    lastStage1ProgressUpdateCpu = now;

                    var adjustedPercentage = rProgress.Percentage * 0.5; // Scale to 0-50%
                    FireProgressChanged(jobId, RenderJobStatus.Running, adjustedPercentage, rProgress.CurrentFrame,
                        null, TimeSpan.Zero, null);
                });

                var stage1Success = await meltService.RenderAsync(
                    renderJob.SourceVideoPath,
                    renderJob.IntermediatePath,
                    renderJob.MeltSettings!,
                    stage1Progress,
                    cancellationToken,
                    renderJob.InPoint,
                    renderJob.OutPoint,
                    renderJob.SelectedVideoTracks,
                    renderJob.SelectedAudioTracks);

                if (!stage1Success)
                {
                    Debug.WriteLine("Stage 1 (MLT render) failed");
                    return false;
                }
            }

            Debug.WriteLine($"Stage 1 complete. Temp file: {renderJob.IntermediatePath}");

            // Record intermediate file size
            if (File.Exists(renderJob.IntermediatePath))
            {
                var tempFileInfo = new FileInfo(renderJob.IntermediatePath);
                renderJob.IntermediateFileSizeBytes = tempFileInfo.Length;
                Debug.WriteLine($"Intermediate file size: {renderJob.GetIntermediateFileSizeFormatted()}");
            }

            // STAGE 2: RIFE interpolation on temp file
            Debug.WriteLine($"Stage 2/2: RIFE interpolation");

            // Update stage information
            renderJob.CurrentStage = "Stage 2: RIFE Interpolation";
            var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();
            await repository.UpdateAsync(renderJob);

            var rifePipeline = scope.ServiceProvider.GetRequiredService<RifeVideoProcessingPipeline>();

            // Ensure FFmpeg path is set for RIFE processing
            var ffmpegSettingsForRife = renderJob.FFmpegSettings ?? new FFmpegRenderSettings();
            if (string.IsNullOrEmpty(ffmpegSettingsForRife.FFmpegPath))
            {
                // Try to get FFmpeg path from settings
                var settingsService = scope.ServiceProvider.GetService<SettingsService>();
                if (settingsService != null)
                {
                    var settings = await settingsService.LoadSettingsAsync();
                    ffmpegSettingsForRife.FFmpegPath = settings.FFmpegPath;
                }

                // Fallback to SVP's FFmpeg if available
                if (string.IsNullOrEmpty(ffmpegSettingsForRife.FFmpegPath))
                {
                    ffmpegSettingsForRife.FFmpegPath = @"C:\Program Files (x86)\SVP 4\utils\ffmpeg.exe";
                    if (!File.Exists(ffmpegSettingsForRife.FFmpegPath))
                    {
                        ffmpegSettingsForRife.FFmpegPath = "ffmpeg"; // Hope it's in PATH
                    }
                }
            }

            var pipelineOptions = new RifePipelineOptions
            {
                RifeOptions = new RifeOptions
                {
                    InterpolationPasses = renderJob.RifeSettings?.InterpolationMultiplier switch
                    {
                        2 => 1,
                        4 => 2,
                        8 => 3,
                        _ => 1
                    }
                },
                FFmpegSettings = ffmpegSettingsForRife,
                InputFps = (int)renderJob.FrameRate,
                ValidateFrameCounts = true,
                KeepTemporaryFiles = false,
                UseHardwareDecode = true
            };

            // Stage 2 progress: 50-100%
            var lastRifeProgressUpdate = DateTime.UtcNow;
            var stage2Progress = new Progress<VideoProcessingProgress>(vProgress =>
            {
                // Throttle to 100ms (10 fps max) to prevent progress bar glitching
                var now = DateTime.UtcNow;
                if ((now - lastRifeProgressUpdate).TotalMilliseconds < 100)
                    return;

                lastRifeProgressUpdate = now;

                var adjustedPercentage = 50 + (vProgress.OverallProgress * 0.5); // Scale to 50-100%
                FireProgressChanged(jobId, RenderJobStatus.Running, adjustedPercentage, 0, null,
                    TimeSpan.Zero, null);
            });

            var stage2Success = await rifePipeline.ProcessVideoAsync(
                renderJob.IntermediatePath,
                renderJob.OutputPath,
                pipelineOptions,
                stage2Progress,
                cancellationToken);

            Debug.WriteLine($"Stage 2 complete. Success: {stage2Success}");

            return stage2Success;
        }
        finally
        {
            // Clean up temporary file
            if (!string.IsNullOrEmpty(renderJob.IntermediatePath) && File.Exists(renderJob.IntermediatePath))
            {
                try
                {
                    File.Delete(renderJob.IntermediatePath);
                    Debug.WriteLine($"Cleaned up temp file: {renderJob.IntermediatePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to delete temp file: {ex.Message}");
                }
            }
        }
    }

    private async Task<bool> ExecuteThreeStageRenderAsync(
        RenderJob renderJob,
        CancellationToken cancellationToken,
        IServiceScope scope,
        Guid jobId)
    {
        Debug.WriteLine($"Executing three-stage render (MLT → RIFE → Real-ESRGAN) for job {jobId}");

        if (string.IsNullOrEmpty(renderJob.IntermediatePath))
        {
            throw new InvalidOperationException("IntermediatePath not set for three-stage render");
        }

        if (string.IsNullOrEmpty(renderJob.IntermediatePath2))
        {
            throw new InvalidOperationException("IntermediatePath2 not set for three-stage render");
        }

        try
        {
            // STAGE 1: Render MLT to temporary file
            Debug.WriteLine($"Stage 1/3: Rendering MLT to temp file: {renderJob.IntermediatePath}");

            // Deserialize combined settings
            var settingsDoc = JsonSerializer.Deserialize<JsonDocument>(renderJob.RenderSettings);
            if (settingsDoc == null)
            {
                throw new InvalidOperationException("Failed to deserialize render settings for three-stage render");
            }

            // Extract settings from combined JSON
            if (settingsDoc.RootElement.TryGetProperty("FFmpegSettings", out var ffmpegElement))
            {
                renderJob.FFmpegSettings = JsonSerializer.Deserialize<FFmpegRenderSettings>(ffmpegElement.GetRawText());
            }

            if (settingsDoc.RootElement.TryGetProperty("MeltSettings", out var meltElement))
            {
                renderJob.MeltSettings = JsonSerializer.Deserialize<MeltRenderSettings>(meltElement.GetRawText());
            }

            if (settingsDoc.RootElement.TryGetProperty("RifeSettings", out var rifeElement))
            {
                renderJob.RifeSettings = JsonSerializer.Deserialize<RifeSettings>(rifeElement.GetRawText());
            }

            // Check if we should use hardware acceleration for Stage 1
            bool useHardwareAcceleration = renderJob.FFmpegSettings?.UseHardwareAcceleration ?? false;

            if (useHardwareAcceleration && renderJob.FFmpegSettings != null)
            {
                Debug.WriteLine("Stage 1: Using FFmpeg with NVENC hardware acceleration for MLT render");

                var xmlService = scope.ServiceProvider.GetRequiredService<CheapHelpers.Services.DataExchange.Xml.IXmlService>();
                var shotcutService = scope.ServiceProvider.GetRequiredService<ShotcutService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var appSettings = await settingsService.LoadSettingsAsync();

                var meltService = new MeltRenderService(
                    meltExecutable: appSettings.MeltPath,
                    xmlService: xmlService,
                    shotcutService: shotcutService);

                var stage1Settings = new MeltRenderSettings
                {
                    ThreadCount = Environment.ProcessorCount,
                    Preset = renderJob.FFmpegSettings.NvencPreset ?? "medium",
                    Crf = renderJob.FFmpegSettings.Quality,
                    VideoCodec = renderJob.FFmpegSettings.VideoCodec switch
                    {
                        "h264_nvenc" => "h264_nvenc",
                        "hevc_nvenc" => "hevc_nvenc",
                        _ => "hevc_nvenc"
                    },
                    AudioCodec = "aac",
                    AudioBitrate = "128k",
                    UseHardwareAcceleration = true
                };

                // Stage 1 progress: 0-33%
                var lastStage1ProgressUpdate = DateTime.UtcNow;
                var stage1Progress = new Progress<RenderProgress>(rProgress =>
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastStage1ProgressUpdate).TotalMilliseconds < 100)
                        return;

                    lastStage1ProgressUpdate = now;
                    var adjustedPercentage = rProgress.Percentage * 0.33;
                    FireProgressChanged(jobId, RenderJobStatus.Running, adjustedPercentage, rProgress.CurrentFrame,
                        null, TimeSpan.Zero, null);
                });

                var stage1Success = await meltService.RenderAsync(
                    renderJob.SourceVideoPath,
                    renderJob.IntermediatePath,
                    stage1Settings,
                    stage1Progress,
                    cancellationToken,
                    renderJob.InPoint,
                    renderJob.OutPoint,
                    renderJob.SelectedVideoTracks,
                    renderJob.SelectedAudioTracks);

                if (!stage1Success)
                {
                    Debug.WriteLine("Stage 1 (MLT render) failed");
                    return false;
                }
            }
            else
            {
                Debug.WriteLine("Stage 1: Using Melt with CPU encoding for MLT render");

                if (renderJob.MeltSettings == null)
                {
                    throw new InvalidOperationException("MeltSettings not found in three-stage render settings");
                }

                var xmlService = scope.ServiceProvider.GetRequiredService<CheapHelpers.Services.DataExchange.Xml.IXmlService>();
                var shotcutService = scope.ServiceProvider.GetRequiredService<ShotcutService>();
                var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var appSettings = await settingsService.LoadSettingsAsync();

                var meltService = new MeltRenderService(
                    meltExecutable: appSettings.MeltPath,
                    xmlService: xmlService,
                    shotcutService: shotcutService);

                // Stage 1 progress: 0-33%
                var lastStage1ProgressUpdate = DateTime.UtcNow;
                var stage1Progress = new Progress<RenderProgress>(rProgress =>
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastStage1ProgressUpdate).TotalMilliseconds < 100)
                        return;

                    lastStage1ProgressUpdate = now;
                    var adjustedPercentage = rProgress.Percentage * 0.33;
                    FireProgressChanged(jobId, RenderJobStatus.Running, adjustedPercentage, rProgress.CurrentFrame,
                        null, TimeSpan.Zero, null);
                });

                var stage1Success = await meltService.RenderAsync(
                    renderJob.SourceVideoPath,
                    renderJob.IntermediatePath,
                    renderJob.MeltSettings!,
                    stage1Progress,
                    cancellationToken,
                    renderJob.InPoint,
                    renderJob.OutPoint,
                    renderJob.SelectedVideoTracks,
                    renderJob.SelectedAudioTracks);

                if (!stage1Success)
                {
                    Debug.WriteLine("Stage 1 (MLT render) failed");
                    return false;
                }
            }

            Debug.WriteLine($"Stage 1 complete. Temp file: {renderJob.IntermediatePath}");

            // Record intermediate file size
            if (File.Exists(renderJob.IntermediatePath))
            {
                var tempFileInfo = new FileInfo(renderJob.IntermediatePath);
                renderJob.IntermediateFileSizeBytes = tempFileInfo.Length;
                Debug.WriteLine($"Intermediate file 1 size: {renderJob.GetIntermediateFileSizeFormatted()}");
            }

            // STAGE 2: RIFE interpolation
            Debug.WriteLine($"Stage 2/3: RIFE interpolation");

            renderJob.CurrentStage = "Stage 2: RIFE Interpolation";
            var repository = scope.ServiceProvider.GetRequiredService<IRenderJobRepository>();
            await repository.UpdateAsync(renderJob);

            var rifePipeline = scope.ServiceProvider.GetRequiredService<RifeVideoProcessingPipeline>();

            var ffmpegSettingsForRife = renderJob.FFmpegSettings ?? new FFmpegRenderSettings();
            if (string.IsNullOrEmpty(ffmpegSettingsForRife.FFmpegPath))
            {
                var settingsService = scope.ServiceProvider.GetService<SettingsService>();
                if (settingsService != null)
                {
                    var settings = await settingsService.LoadSettingsAsync();
                    ffmpegSettingsForRife.FFmpegPath = settings.FFmpegPath;
                }

                if (string.IsNullOrEmpty(ffmpegSettingsForRife.FFmpegPath))
                {
                    ffmpegSettingsForRife.FFmpegPath = @"C:\Program Files (x86)\SVP 4\utils\ffmpeg.exe";
                    if (!File.Exists(ffmpegSettingsForRife.FFmpegPath))
                    {
                        ffmpegSettingsForRife.FFmpegPath = "ffmpeg";
                    }
                }
            }

            var pipelineOptions = new RifePipelineOptions
            {
                RifeOptions = new RifeOptions
                {
                    InterpolationPasses = renderJob.RifeSettings?.InterpolationMultiplier switch
                    {
                        2 => 1,
                        4 => 2,
                        8 => 3,
                        _ => 1
                    }
                },
                FFmpegSettings = ffmpegSettingsForRife,
                InputFps = (int)renderJob.FrameRate,
                ValidateFrameCounts = true,
                KeepTemporaryFiles = false,
                UseHardwareDecode = true
            };

            // Stage 2 progress: 33-66%
            var lastRifeProgressUpdate = DateTime.UtcNow;
            var stage2Progress = new Progress<VideoProcessingProgress>(vProgress =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastRifeProgressUpdate).TotalMilliseconds < 100)
                    return;

                lastRifeProgressUpdate = now;
                var adjustedPercentage = 33 + (vProgress.OverallProgress * 0.33);
                FireProgressChanged(jobId, RenderJobStatus.Running, adjustedPercentage, 0, null,
                    TimeSpan.Zero, null);
            });

            var stage2Success = await rifePipeline.ProcessVideoAsync(
                renderJob.IntermediatePath,
                renderJob.IntermediatePath2,
                pipelineOptions,
                stage2Progress,
                cancellationToken);

            if (!stage2Success)
            {
                Debug.WriteLine("Stage 2 (RIFE interpolation) failed");
                return false;
            }

            Debug.WriteLine($"Stage 2 complete. Temp file 2: {renderJob.IntermediatePath2}");

            // Record second intermediate file size
            if (File.Exists(renderJob.IntermediatePath2))
            {
                var tempFileInfo2 = new FileInfo(renderJob.IntermediatePath2);
                renderJob.IntermediateFileSizeBytes2 = tempFileInfo2.Length;
                Debug.WriteLine($"Intermediate file 2 size: {FormatFileSize(renderJob.IntermediateFileSizeBytes2.Value)}");
            }

            // STAGE 3: Real-ESRGAN upscaling
            Debug.WriteLine($"Stage 3/3: Real-ESRGAN upscaling");

            renderJob.CurrentStage = "Stage 3: Real-ESRGAN Upscaling";
            await repository.UpdateAsync(renderJob);

            // Parse Real-ESRGAN options
            RealEsrganOptions? esrganOptions = null;
            if (!string.IsNullOrEmpty(renderJob.RealEsrganOptionsJson))
            {
                esrganOptions = JsonSerializer.Deserialize<RealEsrganOptions>(renderJob.RealEsrganOptionsJson);
            }

            esrganOptions ??= RealEsrganOptions.GetRecommendedSettings(720);

            var esrganService = scope.ServiceProvider.GetRequiredService<Services.RealESRGAN.RealEsrganService>();

            // Stage 3 progress: 66-100%
            var lastEsrganProgressUpdate = DateTime.UtcNow;
            var stage3Progress = new Progress<double>(esrProgress =>
            {
                var now = DateTime.UtcNow;
                if ((now - lastEsrganProgressUpdate).TotalMilliseconds < 100)
                    return;

                lastEsrganProgressUpdate = now;
                var adjustedPercentage = 66 + (esrProgress * 0.34);
                FireProgressChanged(jobId, RenderJobStatus.Running, adjustedPercentage, 0, null,
                    TimeSpan.Zero, null);
            });

            var stage3Success = await esrganService.UpscaleVideoAsync(
                renderJob.IntermediatePath2,
                renderJob.OutputPath,
                esrganOptions,
                stage3Progress,
                cancellationToken,
                ffmpegSettingsForRife.FFmpegPath);

            Debug.WriteLine($"Stage 3 complete. Success: {stage3Success}");

            return stage3Success;
        }
        finally
        {
            // Clean up temporary files
            if (!string.IsNullOrEmpty(renderJob.IntermediatePath) && File.Exists(renderJob.IntermediatePath))
            {
                try
                {
                    File.Delete(renderJob.IntermediatePath);
                    Debug.WriteLine($"Cleaned up temp file 1: {renderJob.IntermediatePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to delete temp file 1: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(renderJob.IntermediatePath2) && File.Exists(renderJob.IntermediatePath2))
            {
                try
                {
                    File.Delete(renderJob.IntermediatePath2);
                    Debug.WriteLine($"Cleaned up temp file 2: {renderJob.IntermediatePath2}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to delete temp file 2: {ex.Message}");
                }
            }
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }

    private IProgress<RenderProgress> CreateRenderProgressReporter(Guid jobId)
    {
        var startTime = DateTime.UtcNow;
        var lastProgressUpdate = DateTime.UtcNow;
        var lastEventFired = DateTime.UtcNow; // Track UI event throttling

        return new Progress<RenderProgress>(renderProgress =>
        {
            var now = DateTime.UtcNow;

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

            var elapsed = now - startTime;
            TimeSpan? remaining = null;
            if (renderProgress.Percentage > 0)
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

                // Re-enqueue if still pending
                if (crashedJob.Status == RenderJobStatus.Pending)
                {
                    await _taskQueue.QueueBackgroundWorkItemAsync(async ct =>
                    {
                        await ProcessJobAsync(crashedJob.JobId, ct);
                    });
                }

                Debug.WriteLine($"Recovered crashed job {crashedJob.JobId}, status: {crashedJob.Status}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during crash recovery: {ex.Message}");
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

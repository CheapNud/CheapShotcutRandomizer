using CheapShotcutRandomizer.Data.Repositories;
using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Services.Queue;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CheapShotcutRandomizer.Tests.Services;

/// <summary>
/// Unit tests for RenderQueueService
/// Tests cover job queueing, status management, queue control, and crash recovery
/// </summary>
public class RenderQueueServiceTests : IDisposable
{
    private readonly Mock<IRenderJobRepository> _mockRepository;
    private readonly Mock<IBackgroundTaskQueue> _mockTaskQueue;
    private readonly ServiceProvider _serviceProvider;
    private readonly RenderQueueService _queueService;

    public RenderQueueServiceTests()
    {
        _mockRepository = new Mock<IRenderJobRepository>();
        _mockTaskQueue = new Mock<IBackgroundTaskQueue>();

        // Setup service provider for dependency injection
        var services = new ServiceCollection();
        services.AddSingleton(_mockRepository.Object);
        services.AddSingleton(_mockTaskQueue.Object);
        _serviceProvider = services.BuildServiceProvider();

        // Setup mock defaults
        _mockRepository.Setup(x => x.GetActiveJobsAsync())
            .ReturnsAsync(new List<RenderJob>());
        _mockRepository.Setup(x => x.GetByStatusAsync(It.IsAny<RenderJobStatus>()))
            .ReturnsAsync(new List<RenderJob>());
        _mockRepository.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new List<RenderJob>());
        _mockTaskQueue.Setup(x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<CancellationToken, ValueTask>>()))
            .Returns(ValueTask.CompletedTask);

        _queueService = new RenderQueueService(_serviceProvider, _mockTaskQueue.Object, maxConcurrentRenders: 2);
    }

    [Fact]
    public void RenderQueueService_Initializes_With_Queue_Paused()
    {
        // Assert
        _queueService.IsQueuePaused.Should().BeTrue();
    }

    [Fact]
    public async Task AddJobAsync_Adds_Job_To_Repository()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "test.mlt",
            OutputPath = "output.mp4"
        };

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<RenderJob>()))
            .Returns(Task.CompletedTask);

        // Act
        var jobId = await _queueService.AddJobAsync(renderJob);

        // Assert
        jobId.Should().Be(renderJob.JobId);
        _mockRepository.Verify(x => x.AddAsync(It.Is<RenderJob>(j => j.JobId == renderJob.JobId)), Times.Once);
    }

    [Fact]
    public async Task AddJobAsync_Queues_Background_Work_Item()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "test.mlt",
            OutputPath = "output.mp4"
        };

        // Act
        await _queueService.AddJobAsync(renderJob);

        // Assert
        _mockTaskQueue.Verify(
            x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<CancellationToken, ValueTask>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetActiveJobsAsync_Returns_Active_Jobs()
    {
        // Arrange
        var activeJobs = new List<RenderJob>
        {
            new() { JobId = Guid.NewGuid(), Status = RenderJobStatus.Running },
            new() { JobId = Guid.NewGuid(), Status = RenderJobStatus.Pending }
        };
        _mockRepository.Setup(x => x.GetActiveJobsAsync()).ReturnsAsync(activeJobs);

        // Act
        var retrievedJobs = await _queueService.GetActiveJobsAsync();

        // Assert
        retrievedJobs.Should().HaveCount(2);
        retrievedJobs.Should().BeEquivalentTo(activeJobs);
    }

    [Fact]
    public async Task GetCompletedJobsAsync_Returns_Completed_Jobs()
    {
        // Arrange
        var completedJobs = new List<RenderJob>
        {
            new() { JobId = Guid.NewGuid(), Status = RenderJobStatus.Completed }
        };
        _mockRepository.Setup(x => x.GetByStatusAsync(RenderJobStatus.Completed))
            .ReturnsAsync(completedJobs);

        // Act
        var retrievedJobs = await _queueService.GetCompletedJobsAsync();

        // Assert
        retrievedJobs.Should().HaveCount(1);
        retrievedJobs[0].Status.Should().Be(RenderJobStatus.Completed);
    }

    [Fact]
    public async Task GetFailedJobsAsync_Returns_Failed_And_DeadLetter_Jobs()
    {
        // Arrange
        var failedJobs = new List<RenderJob>
        {
            new() { JobId = Guid.NewGuid(), Status = RenderJobStatus.Failed }
        };
        var deadLetterJobs = new List<RenderJob>
        {
            new() { JobId = Guid.NewGuid(), Status = RenderJobStatus.DeadLetter }
        };

        _mockRepository.Setup(x => x.GetByStatusAsync(RenderJobStatus.Failed))
            .ReturnsAsync(failedJobs);
        _mockRepository.Setup(x => x.GetByStatusAsync(RenderJobStatus.DeadLetter))
            .ReturnsAsync(deadLetterJobs);

        // Act
        var retrievedJobs = await _queueService.GetFailedJobsAsync();

        // Assert
        retrievedJobs.Should().HaveCount(2);
        retrievedJobs.Should().Contain(j => j.Status == RenderJobStatus.Failed);
        retrievedJobs.Should().Contain(j => j.Status == RenderJobStatus.DeadLetter);
    }

    [Fact]
    public async Task CancelJobAsync_Sets_Job_Status_To_Cancelled()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var renderJob = new RenderJob
        {
            JobId = jobId,
            Status = RenderJobStatus.Running
        };

        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync(renderJob);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<RenderJob>())).Returns(Task.CompletedTask);

        // Act
        var success = await _queueService.CancelJobAsync(jobId);

        // Assert
        success.Should().BeTrue();
        renderJob.Status.Should().Be(RenderJobStatus.Cancelled);
        renderJob.CompletedAt.Should().NotBeNull();
        _mockRepository.Verify(x => x.UpdateAsync(It.Is<RenderJob>(j => j.Status == RenderJobStatus.Cancelled)), Times.Once);
    }

    [Fact]
    public async Task CancelJobAsync_Returns_False_For_Nonexistent_Job()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync((RenderJob?)null);

        // Act
        var success = await _queueService.CancelJobAsync(jobId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public async Task PauseJobAsync_Sets_Job_Status_To_Paused()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var renderJob = new RenderJob
        {
            JobId = jobId,
            Status = RenderJobStatus.Running
        };

        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync(renderJob);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<RenderJob>())).Returns(Task.CompletedTask);

        // Act
        var success = await _queueService.PauseJobAsync(jobId);

        // Assert
        success.Should().BeTrue();
        renderJob.Status.Should().Be(RenderJobStatus.Paused);
        _mockRepository.Verify(x => x.UpdateAsync(It.Is<RenderJob>(j => j.Status == RenderJobStatus.Paused)), Times.Once);
    }

    [Fact]
    public async Task PauseJobAsync_Returns_False_For_Non_Running_Job()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var renderJob = new RenderJob
        {
            JobId = jobId,
            Status = RenderJobStatus.Completed
        };

        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync(renderJob);

        // Act
        var success = await _queueService.PauseJobAsync(jobId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeJobAsync_Sets_Job_Status_To_Pending_And_Requeues()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var renderJob = new RenderJob
        {
            JobId = jobId,
            Status = RenderJobStatus.Paused,
            ProgressPercentage = 50.0
        };

        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync(renderJob);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<RenderJob>())).Returns(Task.CompletedTask);

        // Act
        var success = await _queueService.ResumeJobAsync(jobId);

        // Assert
        success.Should().BeTrue();
        renderJob.Status.Should().Be(RenderJobStatus.Pending);
        _mockRepository.Verify(x => x.UpdateAsync(It.Is<RenderJob>(j => j.Status == RenderJobStatus.Pending)), Times.Once);
        _mockTaskQueue.Verify(x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<CancellationToken, ValueTask>>()), Times.Once);
    }

    [Fact]
    public async Task RetryJobAsync_Resets_Job_And_Requeues()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var renderJob = new RenderJob
        {
            JobId = jobId,
            Status = RenderJobStatus.Failed,
            RetryCount = 2,
            ProgressPercentage = 45.0,
            LastError = "Some error"
        };

        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync(renderJob);
        _mockRepository.Setup(x => x.UpdateAsync(It.IsAny<RenderJob>())).Returns(Task.CompletedTask);

        // Act
        var success = await _queueService.RetryJobAsync(jobId);

        // Assert
        success.Should().BeTrue();
        renderJob.Status.Should().Be(RenderJobStatus.Pending);
        renderJob.RetryCount.Should().Be(0);
        renderJob.ProgressPercentage.Should().Be(0);
        renderJob.LastError.Should().BeNull();
        _mockTaskQueue.Verify(x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<CancellationToken, ValueTask>>()), Times.Once);
    }

    [Fact]
    public async Task RetryJobAsync_Returns_False_For_Non_Failed_Job()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var renderJob = new RenderJob
        {
            JobId = jobId,
            Status = RenderJobStatus.Running
        };

        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync(renderJob);

        // Act
        var success = await _queueService.RetryJobAsync(jobId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public void StartQueue_Changes_IsQueuePaused_To_False()
    {
        // Arrange
        _queueService.IsQueuePaused.Should().BeTrue();

        // Act
        _queueService.StartQueue();

        // Assert
        _queueService.IsQueuePaused.Should().BeFalse();
    }

    [Fact]
    public void StopQueue_Changes_IsQueuePaused_To_True()
    {
        // Arrange
        _queueService.StartQueue();
        _queueService.IsQueuePaused.Should().BeFalse();

        // Act
        _queueService.StopQueue();

        // Assert
        _queueService.IsQueuePaused.Should().BeTrue();
    }

    [Fact]
    public void StartQueue_Fires_QueueStatusChanged_Event()
    {
        // Arrange
        bool? eventFiredWithValue = null;
        _queueService.QueueStatusChanged += (sender, isPaused) => eventFiredWithValue = isPaused;

        // Act
        _queueService.StartQueue();

        // Assert
        eventFiredWithValue.Should().NotBeNull();
        eventFiredWithValue.Should().BeFalse(); // false = queue is running (not paused)
    }

    [Fact]
    public void StopQueue_Fires_QueueStatusChanged_Event()
    {
        // Arrange
        _queueService.StartQueue(); // Start first
        bool? eventFiredWithValue = null;
        _queueService.QueueStatusChanged += (sender, isPaused) => eventFiredWithValue = isPaused;

        // Act
        _queueService.StopQueue();

        // Assert
        eventFiredWithValue.Should().NotBeNull();
        eventFiredWithValue.Should().BeTrue(); // true = queue is paused
    }

    [Fact]
    public async Task GetQueueStatisticsAsync_Returns_Correct_Statistics()
    {
        // Arrange
        var allJobs = new List<RenderJob>
        {
            new() { Status = RenderJobStatus.Pending },
            new() { Status = RenderJobStatus.Pending },
            new() { Status = RenderJobStatus.Running },
            new() { Status = RenderJobStatus.Completed },
            new() { Status = RenderJobStatus.Completed },
            new() { Status = RenderJobStatus.Completed },
            new() { Status = RenderJobStatus.Failed },
            new() { Status = RenderJobStatus.DeadLetter }
        };

        _mockRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(allJobs);

        // Act
        var stats = await _queueService.GetQueueStatisticsAsync();

        // Assert
        stats.PendingCount.Should().Be(2);
        stats.CompletedCount.Should().Be(3);
        stats.FailedCount.Should().Be(2); // Failed + DeadLetter
        stats.TotalCount.Should().Be(8);
        stats.IsQueuePaused.Should().BeTrue();
    }

    [Fact]
    public async Task GetJobAsync_Returns_Specific_Job()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var renderJob = new RenderJob
        {
            JobId = jobId,
            SourceVideoPath = "test.mlt"
        };

        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync(renderJob);

        // Act
        var retrievedJob = await _queueService.GetJobAsync(jobId);

        // Assert
        retrievedJob.Should().NotBeNull();
        retrievedJob!.JobId.Should().Be(jobId);
        retrievedJob.SourceVideoPath.Should().Be("test.mlt");
    }

    [Fact]
    public async Task GetJobAsync_Returns_Null_For_Nonexistent_Job()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        _mockRepository.Setup(x => x.GetAsync(jobId)).ReturnsAsync((RenderJob?)null);

        // Act
        var retrievedJob = await _queueService.GetJobAsync(jobId);

        // Assert
        retrievedJob.Should().BeNull();
    }

    [Fact]
    public async Task GetAllJobsAsync_Returns_All_Jobs()
    {
        // Arrange
        var allJobs = new List<RenderJob>
        {
            new() { JobId = Guid.NewGuid() },
            new() { JobId = Guid.NewGuid() },
            new() { JobId = Guid.NewGuid() }
        };

        _mockRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(allJobs);

        // Act
        var retrievedJobs = await _queueService.GetAllJobsAsync();

        // Assert
        retrievedJobs.Should().HaveCount(3);
        retrievedJobs.Should().BeEquivalentTo(allJobs);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}

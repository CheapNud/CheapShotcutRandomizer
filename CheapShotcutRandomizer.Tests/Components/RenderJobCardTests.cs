using Bunit;
using CheapShotcutRandomizer.Components.Shared;
using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace CheapShotcutRandomizer.Tests.Components;

/// <summary>
/// Unit tests for RenderJobCard.razor component
/// Tests cover job status display, progress tracking, file size formatting, and action buttons
/// </summary>
public class RenderJobCardTests : BunitContext, IAsyncLifetime
{
    public RenderJobCardTests()
    {
        Services.AddMudServices();

        // Setup JSInterop for MudBlazor components
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public new ValueTask DisposeAsync() => base.DisposeAsync();

    [Fact]
    public void RenderJobCard_Renders_Job_Information()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = @"C:\Videos\test_video.mlt",
            OutputPath = @"C:\Output\rendered.mp4",
            Status = RenderJobStatus.Pending
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        component.Should().NotBeNull();
        component.Markup.Should().Contain("test_video.mlt");
        component.Markup.Should().Contain(@"C:\Output\rendered.mp4");
    }

    [Fact]
    public void RenderJobCard_Shows_Pending_Status()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "pending.mlt",
            Status = RenderJobStatus.Pending
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        component.Markup.Should().Contain("Pending");
        component.Markup.Should().Contain("Queued - Waiting to start");
    }

    [Fact]
    public void RenderJobCard_Shows_Running_Status_With_Progress()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "running.mlt",
            Status = RenderJobStatus.Running,
            ProgressPercentage = 65.5,
            CurrentFrame = 3275,
            TotalFrames = 5000,
            EstimatedTimeRemaining = TimeSpan.FromMinutes(5)
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        component.Markup.Should().Contain("Running");
        component.Markup.Should().Contain("65.5%");
    }

    [Fact]
    public void RenderJobCard_Shows_Completed_Status_With_File_Size()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "completed.mlt",
            Status = RenderJobStatus.Completed,
            ProgressPercentage = 100,
            OutputFileSizeBytes = 1024L * 1024L * 1024L * 2 // 2 GB
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        component.Markup.Should().Contain("Completed");
        component.Markup.Should().Contain("2.00 GB");
    }

    [Fact]
    public void RenderJobCard_Shows_Failed_Status_With_Error_Button()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "failed.mlt",
            Status = RenderJobStatus.Failed,
            LastError = "FFmpeg error: codec not found"
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        component.Markup.Should().Contain("Failed");
        component.Markup.Should().Contain("View Error");
    }

    [Fact]
    public void RenderJobCard_Shows_Paused_Status()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "paused.mlt",
            Status = RenderJobStatus.Paused,
            ProgressPercentage = 35.0
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        component.Markup.Should().Contain("Paused");
        component.Markup.Should().Contain("Resume");
    }

    // Multi-stage render tests removed - AI features moved to CheapUpscaler

    [Fact]
    public void RenderJobCard_Pause_Button_Fires_OnPause_Event()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "running.mlt",
            Status = RenderJobStatus.Running
        };

        Guid? pausedJobId = null;
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob)
            .Add(p => p.OnPause, EventCallback.Factory.Create<Guid>(this, id => pausedJobId = id)));

        // Act
        var pauseButton = component.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Pause"));
        pauseButton.Should().NotBeNull();
        pauseButton!.Click();

        // Assert
        pausedJobId.Should().Be(renderJob.JobId);
    }

    [Fact]
    public void RenderJobCard_Cancel_Button_Fires_OnCancel_Event()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "running.mlt",
            Status = RenderJobStatus.Running
        };

        Guid? cancelledJobId = null;
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob)
            .Add(p => p.OnCancel, EventCallback.Factory.Create<Guid>(this, id => cancelledJobId = id)));

        // Act
        var cancelButton = component.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Cancel"));
        cancelButton.Should().NotBeNull();
        cancelButton!.Click();

        // Assert
        cancelledJobId.Should().Be(renderJob.JobId);
    }

    [Fact]
    public void RenderJobCard_Resume_Button_Fires_OnResume_Event()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "paused.mlt",
            Status = RenderJobStatus.Paused
        };

        Guid? resumedJobId = null;
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob)
            .Add(p => p.OnResume, EventCallback.Factory.Create<Guid>(this, id => resumedJobId = id)));

        // Act
        var resumeButton = component.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Resume"));
        resumeButton.Should().NotBeNull();
        resumeButton!.Click();

        // Assert
        resumedJobId.Should().Be(renderJob.JobId);
    }

    [Fact]
    public void RenderJobCard_Retry_Button_Fires_OnRetry_Event()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "failed.mlt",
            Status = RenderJobStatus.Failed
        };

        Guid? retriedJobId = null;
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob)
            .Add(p => p.OnRetry, EventCallback.Factory.Create<Guid>(this, id => retriedJobId = id)));

        // Act
        var retryButton = component.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Retry"));
        retryButton.Should().NotBeNull();
        retryButton!.Click();

        // Assert
        retriedJobId.Should().Be(renderJob.JobId);
    }

    [Fact]
    public void RenderJobCard_Delete_Button_Fires_OnDelete_Event()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "completed.mlt",
            Status = RenderJobStatus.Completed
        };

        Guid? deletedJobId = null;
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob)
            .Add(p => p.OnDelete, EventCallback.Factory.Create<Guid>(this, id => deletedJobId = id)));

        // Act
        var deleteButton = component.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Delete"));
        deleteButton.Should().NotBeNull();
        deleteButton!.Click();

        // Assert
        deletedJobId.Should().Be(renderJob.JobId);
    }

    [Fact]
    public void RenderJobCard_Formats_Timecode_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "running.mlt",
            Status = RenderJobStatus.Running,
            ProgressPercentage = 50.0,
            CurrentFrame = 1500,
            TotalFrames = 3000,
            FrameRate = 30.0
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        // At 30 fps, 1500 frames = 50 seconds = 00:00:50
        // 3000 frames = 100 seconds = 00:01:40
        component.Markup.Should().Contain("00:00:50");
        component.Markup.Should().Contain("00:01:40");
    }

    [Fact]
    public void RenderJobCard_Shows_Error_Details_When_View_Error_Clicked()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "failed.mlt",
            Status = RenderJobStatus.Failed,
            LastError = "Critical rendering error occurred"
        };

        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Act
        var viewErrorButton = component.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("View Error"));
        viewErrorButton.Should().NotBeNull();
        viewErrorButton!.Click();

        // Assert
        component.Markup.Should().Contain("Critical rendering error occurred");
    }

    [Fact]
    public void RenderJobCard_Disposes_Timer_On_Cleanup()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "running.mlt",
            Status = RenderJobStatus.Running,
            OutputPath = Path.GetTempFileName()
        };

        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Act
        component.Dispose();

        // Assert - component should dispose cleanly without errors
        component.Should().NotBeNull();
    }

    [Fact]
    public void RenderJobCard_Handles_Null_EstimatedTimeRemaining()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            JobId = Guid.NewGuid(),
            SourceVideoPath = "running.mlt",
            Status = RenderJobStatus.Running,
            ProgressPercentage = 10.0,
            EstimatedTimeRemaining = null
        };

        // Act
        var component = Render<RenderJobCard>(parameters => parameters
            .Add(p => p.Job, renderJob));

        // Assert
        component.Markup.Should().Contain("Calculating...");
    }
}

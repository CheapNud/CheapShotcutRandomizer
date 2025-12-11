using CheapShotcutRandomizer.Core.Models;
using FluentAssertions;

namespace CheapShotcutRandomizer.Tests.Models;

/// <summary>
/// Unit tests for RenderJob model
/// Tests cover property initialization, file size formatting, timecode conversion, and helper methods
/// </summary>
public class RenderJobTests
{
    [Fact]
    public void RenderJob_Initializes_With_Default_Values()
    {
        // Arrange & Act
        var renderJob = new RenderJob();

        // Assert
        renderJob.JobId.Should().NotBe(Guid.Empty);
        renderJob.Status.Should().Be(RenderJobStatus.Pending);
        renderJob.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        renderJob.LastUpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        renderJob.ProgressPercentage.Should().Be(0);
        renderJob.CurrentFrame.Should().Be(0);
        renderJob.RetryCount.Should().Be(0);
        renderJob.MaxRetries.Should().Be(3);
        renderJob.FrameRate.Should().Be(30.0);
        renderJob.IsTwoStageRender.Should().BeFalse();
    }

    [Fact]
    public void RenderJob_JobId_Is_Unique()
    {
        // Arrange & Act
        var job1 = new RenderJob();
        var job2 = new RenderJob();

        // Assert
        job1.JobId.Should().NotBe(job2.JobId);
    }

    [Fact]
    public void GetOutputFileSizeFormatted_Returns_NA_When_Null()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            OutputFileSizeBytes = null
        };

        // Act
        var formatted = renderJob.GetOutputFileSizeFormatted();

        // Assert
        formatted.Should().Be("N/A");
    }

    [Fact]
    public void GetOutputFileSizeFormatted_Formats_Bytes_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            OutputFileSizeBytes = 512
        };

        // Act
        var formatted = renderJob.GetOutputFileSizeFormatted();

        // Assert
        formatted.Should().Be("512.00 B");
    }

    [Fact]
    public void GetOutputFileSizeFormatted_Formats_Kilobytes_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            OutputFileSizeBytes = 1024 * 150 // 150 KB
        };

        // Act
        var formatted = renderJob.GetOutputFileSizeFormatted();

        // Assert
        formatted.Should().Be("150.00 KB");
    }

    [Fact]
    public void GetOutputFileSizeFormatted_Formats_Megabytes_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            OutputFileSizeBytes = 1024L * 1024L * 256 // 256 MB
        };

        // Act
        var formatted = renderJob.GetOutputFileSizeFormatted();

        // Assert
        formatted.Should().Be("256.00 MB");
    }

    [Fact]
    public void GetOutputFileSizeFormatted_Formats_Gigabytes_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            OutputFileSizeBytes = 1024L * 1024L * 1024L * 3 // 3 GB
        };

        // Act
        var formatted = renderJob.GetOutputFileSizeFormatted();

        // Assert
        formatted.Should().Be("3.00 GB");
    }

    [Fact]
    public void GetOutputFileSizeFormatted_Formats_Terabytes_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            OutputFileSizeBytes = 1024L * 1024L * 1024L * 1024L * 2 // 2 TB
        };

        // Act
        var formatted = renderJob.GetOutputFileSizeFormatted();

        // Assert
        formatted.Should().Be("2.00 TB");
    }

    [Fact]
    public void GetIntermediateFileSizeFormatted_Returns_NA_When_Null()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            IntermediateFileSizeBytes = null
        };

        // Act
        var formatted = renderJob.GetIntermediateFileSizeFormatted();

        // Assert
        formatted.Should().Be("N/A");
    }

    [Fact]
    public void GetIntermediateFileSizeFormatted_Formats_Size_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            IntermediateFileSizeBytes = 1024L * 1024L * 500 // 500 MB
        };

        // Act
        var formatted = renderJob.GetIntermediateFileSizeFormatted();

        // Assert
        formatted.Should().Be("500.00 MB");
    }

    [Fact]
    public void FramesToTimecode_Converts_30fps_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            FrameRate = 30.0
        };

        // Act
        var timecode = renderJob.FramesToTimecode(1800); // 60 seconds at 30 fps

        // Assert
        timecode.Should().Be("00:01:00.000");
    }

    [Fact]
    public void FramesToTimecode_Converts_60fps_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            FrameRate = 60.0
        };

        // Act
        var timecode = renderJob.FramesToTimecode(3600); // 60 seconds at 60 fps

        // Assert
        timecode.Should().Be("00:01:00.000");
    }

    [Fact]
    public void FramesToTimecode_Handles_Hours_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            FrameRate = 30.0
        };

        // Act
        var timecode = renderJob.FramesToTimecode(108000); // 1 hour at 30 fps (3600 seconds * 30)

        // Assert
        timecode.Should().Be("01:00:00.000");
    }

    [Fact]
    public void FramesToTimecode_Handles_Milliseconds_Correctly()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            FrameRate = 30.0
        };

        // Act
        var timecode = renderJob.FramesToTimecode(15); // 0.5 seconds at 30 fps

        // Assert
        timecode.Should().Contain("00:00:00."); // Should have milliseconds
    }

    [Fact]
    public void FramesToTimecode_Handles_Zero_Frames()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            FrameRate = 30.0
        };

        // Act
        var timecode = renderJob.FramesToTimecode(0);

        // Assert
        timecode.Should().Be("00:00:00.000");
    }

    [Fact]
    public void RenderJob_Supports_Two_Stage_Rendering()
    {
        // Arrange & Act
        var renderJob = new RenderJob
        {
            IsTwoStageRender = true,
            IntermediatePath = @"C:\Temp\intermediate.mp4",
            CurrentStage = "Stage 1: MLT Render"
        };

        // Assert
        renderJob.IsTwoStageRender.Should().BeTrue();
        renderJob.IntermediatePath.Should().Be(@"C:\Temp\intermediate.mp4");
        renderJob.CurrentStage.Should().Be("Stage 1: MLT Render");
    }

    [Fact]
    public void RenderJob_Tracks_Retry_Information()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            RetryCount = 2,
            MaxRetries = 3,
            LastError = "FFmpeg process failed",
            ErrorStackTrace = "Stack trace here"
        };

        // Act & Assert
        renderJob.RetryCount.Should().Be(2);
        renderJob.MaxRetries.Should().Be(3);
        renderJob.LastError.Should().Be("FFmpeg process failed");
        renderJob.ErrorStackTrace.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RenderJob_Tracks_Process_Information_For_Crash_Recovery()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            ProcessId = 12345,
            MachineName = "DESKTOP-TEST"
        };

        // Act & Assert
        renderJob.ProcessId.Should().Be(12345);
        renderJob.MachineName.Should().Be("DESKTOP-TEST");
    }

    [Fact]
    public void RenderJob_Supports_Track_Selection()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            SelectedVideoTracks = "0,1,2",
            SelectedAudioTracks = "0,1"
        };

        // Act & Assert
        renderJob.SelectedVideoTracks.Should().Be("0,1,2");
        renderJob.SelectedAudioTracks.Should().Be("0,1");
    }

    [Fact]
    public void RenderJob_Supports_InOut_Points()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            InPoint = 150,
            OutPoint = 3000,
            FrameRate = 30.0
        };

        // Act
        var inTimecode = renderJob.FramesToTimecode(renderJob.InPoint.Value);
        var outTimecode = renderJob.FramesToTimecode(renderJob.OutPoint.Value);

        // Assert
        renderJob.InPoint.Should().Be(150);
        renderJob.OutPoint.Should().Be(3000);
        inTimecode.Should().Be("00:00:05.000"); // 150 frames at 30fps = 5 seconds
        outTimecode.Should().Be("00:01:40.000"); // 3000 frames at 30fps = 100 seconds
    }

    [Fact]
    public void RenderJob_Tracks_Timestamps()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var renderJob = new RenderJob
        {
            CreatedAt = now,
            QueuedAt = now.AddSeconds(1),
            StartedAt = now.AddSeconds(2),
            CompletedAt = now.AddSeconds(62),
            LastUpdatedAt = now.AddSeconds(30)
        };

        // Act & Assert
        renderJob.CreatedAt.Should().Be(now);
        renderJob.QueuedAt.Should().Be(now.AddSeconds(1));
        renderJob.StartedAt.Should().Be(now.AddSeconds(2));
        renderJob.CompletedAt.Should().Be(now.AddSeconds(62));
        renderJob.LastUpdatedAt.Should().Be(now.AddSeconds(30));
    }

    [Fact]
    public void RenderJob_Supports_Progress_Tracking()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            ProgressPercentage = 67.5,
            CurrentFrame = 2025,
            TotalFrames = 3000,
            EstimatedTimeRemaining = TimeSpan.FromMinutes(5)
        };

        // Act & Assert
        renderJob.ProgressPercentage.Should().Be(67.5);
        renderJob.CurrentFrame.Should().Be(2025);
        renderJob.TotalFrames.Should().Be(3000);
        renderJob.EstimatedTimeRemaining.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void RenderJob_Supports_Different_Source_Types()
    {
        // Arrange
        var mltJob = new RenderJob { RenderType = RenderType.MltSource };
        var videoJob = new RenderJob { RenderType = RenderType.VideoSource };

        // Act & Assert
        mltJob.RenderType.Should().Be(RenderType.MltSource);
        videoJob.RenderType.Should().Be(RenderType.VideoSource);
    }

    [Fact]
    public void RenderJob_Stores_Render_Settings()
    {
        // Arrange
        var renderJob = new RenderJob
        {
            RenderSettings = @"{""ThreadCount"":8,""Preset"":""medium"",""Crf"":23}"
        };

        // Act & Assert
        renderJob.RenderSettings.Should().NotBeNullOrEmpty();
        renderJob.RenderSettings.Should().Contain("ThreadCount");
        renderJob.RenderSettings.Should().Contain("Preset");
    }

    [Fact]
    public void RenderJob_File_Size_Formatting_Handles_Edge_Cases()
    {
        // Arrange
        var job1 = new RenderJob { OutputFileSizeBytes = 0 };
        var job2 = new RenderJob { OutputFileSizeBytes = 1 };
        var job3 = new RenderJob { OutputFileSizeBytes = 1023 };

        // Act
        var size1 = job1.GetOutputFileSizeFormatted();
        var size2 = job2.GetOutputFileSizeFormatted();
        var size3 = job3.GetOutputFileSizeFormatted();

        // Assert
        size1.Should().Be("0.00 B");
        size2.Should().Be("1.00 B");
        size3.Should().Be("1023.00 B");
    }
}

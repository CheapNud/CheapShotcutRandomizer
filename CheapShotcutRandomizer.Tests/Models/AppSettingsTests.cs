using CheapShotcutRandomizer.Core.Models;
using FluentAssertions;

namespace CheapShotcutRandomizer.Tests.Models;

/// <summary>
/// Unit tests for AppSettings model
/// Tests cover default values, property assignments, and RIFE executable name resolution
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void AppSettings_Has_Correct_Default_Values()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert - Logging Settings
        settings.VerboseLogging.Should().BeFalse();

        // SVP Integration
        settings.UseSvpEncoders.Should().BeTrue();

        // Path Settings
        settings.FFmpegPath.Should().Be("ffmpeg");
        settings.FFprobePath.Should().Be("ffprobe");
        settings.MeltPath.Should().Be("melt");
        settings.RifePath.Should().Be("rife-ncnn-vulkan.exe");
        settings.RifeVariant.Should().Be("Vulkan");

        // Render Default Settings
        settings.DefaultQuality.Should().Be("High");
        settings.DefaultCodec.Should().Be("libx264");
        settings.DefaultCrf.Should().Be(23);
        settings.DefaultPreset.Should().Be("medium");

        // RIFE Default Settings
        settings.DefaultRifeModel.Should().Be(46);
        settings.DefaultRifeThreads.Should().Be(2);
        settings.DefaultRifeUhdMode.Should().BeFalse();
        settings.DefaultRifeTtaMode.Should().BeFalse();

        // Application Behavior
        settings.MaxConcurrentRenders.Should().Be(1);
        settings.AutoStartQueue.Should().BeFalse();
        settings.ShowNotificationsOnComplete.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Allows_Property_Modification()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.VerboseLogging = true;
        settings.MaxConcurrentRenders = 4;
        settings.DefaultCodec = "hevc_nvenc";
        settings.DefaultCrf = 18;
        settings.FFmpegPath = @"C:\Tools\ffmpeg.exe";

        // Assert
        settings.VerboseLogging.Should().BeTrue();
        settings.MaxConcurrentRenders.Should().Be(4);
        settings.DefaultCodec.Should().Be("hevc_nvenc");
        settings.DefaultCrf.Should().Be(18);
        settings.FFmpegPath.Should().Be(@"C:\Tools\ffmpeg.exe");
    }

    [Fact]
    public void AppSettings_SVP_Integration_Can_Be_Disabled()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.UseSvpEncoders = false;

        // Assert
        settings.UseSvpEncoders.Should().BeFalse();
    }

    [Fact]
    public void AppSettings_Supports_Custom_Executable_Paths()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.FFmpegPath = @"C:\CustomPath\ffmpeg.exe";
        settings.FFprobePath = @"C:\CustomPath\ffprobe.exe";
        settings.MeltPath = @"C:\Shotcut\melt.exe";
        settings.RifePath = @"C:\RIFE\rife-ncnn-vulkan.exe";

        // Assert
        settings.FFmpegPath.Should().Be(@"C:\CustomPath\ffmpeg.exe");
        settings.FFprobePath.Should().Be(@"C:\CustomPath\ffprobe.exe");
        settings.MeltPath.Should().Be(@"C:\Shotcut\melt.exe");
        settings.RifePath.Should().Be(@"C:\RIFE\rife-ncnn-vulkan.exe");
    }

    [Fact]
    public void AppSettings_Supports_All_Quality_Presets()
    {
        // Arrange
        var settings = new AppSettings();

        // Act & Assert - Valid quality presets
        settings.DefaultQuality = "Low";
        settings.DefaultQuality.Should().Be("Low");

        settings.DefaultQuality = "Medium";
        settings.DefaultQuality.Should().Be("Medium");

        settings.DefaultQuality = "High";
        settings.DefaultQuality.Should().Be("High");

        settings.DefaultQuality = "Ultra";
        settings.DefaultQuality.Should().Be("Ultra");
    }

    [Fact]
    public void AppSettings_Supports_All_Common_Codecs()
    {
        // Arrange
        var settings = new AppSettings();

        // Act & Assert - CPU codecs
        settings.DefaultCodec = "libx264";
        settings.DefaultCodec.Should().Be("libx264");

        settings.DefaultCodec = "libx265";
        settings.DefaultCodec.Should().Be("libx265");

        // Hardware codecs - NVIDIA
        settings.DefaultCodec = "h264_nvenc";
        settings.DefaultCodec.Should().Be("h264_nvenc");

        settings.DefaultCodec = "hevc_nvenc";
        settings.DefaultCodec.Should().Be("hevc_nvenc");

        // Hardware codecs - Intel QuickSync
        settings.DefaultCodec = "h264_qsv";
        settings.DefaultCodec.Should().Be("h264_qsv");

        settings.DefaultCodec = "hevc_qsv";
        settings.DefaultCodec.Should().Be("hevc_qsv");
    }

    [Fact]
    public void AppSettings_Supports_CRF_Range()
    {
        // Arrange
        var settings = new AppSettings();

        // Act & Assert - Valid CRF values (0-51)
        settings.DefaultCrf = 0; // Lossless
        settings.DefaultCrf.Should().Be(0);

        settings.DefaultCrf = 18; // High quality
        settings.DefaultCrf.Should().Be(18);

        settings.DefaultCrf = 23; // Default
        settings.DefaultCrf.Should().Be(23);

        settings.DefaultCrf = 28; // Lower quality
        settings.DefaultCrf.Should().Be(28);

        settings.DefaultCrf = 51; // Lowest quality
        settings.DefaultCrf.Should().Be(51);
    }

    [Fact]
    public void AppSettings_Supports_All_Encoding_Presets()
    {
        // Arrange
        var settings = new AppSettings();

        // Act & Assert - Valid presets
        var validPresets = new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };

        foreach (var preset in validPresets)
        {
            settings.DefaultPreset = preset;
            settings.DefaultPreset.Should().Be(preset);
        }
    }

    [Fact]
    public void AppSettings_Supports_RIFE_Model_Versions()
    {
        // Arrange
        var settings = new AppSettings();

        // Act & Assert - Valid RIFE models
        settings.DefaultRifeModel = 46; // v4.6
        settings.DefaultRifeModel.Should().Be(46);

        settings.DefaultRifeModel = 47; // v4.7
        settings.DefaultRifeModel.Should().Be(47);

        settings.DefaultRifeModel = 415; // v4.15 (latest)
        settings.DefaultRifeModel.Should().Be(415);
    }

    [Fact]
    public void AppSettings_Supports_RIFE_Thread_Configuration()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.DefaultRifeThreads = 8;

        // Assert
        settings.DefaultRifeThreads.Should().Be(8);
        settings.DefaultRifeThreads.Should().BeInRange(1, 32);
    }

    [Fact]
    public void AppSettings_Supports_RIFE_UHD_Mode()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.DefaultRifeUhdMode = true;

        // Assert
        settings.DefaultRifeUhdMode.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Supports_RIFE_TTA_Mode()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.DefaultRifeTtaMode = true;

        // Assert
        settings.DefaultRifeTtaMode.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Supports_MaxConcurrentRenders_Range()
    {
        // Arrange
        var settings = new AppSettings();

        // Act & Assert
        settings.MaxConcurrentRenders = 1;
        settings.MaxConcurrentRenders.Should().Be(1);

        settings.MaxConcurrentRenders = 4;
        settings.MaxConcurrentRenders.Should().Be(4);

        settings.MaxConcurrentRenders = 8;
        settings.MaxConcurrentRenders.Should().Be(8);
    }

    [Fact]
    public void AppSettings_Supports_AutoStartQueue()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.AutoStartQueue = true;

        // Assert
        settings.AutoStartQueue.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Supports_Notification_Configuration()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.ShowNotificationsOnComplete = false;

        // Assert
        settings.ShowNotificationsOnComplete.Should().BeFalse();
    }

    [Fact]
    public void GetRifeExecutableName_Returns_Vulkan_Executable_By_Default()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        var executableName = settings.GetRifeExecutableName();

        // Assert
        executableName.Should().Be("rife-ncnn-vulkan.exe");
    }

    [Fact]
    public void GetRifeExecutableName_Returns_TensorRT_Executable_When_Variant_Is_TensorRT()
    {
        // Arrange
        var settings = new AppSettings
        {
            RifeVariant = "TensorRT"
        };

        // Act
        var executableName = settings.GetRifeExecutableName();

        // Assert
        executableName.Should().Be("rife-tensorrt.exe");
    }

    [Fact]
    public void GetRifeExecutableName_Returns_Vulkan_Executable_For_Unknown_Variant()
    {
        // Arrange
        var settings = new AppSettings
        {
            RifeVariant = "UnknownVariant"
        };

        // Act
        var executableName = settings.GetRifeExecutableName();

        // Assert
        executableName.Should().Be("rife-ncnn-vulkan.exe");
    }

    [Fact]
    public void AppSettings_RifeVariant_Can_Be_Changed()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.RifeVariant = "TensorRT";

        // Assert
        settings.RifeVariant.Should().Be("TensorRT");
        settings.GetRifeExecutableName().Should().Be("rife-tensorrt.exe");
    }

    [Fact]
    public void AppSettings_Supports_Verbose_Logging_Configuration()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.VerboseLogging = true;

        // Assert
        settings.VerboseLogging.Should().BeTrue();
    }

    [Fact]
    public void AppSettings_Default_Values_Are_Production_Ready()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert - Verify sensible defaults for production use
        settings.VerboseLogging.Should().BeFalse("verbose logging should be off by default");
        settings.MaxConcurrentRenders.Should().Be(1, "should default to sequential rendering for stability");
        settings.AutoStartQueue.Should().BeFalse("queue should require manual start for safety");
        settings.DefaultCrf.Should().BeInRange(18, 28, "CRF should be in reasonable quality range");
        settings.DefaultRifeThreads.Should().BeGreaterThan(0, "RIFE needs at least one thread");
    }
}

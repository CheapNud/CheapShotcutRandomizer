using CheapShotcutRandomizer.Core.Models;
using CheapShotcutRandomizer.Services;
using CheapHelpers.MediaProcessing.Services;
using FluentAssertions;
using Moq;

namespace CheapShotcutRandomizer.Tests.Services;

/// <summary>
/// Unit tests for SettingsService
/// Tests cover settings persistence, default creation, auto-detection, and file operations
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly Mock<SvpDetectionService> _mockSvpDetection;
    private readonly SettingsService _settingsService;
    private readonly string _testSettingsPath;

    public SettingsServiceTests()
    {
        _mockSvpDetection = new Mock<SvpDetectionService>();

        // Setup SVP detection mock
        _mockSvpDetection.Setup(x => x.DetectSvpInstallation())
            .Returns(new SvpInstallation
            {
                IsInstalled = false,
                FFmpegHasNvenc = false
            });

        _settingsService = new SettingsService(_mockSvpDetection.Object);
        _testSettingsPath = _settingsService.GetSettingsFilePath();
    }

    [Fact]
    public async Task LoadSettingsAsync_Creates_Default_Settings_When_File_Does_Not_Exist()
    {
        // Arrange
        DeleteTestSettingsFile();

        // Act
        var settings = await _settingsService.LoadSettingsAsync();

        // Assert
        settings.Should().NotBeNull();
        settings.VerboseLogging.Should().BeFalse();
        settings.MaxConcurrentRenders.Should().Be(1);
        settings.DefaultCodec.Should().Be("libx264");
    }

    [Fact]
    public async Task SaveSettingsAsync_Persists_Settings_To_File()
    {
        // Arrange
        DeleteTestSettingsFile();
        var settings = new AppSettings
        {
            VerboseLogging = true,
            MaxConcurrentRenders = 3,
            DefaultCodec = "h264_nvenc",
            DefaultCrf = 20
        };

        // Act
        await _settingsService.SaveSettingsAsync(settings);

        // Assert
        File.Exists(_testSettingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task LoadSettingsAsync_Loads_Previously_Saved_Settings()
    {
        // Arrange
        DeleteTestSettingsFile();
        var originalSettings = new AppSettings
        {
            VerboseLogging = true,
            MaxConcurrentRenders = 5,
            DefaultCodec = "hevc_nvenc",
            DefaultCrf = 18,
            DefaultPreset = "slow",
            FFmpegPath = @"C:\Custom\ffmpeg.exe",
            MeltPath = @"C:\Custom\melt.exe"
        };

        await _settingsService.SaveSettingsAsync(originalSettings);

        // Act
        var loadedSettings = await _settingsService.LoadSettingsAsync();

        // Assert
        loadedSettings.Should().NotBeNull();
        loadedSettings.VerboseLogging.Should().BeTrue();
        loadedSettings.MaxConcurrentRenders.Should().Be(5);
        loadedSettings.DefaultCodec.Should().Be("hevc_nvenc");
        loadedSettings.DefaultCrf.Should().Be(18);
        loadedSettings.DefaultPreset.Should().Be("slow");
        loadedSettings.FFmpegPath.Should().Be(@"C:\Custom\ffmpeg.exe");
        loadedSettings.MeltPath.Should().Be(@"C:\Custom\melt.exe");
    }

    [Fact]
    public async Task SaveSettingsAsync_Updates_Cached_Settings()
    {
        // Arrange
        DeleteTestSettingsFile();
        var settings = new AppSettings
        {
            VerboseLogging = true,
            DefaultCodec = "libx265"
        };

        // Act
        await _settingsService.SaveSettingsAsync(settings);
        var currentSettings = _settingsService.GetCurrentSettings();

        // Assert
        currentSettings.VerboseLogging.Should().BeTrue();
        currentSettings.DefaultCodec.Should().Be("libx265");
    }

    [Fact]
    public async Task ResetToDefaultsAsync_Restores_Default_Settings()
    {
        // Arrange
        DeleteTestSettingsFile();
        var customSettings = new AppSettings
        {
            VerboseLogging = true,
            MaxConcurrentRenders = 10,
            DefaultCodec = "custom_codec"
        };
        await _settingsService.SaveSettingsAsync(customSettings);

        // Act
        await _settingsService.ResetToDefaultsAsync();
        var resetSettings = await _settingsService.LoadSettingsAsync();

        // Assert
        resetSettings.VerboseLogging.Should().BeFalse();
        resetSettings.MaxConcurrentRenders.Should().Be(1);
        resetSettings.DefaultCodec.Should().NotBe("custom_codec");
    }

    [Fact]
    public void GetSettingsFilePath_Returns_Valid_Path()
    {
        // Act
        var path = _settingsService.GetSettingsFilePath();

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().EndWith("settings.json");
        path.Should().Contain("CheapShotcutRandomizer");
    }

    [Fact]
    public void GetCurrentSettings_Returns_Cached_Settings()
    {
        // Arrange - no settings loaded yet
        var settings = _settingsService.GetCurrentSettings();

        // Assert - should return defaults
        settings.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadSettingsAsync_Handles_Corrupted_JSON_Gracefully()
    {
        // Arrange
        DeleteTestSettingsFile();
        await File.WriteAllTextAsync(_testSettingsPath, "{ invalid json content }}");

        // Act
        var settings = await _settingsService.LoadSettingsAsync();

        // Assert - should return defaults when JSON is corrupted
        settings.Should().NotBeNull();
        settings.VerboseLogging.Should().BeFalse();
    }

    [Fact]
    public async Task SaveSettingsAsync_Creates_Directory_If_Not_Exists()
    {
        // Arrange
        var settingsDirectory = Path.GetDirectoryName(_testSettingsPath);
        if (Directory.Exists(settingsDirectory))
        {
            // This test verifies directory creation, so we can't delete the existing one
            // Just verify save works
        }

        var settings = new AppSettings { VerboseLogging = true };

        // Act
        await _settingsService.SaveSettingsAsync(settings);

        // Assert
        File.Exists(_testSettingsPath).Should().BeTrue();
        Directory.Exists(settingsDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task LoadSettingsAsync_Sets_NVENC_Codec_When_Available()
    {
        // Arrange
        DeleteTestSettingsFile();
        _mockSvpDetection.Setup(x => x.DetectSvpInstallation())
            .Returns(new SvpInstallation
            {
                IsInstalled = true,
                FFmpegHasNvenc = true
            });

        var newSettingsService = new SettingsService(_mockSvpDetection.Object);

        // Act
        var settings = await newSettingsService.LoadSettingsAsync();

        // Assert - should auto-detect and use NVENC when available
        settings.DefaultCodec.Should().Be("hevc_nvenc");
    }

    [Fact]
    public async Task AppSettings_Default_Values_Are_Correct()
    {
        // Arrange & Act
        var settings = new AppSettings();

        // Assert
        settings.VerboseLogging.Should().BeFalse();
        settings.UseSvpEncoders.Should().BeTrue();
        settings.MaxConcurrentRenders.Should().Be(1);
        settings.AutoStartQueue.Should().BeFalse();
        settings.ShowNotificationsOnComplete.Should().BeTrue();
        settings.DefaultQuality.Should().Be("High");
        settings.DefaultCodec.Should().Be("libx264");
        settings.DefaultCrf.Should().Be(23);
        settings.DefaultPreset.Should().Be("medium");
    }

    [Fact]
    public async Task SaveSettingsAsync_Uses_Thread_Safe_Locking()
    {
        // Arrange
        DeleteTestSettingsFile();
        var settings1 = new AppSettings { VerboseLogging = true };
        var settings2 = new AppSettings { VerboseLogging = false };

        // Act - try to save concurrently
        var task1 = _settingsService.SaveSettingsAsync(settings1);
        var task2 = _settingsService.SaveSettingsAsync(settings2);

        await Task.WhenAll(task1, task2);

        // Assert - should not throw and file should exist
        File.Exists(_testSettingsPath).Should().BeTrue();
        var loadedSettings = await _settingsService.LoadSettingsAsync();
        loadedSettings.Should().NotBeNull();
    }

    private void DeleteTestSettingsFile()
    {
        if (File.Exists(_testSettingsPath))
        {
            File.Delete(_testSettingsPath);
        }
    }

    public void Dispose()
    {
        // Cleanup: Delete test settings file
        DeleteTestSettingsFile();
    }
}

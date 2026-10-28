using System;
using System.IO;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class SettingsSystemTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IAppDataService> _mockAppData;
    private readonly SettingsService _settingsService;

    public SettingsSystemTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SettingsSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _mockAppData = new Mock<IAppDataService>();
        _mockAppData.Setup(a => a.RootDirectory).Returns(_tempDirectory);

        _settingsService = new SettingsService(_mockAppData.Object, NullLogger<SettingsService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task LoadSettingsAsync_WhenFirstRun_CreatesDefaultSettingsFile()
    {
        string settingsFile = Path.Combine(_tempDirectory, "settings.json");
        File.Exists(settingsFile).Should().BeFalse();

        var settings = await _settingsService.LoadSettingsAsync();

        File.Exists(settingsFile).Should().BeTrue();
        settings.Should().NotBeNull();
        settings.Language.Should().Be("vi-VN");
        settings.Theme.Should().Be("Dark");
        settings.BackupRetentionDays.Should().Be(30);
        settings.AutoBackupBeforeReplacement.Should().BeTrue();
        settings.ConfidenceThreshold.Should().Be(0.85);
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenCalled_UpdatesFileAndCurrentSettings()
    {
        var settings = await _settingsService.LoadSettingsAsync();

        settings.Theme = "Light";
        settings.BackupRetentionDays = 60;
        settings.DefaultSuffixPattern = "_Updated2026";
        settings.ConfidenceThreshold = 0.90;

        await _settingsService.SaveSettingsAsync(settings);

        _settingsService.CurrentSettings.Theme.Should().Be("Light");
        _settingsService.CurrentSettings.BackupRetentionDays.Should().Be(60);
        _settingsService.CurrentSettings.DefaultSuffixPattern.Should().Be("_Updated2026");
        _settingsService.CurrentSettings.ConfidenceThreshold.Should().Be(0.90);

        // Nạp lại từ đĩa để xác nhận đã lưu vật lý
        var reloaded = await _settingsService.LoadSettingsAsync();
        reloaded.Theme.Should().Be("Light");
        reloaded.BackupRetentionDays.Should().Be(60);
    }

    [Fact]
    public async Task ResetToDefaultsAsync_WhenCalled_RestoresAllDefaultValues()
    {
        var settings = await _settingsService.LoadSettingsAsync();
        settings.Theme = "Light";
        settings.BackupRetentionDays = 120;
        settings.DefaultSuffixPattern = "_CustomPattern";
        await _settingsService.SaveSettingsAsync(settings);

        // Khôi phục mặc định
        var defaults = await _settingsService.ResetToDefaultsAsync();

        defaults.Theme.Should().Be("Dark");
        defaults.BackupRetentionDays.Should().Be(30);
        defaults.DefaultSuffixPattern.Should().Be("_2026");
        _settingsService.CurrentSettings.Theme.Should().Be("Dark");
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenThemeChanged_FiresSettingsChangedEvent()
    {
        var settings = await _settingsService.LoadSettingsAsync();

        bool eventFired = false;
        AppSettings? notifiedSettings = null;

        _settingsService.SettingsChanged += s =>
        {
            eventFired = true;
            notifiedSettings = s;
        };

        settings.Theme = "Light";
        await _settingsService.SaveSettingsAsync(settings);

        eventFired.Should().BeTrue();
        notifiedSettings.Should().NotBeNull();
        notifiedSettings!.Theme.Should().Be("Light");
    }

    [Fact]
    public void ValidateAndNormalize_EnforcesSafeBoundaries()
    {
        // 1. Kiểm thử giới hạn dưới
        var s1 = new AppSettings
        {
            MaxDegreeOfParallelism = -10,
            ScanTimeoutSecondsPerFile = 1,
            BackupRetentionDays = -5,
            AuditLogRetentionDays = 0,
            ConfidenceThreshold = -0.5
        };

        s1.ValidateAndNormalize();

        s1.MaxDegreeOfParallelism.Should().Be(1);
        s1.ScanTimeoutSecondsPerFile.Should().Be(5);
        s1.BackupRetentionDays.Should().Be(1);
        s1.AuditLogRetentionDays.Should().Be(1);
        s1.ConfidenceThreshold.Should().Be(0.1);

        // 2. Kiểm thử giới hạn trên
        var s2 = new AppSettings
        {
            MaxDegreeOfParallelism = 100,
            ScanTimeoutSecondsPerFile = 1000,
            ConfidenceThreshold = 2.5
        };

        s2.ValidateAndNormalize();

        s2.MaxDegreeOfParallelism.Should().Be(32);
        s2.ScanTimeoutSecondsPerFile.Should().Be(300);
        s2.ConfidenceThreshold.Should().Be(1.0);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.App.Services;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Cài Đặt Hệ Thống (Settings View - Phase 16).
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFileDialogService _fileDialogService;
    private readonly ILogger<SettingsViewModel> _logger;

    // 1. General Settings
    [ObservableProperty]
    private string _language = "vi-VN";

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    // 2. Performance & Processing
    [ObservableProperty]
    private int _maxDegreeOfParallelism = 4;

    [ObservableProperty]
    private int _scanTimeoutSecondsPerFile = 30;

    [ObservableProperty]
    private bool _enableOpenXmlValidation = true;

    // 3. Safety & Backup
    [ObservableProperty]
    private int _backupRetentionDays = 30;

    [ObservableProperty]
    private bool _autoBackupBeforeReplacement = true;

    [ObservableProperty]
    private bool _verifyChecksumAfterRestore = true;

    [ObservableProperty]
    private bool _warnOnLowConfidenceDates = true;

    [ObservableProperty]
    private double _confidenceThreshold = 0.85;

    // 4. Date Replacement Defaults
    [ObservableProperty]
    private string _defaultDateFormat = "dd/MM/yyyy";

    [ObservableProperty]
    private string _defaultOutputMode = "Suffix";

    [ObservableProperty]
    private string _defaultSuffixPattern = "_2026";

    [ObservableProperty]
    private string _defaultOutputFolder = string.Empty;

    // 5. Privacy & Compliance
    [ObservableProperty]
    private bool _maskPersonalDataInLogs = true;

    [ObservableProperty]
    private int _auditLogRetentionDays = 90;

    [ObservableProperty]
    private string _feedbackMessage = string.Empty;

    public IReadOnlyList<string> AvailableLanguages { get; } = new[] { "vi-VN", "en-US" };
    public IReadOnlyList<string> AvailableThemes { get; } = new[] { "Dark", "Light", "System" };
    public IReadOnlyList<string> AvailableOutputModes { get; } = new[] { "Suffix", "NewFolder", "InPlace" };
    public IReadOnlyList<string> AvailableDateFormats { get; } = new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "d 'tháng' M 'năm' yyyy" };

    public event Action? RequestNavigateBack;

    public SettingsViewModel(
        ISettingsService settingsService,
        IFileDialogService fileDialogService,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _fileDialogService = fileDialogService;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        FeedbackMessage = string.Empty;
        try
        {
            var s = await _settingsService.LoadSettingsAsync();
            PopulateFromModel(s);
            FeedbackMessage = "Đã tải cấu hình hiện tại.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi nạp cấu hình");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
        {
            var s = new AppSettings
            {
                Language = Language,
                Theme = Theme,
                AutoCheckUpdates = AutoCheckUpdates,
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                ScanTimeoutSecondsPerFile = ScanTimeoutSecondsPerFile,
                EnableOpenXmlValidation = EnableOpenXmlValidation,
                BackupRetentionDays = BackupRetentionDays,
                AutoBackupBeforeReplacement = AutoBackupBeforeReplacement,
                VerifyChecksumAfterRestore = VerifyChecksumAfterRestore,
                WarnOnLowConfidenceDates = WarnOnLowConfidenceDates,
                ConfidenceThreshold = ConfidenceThreshold,
                DefaultDateFormat = DefaultDateFormat,
                DefaultOutputMode = DefaultOutputMode,
                DefaultSuffixPattern = DefaultSuffixPattern,
                DefaultOutputFolder = DefaultOutputFolder,
                MaskPersonalDataInLogs = MaskPersonalDataInLogs,
                AuditLogRetentionDays = AuditLogRetentionDays
            };

            await _settingsService.SaveSettingsAsync(s);
            FeedbackMessage = "Đã lưu cài đặt hệ thống thành công!";
            _logger.LogInformation("Cài đặt hệ thống đã được lưu thành công.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu cài đặt");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ResetDefaultsAsync()
    {
        try
        {
            var defaults = await _settingsService.ResetToDefaultsAsync();
            PopulateFromModel(defaults);
            FeedbackMessage = "Đã khôi phục toàn bộ cài đặt về mặc định ban đầu.";
            _logger.LogInformation("Đã khôi phục cài đặt mặc định.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi khôi phục cài đặt");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public void BrowseOutputFolder()
    {
        string? selected = _fileDialogService.ShowFolderDialog();
        if (!string.IsNullOrEmpty(selected))
        {
            DefaultOutputFolder = selected;
        }
    }

    [RelayCommand]
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }

    private void PopulateFromModel(AppSettings s)
    {
        Language = s.Language;
        Theme = s.Theme;
        AutoCheckUpdates = s.AutoCheckUpdates;
        MaxDegreeOfParallelism = s.MaxDegreeOfParallelism;
        ScanTimeoutSecondsPerFile = s.ScanTimeoutSecondsPerFile;
        EnableOpenXmlValidation = s.EnableOpenXmlValidation;
        BackupRetentionDays = s.BackupRetentionDays;
        AutoBackupBeforeReplacement = s.AutoBackupBeforeReplacement;
        VerifyChecksumAfterRestore = s.VerifyChecksumAfterRestore;
        WarnOnLowConfidenceDates = s.WarnOnLowConfidenceDates;
        ConfidenceThreshold = s.ConfidenceThreshold;
        DefaultDateFormat = s.DefaultDateFormat;
        DefaultOutputMode = s.DefaultOutputMode;
        DefaultSuffixPattern = s.DefaultSuffixPattern;
        DefaultOutputFolder = s.DefaultOutputFolder;
        MaskPersonalDataInLogs = s.MaskPersonalDataInLogs;
        AuditLogRetentionDays = s.AuditLogRetentionDays;
    }
}

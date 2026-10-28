using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAppDataService _appDataService;
    private readonly ILogger<MainViewModel> _logger;

    private List<ReplacementOccurrence> _lastOccurrences = new();

    public DashboardViewModel Dashboard { get; }
    public ReviewViewModel Review { get; }
    public PreviewViewModel Preview { get; }
    public ExecutionViewModel Execution { get; }
    public ReportViewModel Report { get; }
    public HistoryRollbackViewModel HistoryRollback { get; }
    public RuleManagementViewModel RuleManagement { get; }
    public PresetManagementViewModel PresetManagement { get; }
    public AuditComplianceViewModel AuditCompliance { get; }
    public SettingsViewModel Settings { get; }
    public AttendanceCalendarViewModel AttendanceCalendar { get; }

    [ObservableProperty]
    private string _title = "Thay đổi ngày tài liệu Word và Excel";

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng làm việc";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _activeView = "Dashboard"; // "Dashboard", "Review", "Preview", "Execution", "Report", "HistoryRollback", "RuleManagement", "PresetManagement", "AuditCompliance", "Settings"

    public string ContinueButtonText => "Tiếp tục: Rà soát ngày";

    public string AppVersion => "v1.1.0 (Word + Excel)";

    public MainViewModel(
        DashboardViewModel dashboardViewModel,
        ReviewViewModel reviewViewModel,
        PreviewViewModel previewViewModel,
        ExecutionViewModel executionViewModel,
        ReportViewModel reportViewModel,
        HistoryRollbackViewModel historyRollbackViewModel,
        RuleManagementViewModel ruleManagementViewModel,
        PresetManagementViewModel presetManagementViewModel,
        AuditComplianceViewModel auditComplianceViewModel,
        SettingsViewModel settingsViewModel,
        IAppDataService appDataService,
        ILogger<MainViewModel> logger, AttendanceCalendarViewModel attendanceCalendarViewModel)
    {
        AttendanceCalendar = attendanceCalendarViewModel;
        AttendanceCalendar.RequestNavigateBack += () => { ActiveView = "Dashboard"; StatusMessage = "Quay về trang chủ"; };
        Dashboard = dashboardViewModel;
        Review = reviewViewModel;
        Preview = previewViewModel;
        Execution = executionViewModel;
        Report = reportViewModel;
        HistoryRollback = historyRollbackViewModel;
        RuleManagement = ruleManagementViewModel;
        PresetManagement = presetManagementViewModel;
        AuditCompliance = auditComplianceViewModel;
        Settings = settingsViewModel;
        _appDataService = appDataService;
        _logger = logger;

        Dashboard.RequestNavigateToReview += () =>
        {
            var detectedItems = Dashboard.GetDetectedItems();
            Review.LoadCandidates(detectedItems);
            ActiveView = "Review";
            StatusMessage = $"Đang rà soát {Review.TotalDatesCount} ngày phát hiện";
            _logger.LogInformation("Chuyển sang màn hình Review với {Count} ngày phát hiện.", Review.TotalDatesCount);
        };

        Review.RequestNavigateBack += () =>
        {
            ActiveView = "Dashboard";
            StatusMessage = "Quay lại danh sách tệp";
            _logger.LogInformation("Quay lại màn hình Dashboard từ Review.");
        };

        Review.RequestNavigateToPreview += () =>
        {
            Preview.LoadDiffItems(Review.DateItems);
            ActiveView = "Preview";
            StatusMessage = $"Xem trước thay đổi ({Preview.ChangedDiffsCount} vị trí sẽ thay thế)";
            _logger.LogInformation("Chuyển sang màn hình Preview với {Count} vị trí thay đổi.", Preview.ChangedDiffsCount);
        };

        Preview.RequestNavigateBack += () =>
        {
            ActiveView = "Review";
            StatusMessage = $"Đang rà soát {Review.TotalDatesCount} ngày phát hiện";
            _logger.LogInformation("Quay lại màn hình Review từ Preview.");
        };

        Preview.RequestExecuteReplacement += () =>
        {
            _lastOccurrences = Review.DateItems.Select(d => new ReplacementOccurrence
            {
                FilePath = d.FilePath,
                Segment = d.Segment ?? new DocumentTextSegment(),
                Candidate = d.Candidate,
                NewDateText = d.FormattedReplacementText,
                NewDateTime = d.TargetNewDate,
                IsSelected = d.HasEffectiveChange
            }).ToList();

            ActiveView = "Execution";
            StatusMessage = "Đang thực hiện thay thế & xác minh...";
            _logger.LogInformation("Bắt đầu thực thi thay thế {Count} vị trí ngày.", _lastOccurrences.Count(o => o.IsSelected));

            _ = Execution.StartExecutionAsync(_lastOccurrences);
        };

        Execution.RequestNavigateBack += () =>
        {
            ActiveView = "Preview";
            StatusMessage = "Quay lại màn hình xem trước thay đổi";
        };

        Execution.RequestNavigateToReport += () =>
        {
            Report.LoadReport(Execution.ExecutionFiles, _lastOccurrences, Execution.OutputDirectoryPath);
            ActiveView = "Report";
            StatusMessage = "Báo cáo kết quả thay thế ngày hợp đồng";
            _logger.LogInformation("Chuyển sang màn hình Báo Cáo Chi Tiết.");
        };

        Report.RequestNavigateBack += () =>
        {
            ActiveView = "Execution";
            StatusMessage = "Kết quả thay thế và xác minh";
        };

        Report.RequestResetToNewBatch += () =>
        {
            Dashboard.ResetBatch();
            ActiveView = "Dashboard";
            StatusMessage = "Sẵn sàng làm việc cho đợt mới";
            _logger.LogInformation("Khởi động đợt làm việc mới, quay về màn hình Dashboard.");
        };

        HistoryRollback.RequestNavigateBack += () =>
        {
            ActiveView = "Dashboard";
            StatusMessage = "Quay về trang chủ";
        };

        RuleManagement.RequestNavigateBack += () =>
        {
            ActiveView = "Dashboard";
            StatusMessage = "Quay về trang chủ";
        };

        PresetManagement.RequestNavigateBack += () =>
        {
            ActiveView = "Dashboard";
            StatusMessage = "Quay về trang chủ";
        };

        AuditCompliance.RequestNavigateBack += () =>
        {
            ActiveView = "Dashboard";
            StatusMessage = "Quay về trang chủ";
        };

        Settings.RequestNavigateBack += () =>
        {
            ActiveView = "Dashboard";
            StatusMessage = "Quay về trang chủ";
        };

        _logger.LogInformation("MainViewModel initialized with all Views (Dashboard, Review, Preview, Execution, Report, Rollback, Rules, Presets, Audit, Settings). AppData: {Path}", _appDataService.RootDirectory);
    }

    [RelayCommand]
    public async Task OpenRollbackAsync()
    {
        await HistoryRollback.LoadHistoryAsync();
        ActiveView = "HistoryRollback";
        StatusMessage = "Lịch sử thay thế & Khôi phục file";
    }

    [RelayCommand]
    public async Task OpenRuleManagementAsync()
    {
        await RuleManagement.LoadRulesAsync();
        ActiveView = "RuleManagement";
        StatusMessage = "Quản lý quy tắc nhận diện & phân loại";
    }

    [RelayCommand]
    public async Task OpenPresetManagementAsync()
    {
        await PresetManagement.LoadPresetsAsync();
        ActiveView = "PresetManagement";
        StatusMessage = "Mẫu thiết lập thay thế ngày (Presets)";
    }

    [RelayCommand]
    public async Task OpenAuditComplianceAsync()
    {
        await AuditCompliance.LoadLogsAsync();
        ActiveView = "AuditCompliance";
        StatusMessage = "Nhật ký kiểm toán & tuân thủ (Audit Log)";
    }

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        await Settings.LoadSettingsAsync();
        ActiveView = "Settings";
        StatusMessage = "Cài đặt hệ thống";
    }

    [RelayCommand]
    public void OpenAttendanceCalendar()
    {
        ActiveView = "AttendanceCalendar";
        StatusMessage = "Đổi tháng bảng chấm công";
    }

    [RelayCommand]
    public void Navigate(string viewName)
    {
        ActiveView = viewName;
        StatusMessage = $"Đang hiển thị: {viewName}";
        _logger.LogInformation("Navigated to {View}", viewName);
    }
}



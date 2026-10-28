using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Nhật Ký Kiểm Toán & Tuân Thủ (Audit Log & Compliance - Phase 15).
/// </summary>
public partial class AuditComplianceViewModel : ObservableObject
{
    private readonly IAuditLogService _auditLogService;
    private readonly IAppDataService _appDataService;
    private readonly ILogger<AuditComplianceViewModel> _logger;

    public ObservableCollection<AuditLogItemViewModel> Logs { get; } = new();

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private AuditActionType? _selectedActionFilter;

    [ObservableProperty]
    private AuditSeverity? _selectedSeverityFilter;

    [ObservableProperty]
    private string _feedbackMessage = string.Empty;

    // Integrity Check Results
    [ObservableProperty]
    private bool _hasIntegrityResult;

    [ObservableProperty]
    private bool _isTamperFree = true;

    [ObservableProperty]
    private string _integrityStatusText = "Chưa kiểm tra";

    [ObservableProperty]
    private string _integrityBadgeAppearance = "Secondary";

    public IReadOnlyList<AuditActionType?> AvailableActionFilters { get; } = new List<AuditActionType?>
    {
        null,
        AuditActionType.BatchExecution,
        AuditActionType.FileRollback,
        AuditActionType.BatchRollback,
        AuditActionType.RuleModified,
        AuditActionType.PresetModified,
        AuditActionType.ConfigChanged,
        AuditActionType.SecurityAlert
    };

    public IReadOnlyList<AuditSeverity?> AvailableSeverityFilters { get; } = new List<AuditSeverity?>
    {
        null,
        AuditSeverity.Info,
        AuditSeverity.Warning,
        AuditSeverity.Error,
        AuditSeverity.Critical
    };

    public event Action? RequestNavigateBack;

    public AuditComplianceViewModel(
        IAuditLogService auditLogService,
        IAppDataService appDataService,
        ILogger<AuditComplianceViewModel> logger)
    {
        _auditLogService = auditLogService;
        _appDataService = appDataService;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadLogsAsync()
    {
        Logs.Clear();
        FeedbackMessage = string.Empty;

        try
        {
            var filter = new AuditLogFilter
            {
                SearchKeyword = string.IsNullOrWhiteSpace(SearchKeyword) ? null : SearchKeyword.Trim(),
                ActionType = SelectedActionFilter,
                Severity = SelectedSeverityFilter,
                Page = 1,
                PageSize = 200
            };

            var entries = await _auditLogService.QueryLogsAsync(filter);
            foreach (var e in entries)
            {
                Logs.Add(new AuditLogItemViewModel(e));
            }

            FeedbackMessage = $"Đã tải {Logs.Count} sự kiện kiểm toán.";
            _logger.LogInformation("Đã tải {Count} audit log entries.", Logs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi nạp nhật ký kiểm toán");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    partial void OnSearchKeywordChanged(string value) => _ = LoadLogsAsync();
    partial void OnSelectedActionFilterChanged(AuditActionType? value) => _ = LoadLogsAsync();
    partial void OnSelectedSeverityFilterChanged(AuditSeverity? value) => _ = LoadLogsAsync();

    [RelayCommand]
    public async Task VerifyIntegrityAsync()
    {
        try
        {
            var report = await _auditLogService.VerifyLogIntegrityAsync();
            HasIntegrityResult = true;
            IsTamperFree = report.IsTamperFree;

            if (report.IsTamperFree)
            {
                IntegrityStatusText = $"Toàn vẹn 100% ({report.TotalLogsScanned} bản ghi hợp lệ)";
                IntegrityBadgeAppearance = "Success";
                FeedbackMessage = "Xác nhận: Toàn bộ nhật ký kiểm toán toàn vẹn, không có dấu vết can thiệp thủ công!";
            }
            else
            {
                IntegrityStatusText = $"CẢNH BÁO: Phát hiện {report.TamperedLogsCount} log bị sửa đổi!";
                IntegrityBadgeAppearance = "Danger";
                FeedbackMessage = report.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xác thực chuỗi log");
            FeedbackMessage = $"Lỗi xác thực: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        try
        {
            string exportDir = Path.Combine(_appDataService.RootDirectory, "Reports");
            string fileName = $"AuditLogs_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string dest = Path.Combine(exportDir, fileName);

            var filter = new AuditLogFilter
            {
                SearchKeyword = string.IsNullOrWhiteSpace(SearchKeyword) ? null : SearchKeyword.Trim(),
                ActionType = SelectedActionFilter,
                Severity = SelectedSeverityFilter,
                PageSize = 5000
            };

            await _auditLogService.ExportLogsCsvAsync(dest, filter);
            FeedbackMessage = $"Đã xuất báo cáo CSV kiểm toán tại: {dest}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xuất CSV kiểm toán");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportJsonAsync()
    {
        try
        {
            string exportDir = Path.Combine(_appDataService.RootDirectory, "Reports");
            string fileName = $"AuditLogs_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            string dest = Path.Combine(exportDir, fileName);

            var filter = new AuditLogFilter
            {
                SearchKeyword = string.IsNullOrWhiteSpace(SearchKeyword) ? null : SearchKeyword.Trim(),
                ActionType = SelectedActionFilter,
                Severity = SelectedSeverityFilter,
                PageSize = 5000
            };

            await _auditLogService.ExportLogsJsonAsync(dest, filter);
            FeedbackMessage = $"Đã xuất báo cáo JSON kiểm toán tại: {dest}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xuất JSON kiểm toán");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }
}

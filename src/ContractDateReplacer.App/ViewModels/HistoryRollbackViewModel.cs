using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Lịch sử Thay Thế & Khôi Phục (Phase 12 - Rollback / Restore).
/// Cho phép duyệt các đợt đã chạy, xem chi tiết checksum SHA256,
/// khôi phục từng file hoặc toàn bộ batch với đối soát mã băm nghiêm ngặt.
/// </summary>
public partial class HistoryRollbackViewModel : ObservableObject
{
    private readonly IBackupRollbackService _backupService;
    private readonly ILogger<HistoryRollbackViewModel> _logger;

    public ObservableCollection<BatchHistoryGroupViewModel> Batches { get; } = new();

    [ObservableProperty]
    private BatchHistoryGroupViewModel? _selectedBatch;

    [ObservableProperty]
    private string _statusFeedback = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public event Action? RequestNavigateBack;

    public HistoryRollbackViewModel(
        IBackupRollbackService backupService,
        ILogger<HistoryRollbackViewModel> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        IsBusy = true;
        StatusFeedback = "Đang tải lịch sử sao lưu...";
        Batches.Clear();

        try
        {
            var allBackups = await _backupService.GetAllBackupsAsync();

            var groups = allBackups
                .GroupBy(b => b.BatchId)
                .OrderByDescending(g => g.Max(b => b.CreatedAt))
                .ToList();

            foreach (var group in groups)
            {
                var batchVm = new BatchHistoryGroupViewModel
                {
                    BatchId = group.Key,
                    CreatedAt = group.Max(b => b.CreatedAt)
                };

                foreach (var b in group)
                {
                    batchVm.Backups.Add(b);
                }

                Batches.Add(batchVm);
            }

            SelectedBatch = Batches.FirstOrDefault();
            StatusFeedback = $"Đã tải {Batches.Count} đợt sao lưu hợp đồng.";
            _logger.LogInformation("Đã nạp {Count} nhóm đợt sao lưu trong lịch sử khôi phục.", Batches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi nạp lịch sử sao lưu");
            StatusFeedback = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestoreSingleFileAsync(BackupRecord? backup)
    {
        if (backup == null) return;

        IsBusy = true;
        StatusFeedback = $"Đang khôi phục file {backup.FileName}...";

        try
        {
            var result = await _backupService.RestoreFileAsync(backup.BackupId);
            if (result.IsSuccess)
            {
                backup.Status = "Restored";
                StatusFeedback = $"✓ {result.Message}";
                _logger.LogInformation("Khôi phục thành công file {File}", backup.FileName);
            }
            else
            {
                StatusFeedback = $"✗ Khôi phục thất bại: {result.Message}";
                _logger.LogWarning("Khôi phục thất bại cho file {File}: {Msg}", backup.FileName, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khôi phục file {File}", backup.FileName);
            StatusFeedback = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestoreEntireBatchAsync(BatchHistoryGroupViewModel? batch)
    {
        if (batch == null || string.IsNullOrWhiteSpace(batch.BatchId)) return;

        IsBusy = true;
        StatusFeedback = $"Đang khôi phục toàn bộ {batch.Backups.Count} file trong đợt...";

        try
        {
            var results = await _backupService.RestoreBatchAsync(batch.BatchId);
            int successCount = results.Count(r => r.IsSuccess);
            int failedCount = results.Count(r => !r.IsSuccess);

            foreach (var b in batch.Backups)
            {
                var matchingResult = results.FirstOrDefault(r => r.BackupId == b.BackupId);
                if (matchingResult != null && matchingResult.IsSuccess)
                {
                    b.Status = "Restored";
                }
            }

            StatusFeedback = $"Khôi phục hoàn tất: {successCount} file thành công, {failedCount} lỗi.";
            _logger.LogInformation("Hoàn tất khôi phục đợt {Batch}: {Success} thành công, {Failed} lỗi.", batch.BatchId, successCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khôi phục đợt {Batch}", batch.BatchId);
            StatusFeedback = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CleanupExpiredAsync()
    {
        IsBusy = true;
        StatusFeedback = "Đang dọn dẹp các bản sao lưu hết hạn lưu trữ (> 30 ngày)...";

        try
        {
            int deleted = await _backupService.CleanupExpiredBackupsAsync(retentionDays: 30);
            StatusFeedback = $"Đã dọn dẹp {deleted} bản sao lưu hết hạn lưu trữ.";
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi dọn dẹp bản sao lưu");
            StatusFeedback = $"Lỗi: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }
}

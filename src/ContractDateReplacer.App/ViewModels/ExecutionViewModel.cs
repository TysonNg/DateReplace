using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Execution & Progress View (Phase 10).
/// Điều phối tiến trình thay thế bất đồng bộ, cập nhật thanh tiến độ,
/// hiển thị trạng thái từng file, ghi log trực tiếp, và hỗ trợ hủy bỏ.
/// </summary>
public partial class ExecutionViewModel : ObservableObject
{
    private readonly IDocumentReplacementService _replacementService;
    private readonly IDocumentVerificationService _verificationService;
    private readonly IBackupRollbackService _backupService;
    private readonly IAppDataService _appDataService;
    private readonly ILogger<ExecutionViewModel> _logger;

    private CancellationTokenSource? _executionCts;

    public ObservableCollection<FileExecutionItemViewModel> ExecutionFiles { get; } = new();
    public ObservableCollection<LogEntryItem> ActivityLogs { get; } = new();

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private string _currentProcessingText = "Sẵn sàng thực thi...";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isCancelled;

    [ObservableProperty]
    private string _summaryResultsText = string.Empty;

    [ObservableProperty]
    private string _outputDirectoryPath = string.Empty;

    public event Action? RequestNavigateToReport;
    public event Action? RequestNavigateBack;

    public ExecutionViewModel(
        IDocumentReplacementService replacementService,
        IDocumentVerificationService verificationService,
        IBackupRollbackService backupService,
        IAppDataService appDataService,
        ILogger<ExecutionViewModel> logger)
    {
        _replacementService = replacementService;
        _verificationService = verificationService;
        _backupService = backupService;
        _appDataService = appDataService;
        _logger = logger;
    }

    public async Task StartExecutionAsync(IReadOnlyList<ReplacementOccurrence> allOccurrences, string? customOutputDirectory = null)
    {
        if (IsRunning) return;

        IsRunning = true;
        IsCompleted = false;
        IsCancelled = false;
        ProgressPercentage = 0;
        ExecutionFiles.Clear();
        ActivityLogs.Clear();

        _executionCts = new CancellationTokenSource();
        var token = _executionCts.Token;

        // Xác định thư mục lưu file mới an toàn (tuyệt đối không ghi đè file gốc)
        string firstFilePath = allOccurrences.FirstOrDefault()?.FilePath ?? string.Empty;
        string baseDir = !string.IsNullOrWhiteSpace(firstFilePath) && File.Exists(firstFilePath)
            ? Path.GetDirectoryName(Path.GetFullPath(firstFilePath))!
            : Path.Combine(_appDataService.RootDirectory, "Output");

        OutputDirectoryPath = customOutputDirectory 
            ?? Path.Combine(baseDir, $"Modified_Contracts_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(OutputDirectoryPath);

        // Nhóm các điểm thay thế theo từng file
        var groupedFiles = allOccurrences
            .GroupBy(o => o.FilePath)
            .ToList();

        foreach (var group in groupedFiles)
        {
            var item = new FileExecutionItemViewModel(group.Key, group.Count(o => o.IsSelected));
            ExecutionFiles.Add(item);
        }

        AddLog($"Khởi động tiến trình thay thế ngày cho {ExecutionFiles.Count} tệp hợp đồng...", "Info");
        AddLog($"Thư mục đầu ra an toàn: {OutputDirectoryPath}", "Info");

        var reservedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int totalFiles = ExecutionFiles.Count;
        int successCount = 0;
        int failedCount = 0;
        string batchId = $"BATCH_{DateTime.Now:yyyyMMdd_HHmmss}";

        try
        {
            for (int i = 0; i < totalFiles; i++)
            {
                if (token.IsCancellationRequested)
                {
                    IsCancelled = true;
                    AddLog("Tiến trình đã bị người dùng hủy bỏ!", "Warning");
                    break;
                }

                var fileItem = ExecutionFiles[i];
                var fileOccurrences = groupedFiles.First(g => g.Key == fileItem.FilePath).ToList();

                CurrentProcessingText = $"Đang xử lý: File {i + 1}/{totalFiles} — {fileItem.FileName}";
                fileItem.Status = ExecutionItemStatus.Processing;
                fileItem.StatusText = "Đang thay thế...";

                AddLog($"Bắt đầu xử lý {fileItem.FileName}...", "Info");

                string extension = Path.GetExtension(fileItem.FileName).ToLowerInvariant();
                if (extension == ".doc") extension = ".docx";
                string destFileName = $"{Path.GetFileNameWithoutExtension(fileItem.FileName)}_modified{extension}";
                string destFilePath = Path.Combine(OutputDirectoryPath, destFileName);
                int suffix = 2;
                while (File.Exists(destFilePath) || !reservedOutputPaths.Add(destFilePath))
                {
                    destFilePath = Path.Combine(OutputDirectoryPath,
                        $"{Path.GetFileNameWithoutExtension(fileItem.FileName)}_modified_{suffix++}{extension}");
                }
                fileItem.OutputFilePath = destFilePath;

                try
                {
                    int toReplaceCount = fileOccurrences.Count(o => o.IsSelected);

                    // 0. Tạo bản sao lưu an toàn trước khi thay thế (Khắc cốt ghi tâm: Không làm mất file gốc)
                    try
                    {
                        var backup = await _backupService.CreateBackupAsync(
                            batchId,
                            fileItem.FilePath,
                            destFilePath,
                            toReplaceCount,
                            token);
                        fileItem.BackupFilePath = backup.BackupPath;
                        string shortHash = backup.ChecksumSHA256.Length >= 8 ? backup.ChecksumSHA256[..8] : backup.ChecksumSHA256;
                        AddLog($"Backup tạo: {Path.GetFileName(backup.BackupPath)} (SHA256: {shortHash}...)", "Info");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Không thể tạo backup cho {File}: {Msg}", fileItem.FileName, ex.Message);
                        AddLog($"Cảnh báo backup: {ex.Message}", "Warning");
                    }

                    // 1. Thực hiện thay thế ngày
                    AddLog($"Thay thế {toReplaceCount} vị trí ngày trong {fileItem.FileName}...", "Info");

                    bool replaced = await _replacementService.ReplaceDatesAsync(
                        fileItem.FilePath,
                        destFilePath,
                        fileOccurrences,
                        preserveOriginalDateFormat: true,
                        cancellationToken: token);

                    if (!replaced)
                    {
                        fileItem.Status = ExecutionItemStatus.Failed;
                        fileItem.StatusText = "Không có vị trí nào được thay thế";
                        AddLog($"Không có thay đổi nào được thực hiện cho {fileItem.FileName}.", "Warning");
                        failedCount++;
                        continue;
                    }

                    fileItem.ReplacedCount = toReplaceCount;

                    // 2. Chạy xác minh độc lập sau thay thế (Post-Replacement Verification)
                    AddLog($"Re-scan verification cho {fileItem.FileName}...", "Info");

                    var verifyResult = await _verificationService.VerifyOutputAsync(
                        fileItem.FilePath,
                        destFilePath,
                        fileOccurrences,
                        cancellationToken: token);

                    fileItem.VerificationResult = verifyResult;

                    if (verifyResult.IsPassed)
                    {
                        fileItem.Status = ExecutionItemStatus.Success;
                        fileItem.StatusText = $"Đã thay {verifyResult.ActualReplacementsDone} ngày — Verify PASS";
                        AddLog($"Verify PASS {fileItem.FileName}: 0 lỗi, SafetyScore {verifyResult.SafetyScore}%.", "Success");
                        successCount++;
                    }
                    else
                    {
                        fileItem.Status = ExecutionItemStatus.Failed;
                        fileItem.StatusText = $"Verify FAILED: {verifyResult.Issues.FirstOrDefault() ?? "Không đạt tiêu chuẩn an toàn"}";
                        AddLog($"Verify FAILED {fileItem.FileName}: {string.Join("; ", verifyResult.Issues)}", "Error");
                        failedCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    fileItem.Status = ExecutionItemStatus.Cancelled;
                    fileItem.StatusText = "Bị hủy bỏ";
                    IsCancelled = true;
                    AddLog($"Tiến trình đối với {fileItem.FileName} đã bị dừng.", "Warning");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi thực thi với file {File}", fileItem.FileName);
                    fileItem.Status = ExecutionItemStatus.Failed;
                    fileItem.StatusText = $"Lỗi: {ex.Message}";
                    AddLog($"Lỗi xử lý {fileItem.FileName}: {ex.Message}", "Error");
                    failedCount++;
                }

                ProgressPercentage = ((double)(i + 1) / totalFiles) * 100;
            }

            if (IsCancelled)
            {
                CurrentProcessingText = "Tiến trình đã dừng lại.";
                SummaryResultsText = $"Đã dừng: {successCount} file thành công • {failedCount} file lỗi • {totalFiles - successCount - failedCount} file chưa xử lý";
            }
            else
            {
                ProgressPercentage = 100;
                CurrentProcessingText = "Hoàn tất toàn bộ tiến trình thay thế & xác minh!";
                SummaryResultsText = $"Thành công: {successCount}/{totalFiles} tệp hợp đồng (Lỗi: {failedCount})";
                AddLog($"Hoàn tất: {successCount} tệp thành công, {failedCount} tệp gặp sự cố.", successCount > 0 ? "Success" : "Warning");
            }
        }
        finally
        {
            IsRunning = false;
            IsCompleted = true;
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    public bool CanCancel => IsRunning && _executionCts != null && !_executionCts.IsCancellationRequested;

    [RelayCommand]
    public void Cancel()
    {
        if (_executionCts != null && !_executionCts.IsCancellationRequested)
        {
            AddLog("Đang gửi tín hiệu yêu cầu dừng tiến trình...", "Warning");
            _executionCts.Cancel();
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    [RelayCommand]
    public void OpenOutputFolder()
    {
        if (!string.IsNullOrWhiteSpace(OutputDirectoryPath) && Directory.Exists(OutputDirectoryPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = OutputDirectoryPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể mở thư mục: {Dir}", OutputDirectoryPath);
            }
        }
    }

    [RelayCommand]
    public void ViewReport()
    {
        RequestNavigateToReport?.Invoke();
    }

    [RelayCommand]
    public void NavigateBack()
    {
        if (IsRunning) return;
        RequestNavigateBack?.Invoke();
    }

    private void AddLog(string message, string level = "Info")
    {
        var log = new LogEntryItem(message, level);
        ActivityLogs.Add(log);
        _logger.LogInformation("[ExecutionLog] {Message}", message);
    }
}




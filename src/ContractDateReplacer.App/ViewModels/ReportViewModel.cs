using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Báo Cáo Sau Thay Thế (Detailed Report View - Phase 11).
/// Hiển thị tổng quan số liệu, chi tiết từng file dạng collapsible expander,
/// xuất báo cáo CSV, mở file log và nút làm đợt mới.
/// </summary>
public partial class ReportViewModel : ObservableObject
{
    private readonly IAppDataService _appDataService;
    private readonly ILogger<ReportViewModel> _logger;

    public ObservableCollection<ReportFileDetailViewModel> FileReports { get; } = new();

    [ObservableProperty]
    private int _totalFilesProcessed;

    [ObservableProperty]
    private int _successFilesCount;

    [ObservableProperty]
    private int _warningFilesCount;

    [ObservableProperty]
    private int _failedFilesCount;

    [ObservableProperty]
    private int _totalDatesReplaced;

    [ObservableProperty]
    private int _totalDatesPreserved;

    [ObservableProperty]
    private int _averageSafetyScore = 100;

    [ObservableProperty]
    private string _outputDirectoryPath = string.Empty;

    [ObservableProperty]
    private string _exportMessage = string.Empty;

    public event Action? RequestResetToNewBatch;
    public event Action? RequestNavigateBack;

    private readonly IExportService? _exportService;

    public ReportViewModel(
        IAppDataService appDataService,
        ILogger<ReportViewModel> logger,
        IExportService? exportService = null)
    {
        _appDataService = appDataService;
        _logger = logger;
        _exportService = exportService;
    }

    public ReplacementBatchReport BuildBatchReport()
    {
        var report = new ReplacementBatchReport
        {
            BatchId = DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            CreatedTime = DateTime.Now,
            TotalFiles = TotalFilesProcessed,
            SuccessFiles = SuccessFilesCount,
            WarningFiles = WarningFilesCount,
            FailedFiles = FailedFilesCount,
            TotalDatesReplaced = TotalDatesReplaced,
            TotalDatesPreserved = TotalDatesPreserved,
            AverageSafetyScore = AverageSafetyScore,
            IntegrityHash = Guid.NewGuid().ToString("N").ToUpperInvariant()
        };

        foreach (var file in FileReports)
        {
            var fileItem = new BatchReportFileItem
            {
                FileName = file.FileName,
                OriginalPath = file.FilePath,
                OutputPath = file.OutputFilePath,
                SafetyScore = file.SafetyScore,
                Status = file.StatusBadgeAppearance == "Success" ? "Success" : "ReviewRequired",
                Issues = file.Issues.ToList()
            };

            foreach (var item in file.ReplacedItems)
            {
                var dateItem = new BatchReportDateItem
                {
                    SourceFileName = file.FileName,
                    SourceLocation = item.SourceLocation,
                    OldDateText = item.OldDateText,
                    NewDateText = item.NewDateText,
                    ClassificationTypeName = item.ClassificationTypeName,
                    Status = item.ResultBadgeText,
                    SafetyScore = file.SafetyScore
                };

                if (item.IsReplaced) fileItem.ReplacedDatesCount++;
                else fileItem.PreservedDatesCount++;

                fileItem.Dates.Add(dateItem);
                report.AllDates.Add(dateItem);
            }

            report.Files.Add(fileItem);
        }

        return report;
    }

    [RelayCommand]
    public async Task ExportExcelAsync()
    {
        if (FileReports.Count == 0) return;
        string outDir = GetReportsOutputDirectory();
        string xlsxPath = Path.Combine(outDir, $"BaoCao_ThayDoiNgay_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

        try
        {
            var report = BuildBatchReport();
            if (_exportService != null)
            {
                await _exportService.ExportReportAsync(report, ExportFormat.ExcelXlsx, xlsxPath);
            }
            ExportMessage = $"Đã xuất báo cáo Excel thành công: {Path.GetFileName(xlsxPath)}";
            OpenFile(xlsxPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xuất Excel");
            ExportMessage = $"Lỗi xuất Excel: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportJsonAsync()
    {
        if (FileReports.Count == 0) return;
        string outDir = GetReportsOutputDirectory();
        string jsonPath = Path.Combine(outDir, $"BaoCao_ThayDoiNgay_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        try
        {
            var report = BuildBatchReport();
            if (_exportService != null)
            {
                await _exportService.ExportReportAsync(report, ExportFormat.Json, jsonPath);
            }
            ExportMessage = $"Đã xuất báo cáo JSON thành công: {Path.GetFileName(jsonPath)}";
            OpenFile(jsonPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xuất JSON");
            ExportMessage = $"Lỗi xuất JSON: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportHtmlPdfAsync()
    {
        if (FileReports.Count == 0) return;
        string outDir = GetReportsOutputDirectory();
        string htmlPath = Path.Combine(outDir, $"BaoCao_ThayDoiNgay_{DateTime.Now:yyyyMMdd_HHmmss}.html");

        try
        {
            var report = BuildBatchReport();
            if (_exportService != null)
            {
                await _exportService.ExportReportAsync(report, ExportFormat.PdfHtml, htmlPath);
            }
            ExportMessage = $"Đã xuất báo cáo HTML thành công: {Path.GetFileName(htmlPath)}";
            OpenFile(htmlPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xuất HTML");
            ExportMessage = $"Lỗi xuất HTML: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ExportCsv()
    {
        if (FileReports.Count == 0) return;

        try
        {
            string outDir = GetReportsOutputDirectory();
            string csvPath = Path.Combine(outDir, $"BaoCao_ThayDoiNgay_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("Tên File,Đường Dẫn File Mới,Điểm An Toàn,Vị Trí,Ngày Gốc,Ngày Mới,Phân Loại,Kết Quả");
            foreach (var file in FileReports)
            {
                foreach (var item in file.ReplacedItems)
                {
                    sb.AppendLine($"\"{file.FileName}\",\"{file.OutputFilePath}\",{file.SafetyScore},\"{item.SourceLocation}\",\"{item.OldDateText}\",\"{item.NewDateText}\",\"{item.ClassificationTypeName}\",\"{item.ResultBadgeText}\"");
                }
            }
            File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(true));

            ExportMessage = $"Đã xuất báo cáo CSV thành công: {Path.GetFileName(csvPath)}";
            OpenFile(csvPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xuất báo cáo CSV");
            ExportMessage = $"Lỗi xuất CSV: {ex.Message}";
        }
    }

    private string GetReportsOutputDirectory()
    {
        string outDir = Directory.Exists(OutputDirectoryPath)
            ? OutputDirectoryPath
            : Path.Combine(_appDataService.RootDirectory, "Reports");
        Directory.CreateDirectory(outDir);
        return outDir;
    }

    private static void OpenFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    public void LoadReport(
        IReadOnlyList<FileExecutionItemViewModel> executionFiles,
        IReadOnlyList<ReplacementOccurrence> allOccurrences,
        string outputDirectory)
    {
        FileReports.Clear();
        OutputDirectoryPath = outputDirectory;
        ExportMessage = string.Empty;

        TotalFilesProcessed = executionFiles.Count;
        TotalDatesReplaced = 0;
        TotalDatesPreserved = 0;

        int totalScore = 0;
        int successCount = 0;
        int warningCount = 0;
        int failedCount = 0;

        foreach (var file in executionFiles)
        {
            var fileReport = new ReportFileDetailViewModel
            {
                FilePath = file.FilePath,
                OutputFilePath = file.OutputFilePath,
                SafetyScore = file.VerificationResult?.SafetyScore ?? (file.Status == ExecutionItemStatus.Success ? 100 : 0)
            };

            totalScore += fileReport.SafetyScore;

            if (file.VerificationResult != null)
            {
                foreach (var issue in file.VerificationResult.Issues)
                {
                    fileReport.Issues.Add(issue);
                }

                if (file.VerificationResult.IsPassed)
                {
                    fileReport.StatusBadgeText = "PASS";
                    fileReport.StatusBadgeAppearance = "Success";
                    successCount++;
                }
                else if (file.VerificationResult.FinalStatus == FileScanStatus.ReviewRequired)
                {
                    fileReport.StatusBadgeText = "WARNING";
                    fileReport.StatusBadgeAppearance = "Caution";
                    warningCount++;
                }
                else
                {
                    fileReport.StatusBadgeText = "FAILED";
                    fileReport.StatusBadgeAppearance = "Danger";
                    failedCount++;
                }
            }
            else
            {
                if (file.Status == ExecutionItemStatus.Success)
                {
                    fileReport.StatusBadgeText = "PASS";
                    fileReport.StatusBadgeAppearance = "Success";
                    successCount++;
                }
                else
                {
                    fileReport.StatusBadgeText = "FAILED";
                    fileReport.StatusBadgeAppearance = "Danger";
                    failedCount++;
                }
            }

            // Lấy danh sách các ngày thuộc file này
            var fileOccurrences = allOccurrences.Where(o => o.FilePath == file.FilePath).ToList();

            foreach (var occ in fileOccurrences)
            {
                var detailItem = new ReportDateDetailItem
                {
                    OldDateText = occ.Candidate.RawText,
                    NewDateText = occ.NewDateText,
                    ClassificationTypeName = occ.Candidate.Classification.Type.ToString(),
                    SourceLocation = occ.Segment.SourceLocation,
                    IsReplaced = occ.IsSelected
                };

                if (occ.IsSelected)
                {
                    TotalDatesReplaced++;
                    detailItem.ResultBadgeText = "[OK]";
                    detailItem.ResultBadgeAppearance = "Success";
                }
                else
                {
                    TotalDatesPreserved++;
                    detailItem.ResultBadgeText = "[GIỮ NGUYÊN THEO YÊU CẦU]";
                    detailItem.ResultBadgeAppearance = "Secondary";
                }

                fileReport.ReplacedItems.Add(detailItem);
            }

            FileReports.Add(fileReport);
        }

        SuccessFilesCount = successCount;
        WarningFilesCount = warningCount;
        FailedFilesCount = failedCount;
        AverageSafetyScore = TotalFilesProcessed > 0 ? (totalScore / TotalFilesProcessed) : 100;

        _logger.LogInformation("Tải dữ liệu báo cáo: {Total} file, Điểm trung bình: {Score}%, Đã thay: {Replaced}, Giữ nguyên: {Preserved}",
            TotalFilesProcessed, AverageSafetyScore, TotalDatesReplaced, TotalDatesPreserved);
    }


    [RelayCommand]
    public void OpenLogFile()
    {
        try
        {
            string logDir = Path.Combine(_appDataService.RootDirectory, "Logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không thể mở thư mục Logs");
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
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }

    [RelayCommand]
    public void ResetForNewBatch()
    {
        RequestResetToNewBatch?.Invoke();
    }
}


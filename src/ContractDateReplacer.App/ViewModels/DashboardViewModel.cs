using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.App.Services;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDocumentScanner _documentScanner;
    private readonly IDateDetector _dateDetector;
    private readonly IDateClassifier _dateClassifier;
    private readonly IFileDialogService _fileDialogService;
    private readonly ILogger<DashboardViewModel> _logger;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "All"; // "All", "Pass", "Review", "Error"

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private double _scanProgress;

    [ObservableProperty]
    private string _scanStatusText = string.Empty;

    [ObservableProperty]
    private int _totalFilesCount;

    [ObservableProperty]
    private int _selectedFilesCount;

    [ObservableProperty]
    private int _passCount;

    [ObservableProperty]
    private int _reviewRequiredCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _notScannedCount;

    [ObservableProperty]
    private ContractFileItem? _selectedFileItem;
    [ObservableProperty]
    private string _selectedFileType = "All";
    [ObservableProperty]
    private bool _includeHiddenSheets;

    public ObservableCollection<DateCandidate> SelectedFileDates { get; } = new();
    public ObservableCollection<string> SelectedFileSheets { get; } = new();
    public string SelectedFileSummary => SelectedFileItem == null ? "Chọn tài liệu để xem chi tiết" :
        $"{SelectedFileItem.DocumentType} · {SelectedFileItem.FormattedFileSize} · {SelectedFileItem.TotalDatesDetected} ngày phát hiện";

    partial void OnSelectedFileItemChanged(ContractFileItem? value) => RefreshSelectedFileDetails();
    partial void OnSelectedFileTypeChanged(string value) => FilteredFilesView.Refresh();
    partial void OnIncludeHiddenSheetsChanged(bool value)
    {
        RefreshSelectedFileDetails();
        OnPropertyChanged(nameof(CanNavigateToReview));
    }
    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanNavigateToReview));
    }

    private void RefreshSelectedFileDetails()
    {
        SelectedFileDates.Clear();
        SelectedFileSheets.Clear();
        if (SelectedFileItem != null)
        {
            foreach (var date in SelectedFileItem.Segments.Where(s => IncludeHiddenSheets || !s.IsHiddenSheet).SelectMany(s => s.DetectedDates)) SelectedFileDates.Add(date);
            foreach (var group in SelectedFileItem.Segments.Where(s => !string.IsNullOrEmpty(s.WorksheetName)).GroupBy(s => s.WorksheetName))
                SelectedFileSheets.Add($"{group.Key} · {group.Sum(s => s.DetectedDates.Count)} ngày");
        }
        OnPropertyChanged(nameof(SelectedFileSummary));
    }
    public ObservableCollection<ContractFileItem> Files { get; } = new();
    public ICollectionView FilteredFilesView { get; }

    public bool HasFiles => Files.Count > 0;
    public bool CanScan => Files.Count > 0 && !IsScanning;
    public bool CanNavigateToReview => !IsScanning && GetDetectedItems().Count > 0;

    public event Action? RequestNavigateToReview;

    public DashboardViewModel(
        IDocumentScanner documentScanner,
        IDateDetector dateDetector,
        IDateClassifier dateClassifier,
        IFileDialogService fileDialogService,
        ILogger<DashboardViewModel> logger)
    {
        _documentScanner = documentScanner;
        _dateDetector = dateDetector;
        _dateClassifier = dateClassifier;
        _fileDialogService = fileDialogService;
        _logger = logger;

        FilteredFilesView = CollectionViewSource.GetDefaultView(Files);
        FilteredFilesView.Filter = FilterFileItem;

        Files.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (ContractFileItem item in e.NewItems)
                {
                    item.PropertyChanged += OnFileItemPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (ContractFileItem item in e.OldItems)
                {
                    item.PropertyChanged -= OnFileItemPropertyChanged;
                }
            }
            UpdateCounters();
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(CanScan));
        };
    }

    private void OnFileItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContractFileItem.IsSelected) ||
            e.PropertyName == nameof(ContractFileItem.Status))
        {
            UpdateCounters();
            OnPropertyChanged(nameof(CanNavigateToReview));
        }
    }

    partial void OnSearchKeywordChanged(string value)
    {
        FilteredFilesView.Refresh();
    }

    partial void OnSelectedFilterChanged(string value)
    {
        FilteredFilesView.Refresh();
    }

    private bool FilterFileItem(object obj)
    {
        if (obj is not ContractFileItem item) return false;
        if (SelectedFileType == "Word" && item.DocumentType != "Word") return false;
        if (SelectedFileType == "Excel" && item.DocumentType != "Excel") return false;

        // 1. Filter theo tab trạng thái
        bool statusMatch = SelectedFilter switch
        {
            "Pass" => item.Status == FileScanStatus.Pass || item.Status == FileScanStatus.Completed,
            "Review" => item.Status == FileScanStatus.ReviewRequired,
            "Error" => item.Status == FileScanStatus.Error,
            _ => true // "All"
        };

        if (!statusMatch) return false;

        // 2. Filter theo SearchKeyword
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return true;

        return item.FileName.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase) ||
               item.FilePath.Contains(SearchKeyword, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        SelectedFilter = filter;
    }

    [RelayCommand]
    public void AddFiles()
    {
        var filePaths = _fileDialogService.ShowWordFilesDialog();
        if (filePaths != null && filePaths.Length > 0)
        {
            AddFilePaths(filePaths);
        }
    }

    [RelayCommand]
    public void AddFolder()
    {
        string? folderPath = _fileDialogService.ShowFolderDialog();
        if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
        {
            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).Where(IsSupportedDocument)
                    .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal));
                AddFilePaths(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi quét file trong thư mục {Folder}", folderPath);
            }
        }
    }

    public void AddFilePaths(IEnumerable<string> paths)
    {
        int addedCount = 0;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (Directory.Exists(path))
            {
                try
                {
                    var innerFiles = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(IsSupportedDocument)
                        .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal));
                    foreach (var inner in innerFiles)
                    {
                        if (AddSingleFile(inner)) addedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi quét folder drag-drop {Path}", path);
                }
            }
            else if (File.Exists(path))
            {
                if (IsSupportedDocument(path) &&
                    !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
                {
                    if (AddSingleFile(path)) addedCount++;
                }
            }
        }

        if (addedCount > 0)
        {
            _logger.LogInformation("Đã thêm {Count} file hợp đồng vào danh sách.", addedCount);
            FilteredFilesView.Refresh();
        }
    }

    private static bool IsSupportedDocument(string path) =>
        Path.GetExtension(path).Equals(".doc", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    private bool AddSingleFile(string filePath)
    {
        // Tránh trùng lặp file theo đường dẫn tuyệt đối
        string fullPath = Path.GetFullPath(filePath);
        if (Files.Any(f => string.Equals(f.FilePath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var fileInfo = new FileInfo(fullPath);
        var item = new ContractFileItem
        {
            FilePath = fullPath,
            FileSizeBytes = fileInfo.Length,
            Status = FileScanStatus.NotScanned
        };

        Files.Add(item);
        SelectedFileItem ??= item;
        return true;
    }

    [RelayCommand]
    public void RemoveFile(ContractFileItem item)
    {
        if (item != null)
        {
            Files.Remove(item);
            if (SelectedFileItem == item) SelectedFileItem = Files.FirstOrDefault();
        }
    }

    [RelayCommand]
    public void ClearAll()
    {
        if (IsScanning)
        {
            CancelScan();
        }
        Files.Clear();
        SelectedFileItem = null;
        FilteredFilesView.Refresh();
    }

    public void ResetBatch()
    {
        ClearAll();
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in Files)
        {
            item.IsSelected = true;
        }
        UpdateCounters();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var item in Files)
        {
            item.IsSelected = false;
        }
        UpdateCounters();
    }

    [RelayCommand]
    public async Task ScanDocumentsAsync()
    {
        var targetFiles = Files.Where(f => f.IsSelected).ToList();
        if (targetFiles.Count == 0) return;

        IsScanning = true;
        ScanProgress = 0;
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        _logger.LogInformation("Bắt đầu quét {Count} file hợp đồng...", targetFiles.Count);

        try
        {
            int total = targetFiles.Count;
            for (int i = 0; i < total; i++)
            {
                if (token.IsCancellationRequested) break;

                var file = targetFiles[i];
                file.Status = FileScanStatus.Scanning;
                ScanStatusText = $"Đang quét {i + 1} / {total} file: {file.FileName}";
                ScanProgress = ((double)(i + 1) / total) * 100;

                try
                {
                    var segments = await _documentScanner.ScanDocumentAsync(file.FilePath, token);
                    segments = segments.Where(s => IncludeHiddenSheets || !s.IsHiddenSheet).ToList();
                    file.Segments = segments;

                    int totalDetected = 0;
                    int replaceable = 0;
                    bool hasSensitiveOrReview = false;

                    foreach (var segment in segments)
                    {
                        token.ThrowIfCancellationRequested();
                        var candidates = segment.StoryType == StoryType.SpreadsheetCell ? segment.DetectedDates : _dateDetector.DetectDates(segment);
                        if (candidates.Count > 0)
                        {
                            await _dateClassifier.ClassifyBatchAsync(candidates, token, segment);
                            if (!segment.IsSupportedForReplacement)
                                foreach (var candidate in candidates) candidate.Classification.IsRecommendedForReplacement = false;
                            segment.DetectedDates = candidates;
                            totalDetected += candidates.Count;
                            replaceable += candidates.Count(c => c.Classification.IsRecommendedForReplacement);

                            if (!segment.IsSupportedForReplacement || candidates.Any(c => c.Classification.IsReviewRequired || 
                                                    c.Classification.Type is DateClassificationType.BirthDate 
                                                                          or DateClassificationType.IdentityIssueDate))
                            {
                                hasSensitiveOrReview = true;
                            }
                        }
                    }

                    file.DetectedCandidates = segments.SelectMany(s => s.DetectedDates).ToList();
                    file.UnknownCount = file.DetectedCandidates.Count(c => c.Classification.IsReviewRequired);
                    file.ClassifiedCount = totalDetected - file.UnknownCount;
                    file.TotalDatesDetected = totalDetected;
                    file.ReplaceableDatesCount = replaceable;

                    if (segments.Count == 0)
                    {
                        file.Status = FileScanStatus.ReviewRequired;
                        file.StatusMessage = "Tài liệu không chứa đoạn văn bản nào";
                    }
                    else if (hasSensitiveOrReview)
                    {
                        file.Status = FileScanStatus.ReviewRequired;
                        file.StatusMessage = $"{totalDetected} ngày phát hiện • {replaceable} ngày đề xuất thay (Có ngày nhân thân/cần review)";
                    }
                    else
                    {
                        file.Status = FileScanStatus.Pass;
                        file.StatusMessage = $"{totalDetected} ngày phát hiện • {replaceable} ngày đề xuất thay";
                    }
                }
                catch (OperationCanceledException)
                {
                    file.Status = FileScanStatus.NotScanned;
                    file.StatusMessage = "Quá trình quét bị huỷ bỏ";
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi quét file {Path}", file.FilePath);
                    file.Status = FileScanStatus.Error;
                    file.StatusMessage = $"Không thể đọc tài liệu: {ex.Message}";
                }

                UpdateCounters();
            }

            if (!token.IsCancellationRequested)
            {
                ScanStatusText = $"Hoàn tất quét {total} file hợp đồng.";
            }
            else
            {
                ScanStatusText = "Đã dừng quét tài liệu.";
            }
        }
        finally
        {
            RefreshSelectedFileDetails();
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
            UpdateCounters();
            OnPropertyChanged(nameof(CanScan));
            OnPropertyChanged(nameof(CanNavigateToReview));
        }
    }

    [RelayCommand]
    public void CancelScan()
    {
        if (_scanCts != null && !_scanCts.IsCancellationRequested)
        {
            _scanCts.Cancel();
            ScanStatusText = "Đang dừng tiến trình quét...";
        }
    }

    [RelayCommand]
    public void ContinueToReview()
    {
        RequestNavigateToReview?.Invoke();
    }

    private void UpdateCounters()
    {
        TotalFilesCount = Files.Count;
        SelectedFilesCount = Files.Count(f => f.IsSelected);
        PassCount = Files.Count(f => f.Status == FileScanStatus.Pass || f.Status == FileScanStatus.Completed);
        ReviewRequiredCount = Files.Count(f => f.Status == FileScanStatus.ReviewRequired);
        ErrorCount = Files.Count(f => f.Status == FileScanStatus.Error);
        NotScannedCount = Files.Count(f => f.Status == FileScanStatus.NotScanned);

        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanNavigateToReview));
    }

    /// <summary>
    /// Thu thập tất cả các phân đoạn và ứng viên ngày phát hiện từ các file đã quét.
    /// </summary>
    public List<(ContractFileItem File, DocumentTextSegment Segment, List<DateCandidate> Candidates)> GetDetectedItems()
    {
        var result = new List<(ContractFileItem File, DocumentTextSegment Segment, List<DateCandidate> Candidates)>();
        foreach (var file in Files.Where(f => f.IsSelected && (f.Status is FileScanStatus.Pass or FileScanStatus.ReviewRequired or FileScanStatus.Completed) && f.Segments.Count > 0))
        {
            foreach (var segment in file.Segments.Where(s => IncludeHiddenSheets || !s.IsHiddenSheet))
            {
                if (segment.DetectedDates.Count > 0)
                {
                    result.Add((file, segment, segment.DetectedDates));
                }
            }
        }
        return result;
    }
}






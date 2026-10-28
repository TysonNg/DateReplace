using System.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Review & Selection (Phase 6).
/// Cho phép người dùng duyệt tất cả các ngày phát hiện, chọn ngày cần thay,
/// chỉ định ngày mới hàng loạt hoặc riêng từng dòng, và lọc theo nhóm ngữ cảnh.
/// </summary>
public partial class ReviewViewModel : ObservableObject
{
    private const string AllFilesFilter = "Tất cả các file";
    private readonly IDateIntelligenceService? _dateIntelligenceService;
    private readonly List<ContractFileItem> _previewFiles = new();
    private readonly Dictionary<string, string> _fileFilterPathByLabel = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _selectedSheetFilter = "Tất cả sheet";
    public ObservableCollection<string> SheetFilterOptions { get; } = new() { "Tất cả sheet" };
    public bool IsSpreadsheetPreview => SelectedDateItem?.FileItem.DocumentType == "Excel";
    public DataView ExcelContextRows { get; private set; } = new DataTable().DefaultView;
    public string ContinueButtonText => $"Xem trước {DateItems.Count(d => d.HasEffectiveChange)} thay đổi";
    public string ReadinessMessage
    {
        get
        {
            var selected = DateItems.Where(d => d.IsSelected).ToList();
            if (selected.Count == 0) return "Chọn ít nhất một ngày có thể thay thế. Ô công thức luôn được giữ nguyên.";
            int missing = selected.Count(d => !d.TargetNewDate.HasValue);
            if (missing > 0) return $"{missing} ngày đã chọn chưa có giá trị mới.";
            int changes = selected.Count(d => d.HasEffectiveChange);
            return changes == 0 ? "Ngày mới trùng ngày hiện tại; chưa có thay đổi để xem trước." :
                $"{changes} thay đổi sẵn sàng · {DateItems.Count - selected.Count} ngày giữ nguyên";
        }
    }

    partial void OnSelectedSheetFilterChanged(string value)
    {
        FilteredDateItemsView.Refresh();
        EnsureVisiblePreviewSelection();
    }
    public ObservableCollection<DateItemViewModel> DateItems { get; } = new();
    public ICollectionView FilteredDateItemsView { get; }

    public ObservableCollection<string> FileFilterOptions { get; } = new();

    [ObservableProperty]
    private string? _selectedFileFilter = "Tất cả các file";

    [ObservableProperty]
    private string _activeCategoryFilter = "All";

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private DateTime _batchTargetDate = DateTime.Today;

    [ObservableProperty]
    private string _currentFileInfoText = string.Empty;

    [ObservableProperty]
    private DateItemViewModel? _selectedDateItem;

    [ObservableProperty]
    private string _previewFileName = "Chưa chọn file";

    [ObservableProperty]
    private string _previewLocation = "Chọn một ngày để xem đúng vị trí trong tài liệu";

    [ObservableProperty]
    private int _previewRevision;

    public ObservableCollection<PreviewTextSegment> PreviewSegments { get; } = new();
    public bool HasPreviewContent => PreviewSegments.Any(x => !string.IsNullOrWhiteSpace(x.Text));
    public bool HasDateItems => DateItems.Count > 0;
    public bool HasSensitiveWarnings => SensitiveWarningsCount > 0;

    // Events điều hướng
    public event Action? RequestNavigateBack;
    public event Action? RequestNavigateToPreview;

    public ReviewViewModel(IDateIntelligenceService? dateIntelligenceService = null)
    {
        _dateIntelligenceService = dateIntelligenceService;
        FilteredDateItemsView = CollectionViewSource.GetDefaultView(DateItems);
        FilteredDateItemsView.Filter = FilterDateItem;

        FileFilterOptions.Add("Tất cả các file");
    }

    public void LoadCandidates(IEnumerable<(ContractFileItem File, DocumentTextSegment Segment, List<DateCandidate> Candidates)> detectedItems)
    {
        var detected = detectedItems.ToList();
        SelectedSheetFilter = "Tất cả sheet";
        SheetFilterOptions.Clear();
        SheetFilterOptions.Add("Tất cả sheet");
        DateItems.Clear();
        _previewFiles.Clear();
        _fileFilterPathByLabel.Clear();
        PreviewSegments.Clear();
        SelectedDateItem = null;
        FileFilterOptions.Clear();
        FileFilterOptions.Add(AllFilesFilter);

        _previewFiles.AddRange(detected
            .Select(x => x.File)
            .DistinctBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase));

        foreach (var fileGroup in _previewFiles.GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
        {
            bool needsPath = fileGroup.Count() > 1;
            foreach (var file in fileGroup)
            {
                string label = needsPath ? $"{file.FileName} — {file.DirectoryPath}" : file.FileName;
                FileFilterOptions.Add(label);
                _fileFilterPathByLabel[label] = file.FilePath;
            }
        }

        SelectedFileFilter = AllFilesFilter;
        ActiveCategoryFilter = "All";
        SearchKeyword = string.Empty;

        foreach (var (file, segment, candidates) in detected)
        {
            foreach (var candidate in candidates)
            {
                var itemVm = new DateItemViewModel(candidate, file, segment, _dateIntelligenceService);
                itemVm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName is nameof(DateItemViewModel.IsSelected) or nameof(DateItemViewModel.TargetNewDate))
                    {
                        UpdateCounters();
                    }
                };
                DateItems.Add(itemVm);
            }
        }

        PopulateSheetOptions();
        UpdateCounters();
        FilteredDateItemsView.Refresh();
        EnsureVisiblePreviewSelection();
        RefreshPreview();
    }

    partial void OnSelectedFileFilterChanged(string? value)
    {
        PopulateSheetOptions();
        FilteredDateItemsView.Refresh();
        UpdateCurrentFileInfoText();
        EnsureVisiblePreviewSelection();
    }

    partial void OnActiveCategoryFilterChanged(string value)
    {
        FilteredDateItemsView.Refresh();
        EnsureVisiblePreviewSelection();
    }

    partial void OnSearchKeywordChanged(string value)
    {
        FilteredDateItemsView.Refresh();
        EnsureVisiblePreviewSelection();
    }

    private void PopulateSheetOptions()
    {
        string previous = SelectedSheetFilter;
        SheetFilterOptions.Clear();
        SheetFilterOptions.Add("Tất cả sheet");
        _fileFilterPathByLabel.TryGetValue(SelectedFileFilter ?? "", out string? selectedPath);
        foreach (var sheet in DateItems.Where(d => selectedPath == null || d.FilePath == selectedPath)
            .Select(d => d.Segment?.WorksheetName).Where(n => !string.IsNullOrEmpty(n)).Distinct())
            SheetFilterOptions.Add(sheet!);
        SelectedSheetFilter = SheetFilterOptions.Contains(previous) ? previous : "Tất cả sheet";
    }
    private bool FilterDateItem(object obj)
    {
        if (obj is not DateItemViewModel item) return false;
        if (SelectedSheetFilter != "Tất cả sheet" && item.Segment?.WorksheetName != SelectedSheetFilter) return false;

        // 1. Lọc theo file
        if (!string.IsNullOrWhiteSpace(SelectedFileFilter) &&
            !string.Equals(SelectedFileFilter, AllFilesFilter, StringComparison.OrdinalIgnoreCase))
        {
            if (!_fileFilterPathByLabel.TryGetValue(SelectedFileFilter, out string? selectedPath) ||
                !string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 2. Lọc theo nhóm ngữ cảnh
        bool matchesCategory = ActiveCategoryFilter switch
        {
            "SignAndEffective" => item.ClassificationType is DateClassificationType.ContractSignDate
                                                          or DateClassificationType.ContractEffectiveDate
                                                          or DateClassificationType.ContractStartDate
                                                          or DateClassificationType.ContractEndDate,

            "PaymentAndHandover" => item.ClassificationType is DateClassificationType.PaymentDate
                                                            or DateClassificationType.HandoverDate
                                                            or DateClassificationType.AppendixDate,

            "Personal" => item.IsSensitivePersonalData,

            "Review" => item.IsReviewRequired || item.ClassificationType == DateClassificationType.Unknown,

            _ => true
        };

        if (!matchesCategory) return false;

        // 3. Lọc theo từ khóa tìm kiếm
        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            string kw = SearchKeyword.Trim();
            bool matchesSearch = item.RawText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                 item.FormattedContextDisplay.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                 item.ClassificationTypeName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                                 item.FileName.Contains(kw, StringComparison.OrdinalIgnoreCase);

            if (!matchesSearch) return false;
        }

        return true;
    }

    #region Commands

    [RelayCommand]
    public void SelectDateForPreview(DateItemViewModel? item)
    {
        if (item == null) return;

        string itemFilter = _fileFilterPathByLabel.FirstOrDefault(x =>
            string.Equals(x.Value, item.FilePath, StringComparison.OrdinalIgnoreCase)).Key ?? item.FileName;
        if (!string.Equals(SelectedFileFilter, itemFilter, StringComparison.OrdinalIgnoreCase))
        {
            SelectedFileFilter = itemFilter;
        }

        SelectedDateItem = item;
        RefreshPreview();
    }

    [RelayCommand]
    public void SetCategoryFilter(string category)
    {
        ActiveCategoryFilter = category;
    }

    [RelayCommand]
    public void SelectAllRecommended()
    {
        foreach (var item in FilteredDateItemsView.Cast<DateItemViewModel>().ToList())
        {
            item.IsSelected = item.IsRecommendedForReplacement;
        }
        UpdateCounters();
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in FilteredDateItemsView.Cast<DateItemViewModel>().ToList())
        {
            item.IsSelected = true;
        }
        UpdateCounters();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var item in FilteredDateItemsView.Cast<DateItemViewModel>().ToList())
        {
            item.IsSelected = false;
        }
        UpdateCounters();
    }

    [RelayCommand]
    public void ApplyBatchDate()
    {
        int appliedCount = 0;
        foreach (var item in FilteredDateItemsView.Cast<DateItemViewModel>().ToList())
        {
            if (item.IsSelected)
            {
                item.TargetNewDate = BatchTargetDate;
                appliedCount++;
            }
        }

        UpdateCounters();
    }

    [RelayCommand]
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }

    [RelayCommand]
    public void ContinueToPreview()
    {
        RequestNavigateToPreview?.Invoke();
    }

    #endregion

    partial void OnSelectedDateItemChanged(DateItemViewModel? value)
    {
        RefreshPreview();
    }

    private void EnsureVisiblePreviewSelection()
    {
        var visibleItems = FilteredDateItemsView.Cast<DateItemViewModel>().ToList();
        if (SelectedDateItem == null || !visibleItems.Contains(SelectedDateItem))
        {
            var nextSelection = visibleItems.FirstOrDefault();
            if (!ReferenceEquals(SelectedDateItem, nextSelection))
            {
                SelectedDateItem = nextSelection;
            }
            else
            {
                RefreshPreview();
            }
        }
        else
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        var selectedFile = ResolvePreviewFile();
        BuildExcelContext();
        OnPropertyChanged(nameof(IsSpreadsheetPreview));
        PreviewSegments.Clear();

        if (selectedFile == null)
        {
            PreviewFileName = "Chưa chọn file";
            PreviewLocation = "Không có nội dung đã quét để xem trước";
            OnPropertyChanged(nameof(HasPreviewContent));
            PreviewRevision++;
            return;
        }

        PreviewFileName = selectedFile.FileName;
        PreviewLocation = SelectedDateItem != null &&
                          string.Equals(SelectedDateItem.FilePath, selectedFile.FilePath, StringComparison.OrdinalIgnoreCase)
            ? SelectedDateItem.SourceLocation
            : "Toàn bộ nội dung text đã quét";

        IEnumerable<DocumentTextSegment> segments = selectedFile.Segments;
        if (!segments.Any())
        {
            segments = DateItems
                .Where(x => string.Equals(x.FilePath, selectedFile.FilePath, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Segment)
                .Where(x => x != null)
                .Cast<DocumentTextSegment>()
                .DistinctBy(x => x.Id);
        }

        foreach (var segment in segments)
        {
            bool isTarget = ReferenceEquals(SelectedDateItem?.Segment, segment) ||
                            (!string.IsNullOrEmpty(segment.Id) && SelectedDateItem?.Segment?.Id == segment.Id);
            int highlightStart = isTarget ? SelectedDateItem!.Candidate.StartIndex : -1;
            int highlightLength = isTarget ? SelectedDateItem!.Candidate.Length : 0;

            if (isTarget && highlightLength <= 0)
            {
                highlightLength = SelectedDateItem!.RawText.Length;
            }

            PreviewSegments.Add(new PreviewTextSegment(
                segment.Id,
                GetStoryLabel(segment.StoryType),
                segment.SourceLocation,
                segment.LogicalText,
                highlightStart,
                highlightLength));
        }

        OnPropertyChanged(nameof(HasPreviewContent));
        PreviewRevision++;
    }

    private void BuildExcelContext()
    {
        var table = new DataTable();
        var target = SelectedDateItem?.Segment;
        if (IsSpreadsheetPreview && target != null && !string.IsNullOrEmpty(target.CellAddress))
        {
            static (int Column, int Row) ParseAddress(string address)
            {
                int column = 0, index = 0;
                while (index < address.Length && char.IsLetter(address[index]))
                    column = column * 26 + char.ToUpperInvariant(address[index++]) - 'A' + 1;
                return (column, int.TryParse(address[index..], out int row) ? row : 1);
            }
            static string ColumnName(int value)
            {
                string name = "";
                while (value > 0) { value--; name = (char)('A' + value % 26) + name; value /= 26; }
                return name;
            }
            var center = ParseAddress(target.CellAddress);
            int firstColumn = Math.Max(1, center.Column - 2);
            int lastColumn = Math.Min(16384, firstColumn + 4);
            int firstRow = Math.Max(1, center.Row - 3);
            int lastRow = Math.Min(1048576, firstRow + 6);
            table.Columns.Add("Hàng");
            for (int col = firstColumn; col <= lastColumn; col++) table.Columns.Add(ColumnName(col));
            var cells = SelectedDateItem!.FileItem.Segments
                .Where(s => s.WorksheetName == target.WorksheetName && !string.IsNullOrEmpty(s.CellAddress))
                .GroupBy(s => s.CellAddress).ToDictionary(g => g.Key!, g => g.First().LogicalText);
            for (int row = firstRow; row <= lastRow; row++)
            {
                var values = new object[lastColumn - firstColumn + 2];
                values[0] = row.ToString();
                for (int col = firstColumn; col <= lastColumn; col++)
                {
                    string address = ColumnName(col) + row;
                    cells.TryGetValue(address, out string? text);
                    values[col - firstColumn + 1] = (address == target.CellAddress ? "▶ " : "") + (text ?? "");
                }
                table.Rows.Add(values);
            }
        }
        ExcelContextRows = table.DefaultView;
        OnPropertyChanged(nameof(ExcelContextRows));
    }
    private ContractFileItem? ResolvePreviewFile()
    {
        if (!string.IsNullOrWhiteSpace(SelectedFileFilter) &&
            !string.Equals(SelectedFileFilter, AllFilesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return _fileFilterPathByLabel.TryGetValue(SelectedFileFilter, out string? selectedPath)
                ? _previewFiles.FirstOrDefault(x => string.Equals(x.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                : null;
        }

        return SelectedDateItem?.FileItem ?? _previewFiles.FirstOrDefault();
    }

    private static string GetStoryLabel(StoryType storyType) => storyType switch
    {
        StoryType.MainDocument => "Nội dung chính",
        StoryType.Table => "Bảng",
        StoryType.Header => "Đầu trang",
        StoryType.Footer => "Chân trang",
        StoryType.TextBox => "Hộp văn bản",
        StoryType.Footnote => "Chú thích cuối trang",
        StoryType.Endnote => "Chú thích cuối văn bản",
        StoryType.ContentControl => "Trường nội dung",
        StoryType.SpreadsheetCell => "Ô Excel",
        _ => "Vùng khác"
    };

    #region Counters & Properties

    public int TotalDatesCount => DateItems.Count;
    public int SelectedDatesCount => DateItems.Count(d => d.IsSelected);
    public int SensitiveWarningsCount => DateItems.Count(d => d.HasSensitiveWarning);
    public int ReviewRequiredCount => DateItems.Count(d => d.IsReviewRequired);

    public int SignAndEffectiveCount => DateItems.Count(d => d.ClassificationType is DateClassificationType.ContractSignDate 
                                                                                  or DateClassificationType.ContractEffectiveDate
                                                                                  or DateClassificationType.ContractStartDate
                                                                                  or DateClassificationType.ContractEndDate);

    public int PaymentAndHandoverCount => DateItems.Count(d => d.ClassificationType is DateClassificationType.PaymentDate 
                                                                                    or DateClassificationType.HandoverDate
                                                                                    or DateClassificationType.AppendixDate);

    public int PersonalCount => DateItems.Count(d => d.IsSensitivePersonalData);

    public bool CanContinueToPreview
    {
        get
        {
            var selectedItems = DateItems.Where(d => d.IsSelected).ToList();
            return selectedItems.Count > 0 && selectedItems.All(d => d.CanReplace && d.TargetNewDate.HasValue) && selectedItems.Any(d => d.HasEffectiveChange);
        }
    }

    public void UpdateCounters()
    {
        OnPropertyChanged(nameof(TotalDatesCount));
        OnPropertyChanged(nameof(SelectedDatesCount));
        OnPropertyChanged(nameof(SensitiveWarningsCount));
        OnPropertyChanged(nameof(ReviewRequiredCount));
        OnPropertyChanged(nameof(SignAndEffectiveCount));
        OnPropertyChanged(nameof(PaymentAndHandoverCount));
        OnPropertyChanged(nameof(PersonalCount));
        OnPropertyChanged(nameof(HasDateItems));
        OnPropertyChanged(nameof(HasSensitiveWarnings));
        OnPropertyChanged(nameof(CanContinueToPreview));
        OnPropertyChanged(nameof(ReadinessMessage));
        OnPropertyChanged(nameof(ContinueButtonText));

        UpdateCurrentFileInfoText();
    }

    private void UpdateCurrentFileInfoText()
    {
        if (string.IsNullOrWhiteSpace(SelectedFileFilter) ||
            string.Equals(SelectedFileFilter, AllFilesFilter, StringComparison.OrdinalIgnoreCase))
        {
            CurrentFileInfoText = $"TẤT CẢ FILE: {DateItems.Count} ngày được phát hiện ({SelectedDatesCount} ngày được chọn thay)";
        }
        else
        {
            _fileFilterPathByLabel.TryGetValue(SelectedFileFilter, out string? selectedPath);
            int fileDates = DateItems.Count(d => string.Equals(d.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
            int fileSelected = DateItems.Count(d => string.Equals(d.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase) && d.IsSelected);
            CurrentFileInfoText = $"FILE: {SelectedFileFilter} ({fileDates} ngày phát hiện • {fileSelected} ngày chọn thay)";
        }
    }

    #endregion
}

public sealed record PreviewTextSegment(
    string SegmentId,
    string StoryLabel,
    string SourceLocation,
    string Text,
    int HighlightStart,
    int HighlightLength);




using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Preview Changes (Diff) theo Master Spec Phase 7.
/// Hỗ trợ chuyển đổi 2 chế độ Side-by-side vs Inline Diff, lọc dòng thay đổi,
/// và bắt buộc người dùng xác nhận checkbox trước khi bấm thực hiện thay thế.
/// </summary>
public partial class PreviewViewModel : ObservableObject
{
    private const string AllFilesFilter = "Tất cả các file";
    private readonly Dictionary<string, string> _fileFilterPathByLabel = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<DiffItemViewModel> DiffItems { get; } = new();
    public ICollectionView FilteredDiffItemsView { get; }

    public ObservableCollection<string> FileFilterOptions { get; } = new();

    [ObservableProperty]
    private string? _selectedFileFilter = "Tất cả các file";

    [ObservableProperty]
    private bool _isSideBySideMode = true;

    [ObservableProperty]
    private bool _isInlineMode = false;

    [ObservableProperty]
    private bool _onlyShowChanged = true;

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private bool _isConfirmedByUser = false;

    [ObservableProperty]
    private string _summaryStatsText = string.Empty;

    public event Action? RequestNavigateBack;
    public event Action? RequestExecuteReplacement;

    public PreviewViewModel()
    {
        FilteredDiffItemsView = CollectionViewSource.GetDefaultView(DiffItems);
        FilteredDiffItemsView.Filter = FilterDiffItem;

        FileFilterOptions.Add("Tất cả các file");
    }

    public void LoadDiffItems(IEnumerable<DateItemViewModel> dateItems)
    {
        DiffItems.Clear();
        _fileFilterPathByLabel.Clear();
        FileFilterOptions.Clear();
        FileFilterOptions.Add(AllFilesFilter);

        var items = dateItems.ToList();
        foreach (var fileGroup in items.GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
        {
            bool needsPath = fileGroup.Select(x => x.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            foreach (var item in fileGroup.DistinctBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                string label = needsPath ? $"{item.FileName} — {item.FileItem.DirectoryPath}" : item.FileName;
                FileFilterOptions.Add(label);
                _fileFilterPathByLabel[label] = item.FilePath;
            }
        }

        SelectedFileFilter = AllFilesFilter;
        OnlyShowChanged = true;
        SearchKeyword = string.Empty;
        IsConfirmedByUser = false;

        foreach (var item in items)
        {
            var diffVm = new DiffItemViewModel(item);
            DiffItems.Add(diffVm);
        }

        UpdateCounters();
        FilteredDiffItemsView.Refresh();
        OnPropertyChanged(nameof(HasDiffItems));
    }

    partial void OnSelectedFileFilterChanged(string? value)
    {
        FilteredDiffItemsView.Refresh();
        OnPropertyChanged(nameof(HasDiffItems));
    }

    partial void OnOnlyShowChangedChanged(bool value)
    {
        FilteredDiffItemsView.Refresh();
        OnPropertyChanged(nameof(HasDiffItems));
    }

    partial void OnSearchKeywordChanged(string value)
    {
        FilteredDiffItemsView.Refresh();
        OnPropertyChanged(nameof(HasDiffItems));
    }

    partial void OnIsConfirmedByUserChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExecuteReplacement));
    }

    private bool FilterDiffItem(object obj)
    {
        if (obj is not DiffItemViewModel item) return false;

        // 1. Lọc theo chỉ xem dòng thay đổi
        if (OnlyShowChanged && !item.IsChanged)
        {
            return false;
        }

        // 2. Lọc theo file
        if (!string.IsNullOrWhiteSpace(SelectedFileFilter) &&
            !string.Equals(SelectedFileFilter, AllFilesFilter, StringComparison.OrdinalIgnoreCase))
        {
            if (!_fileFilterPathByLabel.TryGetValue(SelectedFileFilter, out string? selectedPath) ||
                !string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 3. Lọc theo từ khóa tìm kiếm
        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            string kw = SearchKeyword.Trim();
            bool matches = item.OldDateText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                           item.NewDateText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                           item.FileName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                           item.SideBySideBeforeDisplay.Contains(kw, StringComparison.OrdinalIgnoreCase);

            if (!matches) return false;
        }

        return true;
    }

    #region View Mode Switching

    [RelayCommand]
    public void SetSideBySideMode()
    {
        IsSideBySideMode = true;
        IsInlineMode = false;
    }

    [RelayCommand]
    public void SetInlineMode()
    {
        IsSideBySideMode = false;
        IsInlineMode = true;
    }

    #endregion

    #region Navigation Commands

    [RelayCommand]
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }

    [RelayCommand]
    public void ExecuteReplacement()
    {
        if (CanExecuteReplacement)
        {
            RequestExecuteReplacement?.Invoke();
        }
    }

    #endregion

    #region Statistics & Safety

    public int TotalDiffsCount => DiffItems.Count;
    public int ChangedDiffsCount => DiffItems.Count(d => d.IsChanged);
    public int FilesAffectedCount => DiffItems.Where(d => d.IsChanged).Select(d => d.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public bool HasDiffItems => FilteredDiffItemsView.Cast<DiffItemViewModel>().Any();

    public bool CanExecuteReplacement => IsConfirmedByUser && ChangedDiffsCount > 0;

    private void UpdateCounters()
    {
        OnPropertyChanged(nameof(TotalDiffsCount));
        OnPropertyChanged(nameof(ChangedDiffsCount));
        OnPropertyChanged(nameof(FilesAffectedCount));
        OnPropertyChanged(nameof(HasDiffItems));
        OnPropertyChanged(nameof(CanExecuteReplacement));

        SummaryStatsText = $"{FilesAffectedCount} tệp hợp đồng • {ChangedDiffsCount} vị trí ngày sẽ được thay thế (tổng số {TotalDiffsCount} ngày phát hiện)";
    }

    #endregion
}

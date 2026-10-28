using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý báo cáo chi tiết của từng tệp hợp đồng dạng collapsible / expander.
/// </summary>
public partial class ReportFileDetailViewModel : ObservableObject
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string OutputFilePath { get; set; } = string.Empty;

    [ObservableProperty]
    private int _safetyScore = 100;

    [ObservableProperty]
    private string _statusBadgeText = "PASS";

    [ObservableProperty]
    private string _statusBadgeAppearance = "Success";

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<string> Issues { get; } = new();
    public ObservableCollection<ReportDateDetailItem> ReplacedItems { get; } = new();

    public string HeaderDisplay => $"{FileName} (Safety Score: {SafetyScore}/100)";
}

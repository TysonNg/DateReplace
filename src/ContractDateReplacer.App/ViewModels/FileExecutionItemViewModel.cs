using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ContractDateReplacer.Core.Models;
using Wpf.Ui.Controls;

namespace ContractDateReplacer.App.ViewModels;

public enum ExecutionItemStatus
{
    Pending,
    Processing,
    Success,
    Failed,
    Cancelled
}

/// <summary>
/// ViewModel quản lý tình trạng thực thi và kết quả kiểm tra từng file trong quá trình chạy.
/// </summary>
public partial class FileExecutionItemViewModel : ObservableObject
{
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string OutputFilePath { get; set; } = string.Empty;

    public int TotalOccurrencesCount { get; }

    [ObservableProperty]
    private ExecutionItemStatus _status = ExecutionItemStatus.Pending;

    [ObservableProperty]
    private string _statusText = "Đang chờ...";

    [ObservableProperty]
    private int _replacedCount = 0;

    [ObservableProperty]
    private VerificationResult? _verificationResult;

    [ObservableProperty]
    private string _backupFilePath = string.Empty;

    public FileExecutionItemViewModel(string filePath, int occurrencesCount)
    {
        FilePath = filePath;
        TotalOccurrencesCount = occurrencesCount;
    }

    partial void OnStatusChanged(ExecutionItemStatus value)
    {
        OnPropertyChanged(nameof(StatusAppearance));
        OnPropertyChanged(nameof(StatusIcon));
    }

    public string StatusAppearance => Status switch
    {
        ExecutionItemStatus.Pending => "Secondary",
        ExecutionItemStatus.Processing => "Info",
        ExecutionItemStatus.Success => "Success",
        ExecutionItemStatus.Failed => "Danger",
        ExecutionItemStatus.Cancelled => "Caution",
        _ => "Secondary"
    };

    public SymbolRegular StatusIcon => Status switch
    {
        ExecutionItemStatus.Pending => SymbolRegular.Clock24,
        ExecutionItemStatus.Processing => SymbolRegular.ArrowClockwise24,
        ExecutionItemStatus.Success => SymbolRegular.Checkmark24,
        ExecutionItemStatus.Failed => SymbolRegular.Dismiss24,
        ExecutionItemStatus.Cancelled => SymbolRegular.Warning24,
        _ => SymbolRegular.Document24
    };
}

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.App.Services;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

public partial class AttendanceCalendarViewModel : ObservableObject
{
    private readonly IAttendanceCalendarService _service;
    private readonly IBackupRollbackService _backup;
    private readonly IAttendanceFileDialogService _dialogs;
    private readonly ILogger<AttendanceCalendarViewModel> _logger;
    private AttendancePreview? _preview;
    public ObservableCollection<AttendanceTable> Tables { get; } = new();
    public ObservableCollection<AttendanceDay> PreviewDays { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();
    public int[] Months { get; } = Enumerable.Range(1, 12).ToArray();
    public int[] Years { get; } = Enumerable.Range(1900, 201).ToArray();
    [ObservableProperty] private string _sourcePath = "";
    [ObservableProperty] private AttendanceTable? _selectedTable;
    [ObservableProperty] private int _selectedMonth = DateTime.Today.Month;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Chọn tệp Excel có bảng chấm công để bắt đầu.";
    [ObservableProperty] private string _periodText = "";
    [ObservableProperty] private string _outputPath = "";
    public bool CanEdit => !IsBusy;
    public bool CanPreview => !IsBusy && SelectedTable != null;
    public bool CanExport => !IsBusy && _preview?.CanExport == true;
    public bool CanOpenOutput => !IsBusy && File.Exists(OutputPath);
    public event Action? RequestNavigateBack;

    public AttendanceCalendarViewModel(IAttendanceCalendarService service, IBackupRollbackService backup,
        IAttendanceFileDialogService dialogs, ILogger<AttendanceCalendarViewModel> logger)
    { _service = service; _backup = backup; _dialogs = dialogs; _logger = logger; }

    partial void OnSelectedMonthChanged(int value) => InvalidatePreview();
    partial void OnSelectedYearChanged(int value) => InvalidatePreview();
    partial void OnSelectedTableChanged(AttendanceTable? value)
    {
        if (value != null) { SelectedMonth = value.SourceMonth; SelectedYear = value.SourceYear; }
        InvalidatePreview(); NotifyCommands();
    }
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnOutputPathChanged(string value) => NotifyCommands();
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanPreview)); OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(CanOpenOutput));
        ChooseFileCommand.NotifyCanExecuteChanged(); BuildPreviewCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged();
        NavigateBackCommand.NotifyCanExecuteChanged(); OpenOutputFolderCommand.NotifyCanExecuteChanged();
    }
    private void InvalidatePreview()
    {
        _preview = null; PreviewDays.Clear(); Errors.Clear(); PeriodText = "";
        if (SelectedTable != null) StatusMessage = "Chọn tháng/năm rồi bấm Xem trước. Công được giữ theo số ngày; Chủ nhật được tô xanh.";
        NotifyCommands();
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ChooseFileAsync()
    {
        string? path = _dialogs.OpenWorkbook(); if (path != null) await LoadFileAsync(path);
    }
    public async Task LoadFileAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true; SelectedTable = null; Tables.Clear(); InvalidatePreview(); SourcePath = path; OutputPath = "";
        try
        {
            var analysis = await Task.Run(() => _service.Analyze(path));
            foreach (var table in analysis.Tables) Tables.Add(table);
            SelectedTable = Tables.FirstOrDefault();
            if (analysis.Errors.Count > 0) { foreach (var error in analysis.Errors) Errors.Add(error); StatusMessage = string.Join(Environment.NewLine, analysis.Errors); }
            else StatusMessage = $"Đã nhận diện {Tables.Count} bảng. Chọn tháng/năm và bấm Xem trước.";
        }
        catch (Exception ex) { StatusMessage = "Không đọc được bảng chấm công: " + ex.Message; _logger.LogWarning(ex, "Attendance analysis failed"); }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanPreview))]
    public async Task BuildPreviewAsync()
    {
        if (!CanPreview) return;
        var table = SelectedTable!; string path = SourcePath; int year = SelectedYear, month = SelectedMonth;
        IsBusy = true; _preview = null; Errors.Clear(); PreviewDays.Clear(); PeriodText = ""; OutputPath = "";
        try
        {
            var preview = await Task.Run(() => _service.Preview(path, table.Id, year, month));
            _preview = preview; PeriodText = preview.PeriodText;
            foreach (var day in preview.Days) PreviewDays.Add(day);
            foreach (var error in preview.Errors) Errors.Add(error);
            StatusMessage = preview.CanExport ? $"Lịch {month:00}/{year}: {preview.Days.Count} ngày, {preview.Days.Count(d => d.IsSunday)} Chủ nhật. Giữ nguyên công theo số ngày. Sẵn sàng xuất bản sao."
                : string.Join(Environment.NewLine, preview.Errors);
        }
        catch (Exception ex) { StatusMessage = "Không tạo được bản xem trước: " + ex.Message; _logger.LogWarning(ex, "Attendance preview failed"); }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanExport))]
    public async Task ExportAsync()
    {
        if (!CanExport) return;
        var preview = _preview!; var table = preview.Table!; string source = SourcePath;
        string directory = Path.GetDirectoryName(Path.GetFullPath(source))!;
        string stem = $"{Path.GetFileNameWithoutExtension(source)}_{preview.Month:00}_{preview.Year}";
        string suggested = Path.Combine(directory, stem + ".xlsx"); int suffix = 2;
        while (File.Exists(suggested)) suggested = Path.Combine(directory, $"{stem}_{suffix++}.xlsx");
        string? destination = _dialogs.SaveWorkbook(suggested); if (destination == null) return;
        IsBusy = true; OutputPath = ""; BackupRecord? record = null;
        try
        {
            // Backup failure must stop export; the history must always have an intact original.
            record = await _backup.CreateBackupAsync($"CALENDAR_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}", source, destination, preview.Days.Count);
            await Task.Run(() => _service.Export(source, destination, table.Id, preview.Year, preview.Month, preview.SourceChecksum));
            OutputPath = destination;
            StatusMessage = $"Đã xuất và xác minh: {destination}. Bản gốc được giữ nguyên; đã lưu lịch sử sao lưu.";
        }
        catch (Exception ex)
        {
            if (record != null)
            {
                try { await _backup.DeleteBackupAsync(record.BackupId); }
                catch (Exception cleanup) { _logger.LogWarning(cleanup, "Cannot remove failed attendance export backup {Id}", record.BackupId); }
            }
            InvalidatePreview(); StatusMessage = "Không xuất được: " + ex.Message;
            _logger.LogError(ex, "Attendance export failed");
        }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanOpenOutput))]
    private void OpenOutputFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(OutputPath)}\"") { UseShellExecute = true }); }
        catch (Exception ex) { StatusMessage = "Không mở được thư mục: " + ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    public void NavigateBack() { if (!IsBusy) RequestNavigateBack?.Invoke(); }
}

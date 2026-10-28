using System.IO;
using Microsoft.Win32;
namespace ContractDateReplacer.App.Services;
public sealed class AttendanceFileDialogService : IAttendanceFileDialogService
{
    public string? OpenWorkbook()
    {
        var dialog = new OpenFileDialog { Title = "Chọn bảng chấm công Excel", Filter = "Excel (*.xlsx)|*.xlsx", CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public string? SaveWorkbook(string suggestedPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất bảng chấm công mới", Filter = "Excel (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true,
            InitialDirectory = Path.GetDirectoryName(suggestedPath), FileName = Path.GetFileName(suggestedPath), OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

using Microsoft.Win32;

namespace ContractDateReplacer.App.Services;

public class FileDialogService : IFileDialogService
{
    public string[]? ShowWordFilesDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Thêm tài liệu Word hoặc Excel",
            Filter = "Word và Excel (*.doc;*.docx;*.xlsx)|*.doc;*.docx;*.xlsx|Word (*.doc;*.docx)|*.doc;*.docx|Excel (*.xlsx)|*.xlsx",
            Multiselect = true,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    public string? ShowFolderDialog()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục chứa tài liệu Word và Excel",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}


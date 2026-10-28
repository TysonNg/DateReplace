namespace ContractDateReplacer.App.Services;

public interface IFileDialogService
{
    string[]? ShowWordFilesDialog();
    string? ShowFolderDialog();
}

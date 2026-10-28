namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Quản lý đường dẫn thư mục ứng dụng chuẩn Windows (%LocalAppData%\ContractDateReplacer\).
/// </summary>
public interface IAppDataService
{
    string RootDirectory { get; }
    string LogsDirectory { get; }
    string TempDirectory { get; }
    string DatabasePath { get; }
    string SettingsFilePath { get; }

    void EnsureDirectoriesExist();
    string GetUniqueOutputFilePath(string targetDirectory, string originalFileName);
}

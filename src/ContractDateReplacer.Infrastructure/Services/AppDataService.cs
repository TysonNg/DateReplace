using System;
using System.IO;
using ContractDateReplacer.Core.Interfaces;

namespace ContractDateReplacer.Infrastructure.Services;

/// <summary>
/// Quản lý đường dẫn và thư mục hệ thống chuẩn Windows (%LocalAppData%\ContractDateReplacer\).
/// </summary>
public class AppDataService : IAppDataService
{
    private const string AppFolderName = "ContractDateReplacer";

    public string RootDirectory { get; }
    public string LogsDirectory { get; }
    public string TempDirectory { get; }
    public string DatabasePath { get; }
    public string SettingsFilePath { get; }

    public AppDataService(string? customBasePath = null)
    {
        string localAppData = customBasePath ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootDirectory = Path.Combine(localAppData, AppFolderName);
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        TempDirectory = Path.Combine(RootDirectory, "temp");
        DatabasePath = Path.Combine(RootDirectory, "ContractDateReplacer.db");
        SettingsFilePath = Path.Combine(RootDirectory, "settings.json");

        EnsureDirectoriesExist();
    }

    public void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(RootDirectory))
        {
            Directory.CreateDirectory(RootDirectory);
        }

        if (!Directory.Exists(LogsDirectory))
        {
            Directory.CreateDirectory(LogsDirectory);
        }

        if (!Directory.Exists(TempDirectory))
        {
            Directory.CreateDirectory(TempDirectory);
        }
    }

    public string GetUniqueOutputFilePath(string targetDirectory, string originalFileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(originalFileName);
        string extension = Path.GetExtension(originalFileName);
        string destinationPath = Path.Combine(targetDirectory, originalFileName);

        int counter = 1;
        while (File.Exists(destinationPath))
        {
            string newName = $"{baseName}_{counter}{extension}";
            destinationPath = Path.Combine(targetDirectory, newName);
            counter++;
        }

        return destinationPath;
    }
}

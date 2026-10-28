using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.Infrastructure.Services;

/// <summary>
/// Triển khai dịch vụ lưu trữ và quản lý cấu hình hệ thống (Settings Service).
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly IAppDataService _appDataService;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private AppSettings _currentSettings = new();

    public AppSettings CurrentSettings => _currentSettings;

    public event Action<AppSettings>? SettingsChanged;

    public SettingsService(IAppDataService appDataService, ILogger<SettingsService> logger)
    {
        _appDataService = appDataService;
        _logger = logger;
    }

    private string GetSettingsFilePath()
    {
        _appDataService.EnsureDirectoriesExist();
        return Path.Combine(_appDataService.RootDirectory, "settings.json");
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            string filePath = GetSettingsFilePath();

            if (!File.Exists(filePath))
            {
                _currentSettings = new AppSettings();
                _currentSettings.ValidateAndNormalize();

                string defaultJson = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, defaultJson, Encoding.UTF8, cancellationToken);
                _logger.LogInformation("Khởi tạo tệp cấu hình mặc định tại {Path}", filePath);
                return _currentSettings;
            }

            string json = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);

            if (loaded == null)
            {
                loaded = new AppSettings();
            }

            loaded.ValidateAndNormalize();
            _currentSettings = loaded;
            _logger.LogInformation("Đã tải cấu hình hệ thống thành công.");
            return _currentSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải tệp cấu hình, sử dụng cấu hình mặc định.");
            _currentSettings = new AppSettings();
            return _currentSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            settings.ValidateAndNormalize();
            string filePath = GetSettingsFilePath();

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, cancellationToken);

            _currentSettings = settings.Clone();
            _logger.LogInformation("Đã lưu cấu hình hệ thống tại {Path}", filePath);

            SettingsChanged?.Invoke(_currentSettings);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AppSettings> ResetToDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var defaultSettings = new AppSettings();
        await SaveSettingsAsync(defaultSettings, cancellationToken);
        _logger.LogInformation("Đã khôi phục toàn bộ cấu hình về mặc định ban đầu.");
        return defaultSettings;
    }
}

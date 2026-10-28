using System;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Quản lý lưu trữ, truy xuất và theo dõi sự thay đổi cấu hình hệ thống.
/// </summary>
public interface ISettingsService
{
    AppSettings CurrentSettings { get; }

    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task<AppSettings> ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    event Action<AppSettings>? SettingsChanged;
}

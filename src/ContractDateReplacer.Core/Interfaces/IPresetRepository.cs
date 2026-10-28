using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Quản lý truy xuất và lưu trữ các mẫu thiết lập (Presets) trong cơ sở dữ liệu.
/// </summary>
public interface IPresetRepository
{
    Task<List<PresetModel>> GetAllPresetsAsync(CancellationToken cancellationToken = default);
    Task<PresetModel?> GetPresetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> SavePresetAsync(PresetModel preset, CancellationToken cancellationToken = default);
    Task<bool> DeletePresetAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> SetDefaultPresetAsync(string id, CancellationToken cancellationToken = default);
    Task<PresetModel?> GetDefaultPresetAsync(CancellationToken cancellationToken = default);
    Task EnsureSeedPresetsAsync(CancellationToken cancellationToken = default);
}

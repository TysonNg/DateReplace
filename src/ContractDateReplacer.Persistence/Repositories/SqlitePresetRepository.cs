using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.Persistence.Repositories;

/// <summary>
/// Quản lý lưu trữ mẫu thiết lập (Presets / Templates) trong SQLite.
/// </summary>
public class SqlitePresetRepository : IPresetRepository
{
    private readonly IAppDataService _appDataService;
    private readonly ILogger<SqlitePresetRepository> _logger;
    private readonly string _connectionString;

    public SqlitePresetRepository(IAppDataService appDataService, ILogger<SqlitePresetRepository> logger)
    {
        _appDataService = appDataService;
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appDataService.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task<List<PresetModel>> GetAllPresetsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string query = @"
            SELECT PresetId, Name, Description, CreatedAt, ConfigJson, OutputMode, SuffixPattern, CustomFolder, IsDefault, IsBuiltIn
            FROM Presets
            ORDER BY IsDefault DESC, IsBuiltIn DESC, Name ASC
        ";

        await using var command = new SqliteCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<PresetModel>();
        while (await reader.ReadAsync(cancellationToken))
        {
            string configJson = reader.GetString(4);
            var rules = JsonSerializer.Deserialize<List<DateOffsetRule>>(configJson) ?? new List<DateOffsetRule>();

            list.Add(new PresetModel
            {
                PresetId = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                CreatedAt = DateTime.Parse(reader.GetString(3)),
                DateMappingRules = rules,
                OutputMode = reader.GetString(5),
                SuffixPattern = reader.GetString(6),
                CustomFolder = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                IsDefault = reader.GetInt32(8) == 1,
                IsBuiltIn = reader.GetInt32(9) == 1
            });
        }

        return list;
    }

    public async Task<PresetModel?> GetPresetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string query = @"
            SELECT PresetId, Name, Description, CreatedAt, ConfigJson, OutputMode, SuffixPattern, CustomFolder, IsDefault, IsBuiltIn
            FROM Presets
            WHERE PresetId = @Id
        ";

        await using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            string configJson = reader.GetString(4);
            var rules = JsonSerializer.Deserialize<List<DateOffsetRule>>(configJson) ?? new List<DateOffsetRule>();

            return new PresetModel
            {
                PresetId = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                CreatedAt = DateTime.Parse(reader.GetString(3)),
                DateMappingRules = rules,
                OutputMode = reader.GetString(5),
                SuffixPattern = reader.GetString(6),
                CustomFolder = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                IsDefault = reader.GetInt32(8) == 1,
                IsBuiltIn = reader.GetInt32(9) == 1
            };
        }

        return null;
    }

    public async Task<bool> SavePresetAsync(PresetModel preset, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (preset.IsDefault)
        {
            // Reset các preset khác không còn là mặc định
            string resetDefaultSql = "UPDATE Presets SET IsDefault = 0;";
            await using var resetCmd = new SqliteCommand(resetDefaultSql, connection);
            await resetCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        string configJson = JsonSerializer.Serialize(preset.DateMappingRules);

        string sql = @"
            INSERT INTO Presets (PresetId, Name, Description, CreatedAt, ConfigJson, OutputMode, SuffixPattern, CustomFolder, IsDefault, IsBuiltIn)
            VALUES (@PresetId, @Name, @Description, @CreatedAt, @ConfigJson, @OutputMode, @SuffixPattern, @CustomFolder, @IsDefault, @IsBuiltIn)
            ON CONFLICT(PresetId) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                ConfigJson = excluded.ConfigJson,
                OutputMode = excluded.OutputMode,
                SuffixPattern = excluded.SuffixPattern,
                CustomFolder = excluded.CustomFolder,
                IsDefault = excluded.IsDefault;
        ";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@PresetId", preset.PresetId);
        command.Parameters.AddWithValue("@Name", preset.Name);
        command.Parameters.AddWithValue("@Description", preset.Description ?? string.Empty);
        command.Parameters.AddWithValue("@CreatedAt", preset.CreatedAt.ToString("o"));
        command.Parameters.AddWithValue("@ConfigJson", configJson);
        command.Parameters.AddWithValue("@OutputMode", preset.OutputMode ?? "Suffix");
        command.Parameters.AddWithValue("@SuffixPattern", preset.SuffixPattern ?? "_2026");
        command.Parameters.AddWithValue("@CustomFolder", preset.CustomFolder ?? string.Empty);
        command.Parameters.AddWithValue("@IsDefault", preset.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("@IsBuiltIn", preset.IsBuiltIn ? 1 : 0);

        int rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> DeletePresetAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await GetPresetByIdAsync(id, cancellationToken);
        if (existing == null) return false;

        if (existing.IsBuiltIn)
        {
            throw new InvalidOperationException("Không thể xóa mẫu thiết lập mặc định (Built-in) của hệ thống!");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = "DELETE FROM Presets WHERE PresetId = @Id AND IsBuiltIn = 0;";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<bool> SetDefaultPresetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = @"
            UPDATE Presets
            SET IsDefault = CASE WHEN PresetId = @Id THEN 1 ELSE 0 END;
        ";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<PresetModel?> GetDefaultPresetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string query = @"
            SELECT PresetId, Name, Description, CreatedAt, ConfigJson, OutputMode, SuffixPattern, CustomFolder, IsDefault, IsBuiltIn
            FROM Presets
            ORDER BY IsDefault DESC, IsBuiltIn DESC
            LIMIT 1;
        ";

        await using var command = new SqliteCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            string configJson = reader.GetString(4);
            var rules = JsonSerializer.Deserialize<List<DateOffsetRule>>(configJson) ?? new List<DateOffsetRule>();

            return new PresetModel
            {
                PresetId = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                CreatedAt = DateTime.Parse(reader.GetString(3)),
                DateMappingRules = rules,
                OutputMode = reader.GetString(5),
                SuffixPattern = reader.GetString(6),
                CustomFolder = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                IsDefault = reader.GetInt32(8) == 1,
                IsBuiltIn = reader.GetInt32(9) == 1
            };
        }

        return null;
    }

    public async Task EnsureSeedPresetsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string countSql = "SELECT COUNT(*) FROM Presets;";
        await using var countCmd = new SqliteCommand(countSql, connection);
        long count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(cancellationToken));

        if (count > 0) return;

        var defaults = GetDefaultPresets();
        foreach (var p in defaults)
        {
            await SavePresetAsync(p, cancellationToken);
        }

        _logger.LogInformation("Đã khởi tạo 3 mẫu thiết lập mặc định (Presets) vào SQLite.");
    }

    public static List<PresetModel> GetDefaultPresets()
    {
        return new List<PresetModel>
        {
            // 1. Gia hạn 1 năm
            new()
            {
                PresetId = "PRESET_GIA_HAN_1_NAM",
                Name = "Gia hạn 1 năm",
                Description = "Ngày ký và ngày bắt đầu đặt thành Hôm nay; Ngày kết thúc tự động cộng 1 năm từ ngày bắt đầu",
                IsBuiltIn = true,
                IsDefault = true,
                OutputMode = "Suffix",
                SuffixPattern = "_GiaHan1Nam",
                DateMappingRules = new List<DateOffsetRule>
                {
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractSignDate,
                        Mode = DateOffsetMode.CurrentDate
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractEffectiveDate,
                        Mode = DateOffsetMode.CurrentDate
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractStartDate,
                        Mode = DateOffsetMode.CurrentDate
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractEndDate,
                        Mode = DateOffsetMode.OffsetFromAnotherDate,
                        BaseDateType = DateClassificationType.ContractStartDate,
                        OffsetYears = 1
                    }
                }
            },

            // 2. Đổi ngày sang năm mới
            new()
            {
                PresetId = "PRESET_SANG_NAM_MOI",
                Name = "Đổi ngày sang năm mới",
                Description = "Tất cả các mốc ngày trong hợp đồng đều được cộng dồn sang năm tiếp theo (+1 năm)",
                IsBuiltIn = true,
                IsDefault = false,
                OutputMode = "Suffix",
                SuffixPattern = "_NamMoi",
                DateMappingRules = new List<DateOffsetRule>
                {
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractSignDate,
                        Mode = DateOffsetMode.OffsetYearsFromSelf,
                        OffsetYears = 1
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractEffectiveDate,
                        Mode = DateOffsetMode.OffsetYearsFromSelf,
                        OffsetYears = 1
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractStartDate,
                        Mode = DateOffsetMode.OffsetYearsFromSelf,
                        OffsetYears = 1
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractEndDate,
                        Mode = DateOffsetMode.OffsetYearsFromSelf,
                        OffsetYears = 1
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.PaymentDate,
                        Mode = DateOffsetMode.OffsetYearsFromSelf,
                        OffsetYears = 1
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.HandoverDate,
                        Mode = DateOffsetMode.OffsetYearsFromSelf,
                        OffsetYears = 1
                    }
                }
            },

            // 3. Đặt ngày ký hôm nay
            new()
            {
                PresetId = "PRESET_KY_HOM_NAY",
                Name = "Đặt ngày ký hôm nay",
                Description = "Chỉ cập nhật ngày ký hợp đồng thành ngày hiện tại, các ngày khác giữ nguyên",
                IsBuiltIn = true,
                IsDefault = false,
                OutputMode = "Suffix",
                SuffixPattern = "_KyMoi",
                DateMappingRules = new List<DateOffsetRule>
                {
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractSignDate,
                        Mode = DateOffsetMode.CurrentDate
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractEffectiveDate,
                        Mode = DateOffsetMode.CurrentDate
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractStartDate,
                        Mode = DateOffsetMode.KeepOriginal
                    },
                    new()
                    {
                        TargetClassificationType = DateClassificationType.ContractEndDate,
                        Mode = DateOffsetMode.KeepOriginal
                    }
                }
            }
        };
    }
}

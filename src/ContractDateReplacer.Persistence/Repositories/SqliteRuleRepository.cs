using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Data.Sqlite;

namespace ContractDateReplacer.Persistence.Repositories;

/// <summary>
/// Quản lý dữ liệu bộ quy tắc nhận diện ngày trong SQLite.
/// </summary>
public class SqliteRuleRepository : IRuleRepository
{
    private readonly IAppDataService _appDataService;
    private readonly string _connectionString;

    public SqliteRuleRepository(IAppDataService appDataService)
    {
        _appDataService = appDataService;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appDataService.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task<List<ClassificationRule>> GetAllRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string query = "SELECT Id, Name, Keywords, TargetType, Priority, DefaultConfidence, IsEnabled, IsBuiltIn FROM Rules ORDER BY Priority DESC, Id ASC";
        await using var command = new SqliteCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var list = new List<ClassificationRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            string keywordsJson = reader.GetString(2);
            var keywords = JsonSerializer.Deserialize<List<string>>(keywordsJson) ?? new List<string>();

            list.Add(new ClassificationRule
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Keywords = keywords,
                TargetType = (DateClassificationType)reader.GetInt32(3),
                Priority = reader.GetInt32(4),
                DefaultConfidence = reader.GetDouble(5),
                IsEnabled = reader.GetInt32(6) == 1,
                IsBuiltIn = reader.GetInt32(7) == 1
            });
        }

        return list;
    }

    public async Task<ClassificationRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string query = "SELECT Id, Name, Keywords, TargetType, Priority, DefaultConfidence, IsEnabled, IsBuiltIn FROM Rules WHERE Id = @Id";
        await using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            string keywordsJson = reader.GetString(2);
            var keywords = JsonSerializer.Deserialize<List<string>>(keywordsJson) ?? new List<string>();

            return new ClassificationRule
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Keywords = keywords,
                TargetType = (DateClassificationType)reader.GetInt32(3),
                Priority = reader.GetInt32(4),
                DefaultConfidence = reader.GetDouble(5),
                IsEnabled = reader.GetInt32(6) == 1,
                IsBuiltIn = reader.GetInt32(7) == 1
            };
        }

        return null;
    }

    public async Task<int> SaveRuleAsync(ClassificationRule rule, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string keywordsJson = JsonSerializer.Serialize(rule.Keywords);

        if (rule.Id == 0)
        {
            string insertSql = @"
                INSERT INTO Rules (Name, Keywords, TargetType, Priority, DefaultConfidence, IsEnabled, IsBuiltIn)
                VALUES (@Name, @Keywords, @TargetType, @Priority, @DefaultConfidence, @IsEnabled, @IsBuiltIn);
                SELECT last_insert_rowid();
            ";
            await using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("@Name", rule.Name);
            command.Parameters.AddWithValue("@Keywords", keywordsJson);
            command.Parameters.AddWithValue("@TargetType", (int)rule.TargetType);
            command.Parameters.AddWithValue("@Priority", rule.Priority);
            command.Parameters.AddWithValue("@DefaultConfidence", rule.DefaultConfidence);
            command.Parameters.AddWithValue("@IsEnabled", rule.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("@IsBuiltIn", rule.IsBuiltIn ? 1 : 0);

            var newId = await command.ExecuteScalarAsync(cancellationToken);
            rule.Id = Convert.ToInt32(newId);
            return rule.Id;
        }
        else
        {
            string updateSql = @"
                UPDATE Rules
                SET Name = @Name,
                    Keywords = @Keywords,
                    TargetType = @TargetType,
                    Priority = @Priority,
                    DefaultConfidence = @DefaultConfidence,
                    IsEnabled = @IsEnabled
                WHERE Id = @Id;
            ";
            await using var command = new SqliteCommand(updateSql, connection);
            command.Parameters.AddWithValue("@Id", rule.Id);
            command.Parameters.AddWithValue("@Name", rule.Name);
            command.Parameters.AddWithValue("@Keywords", keywordsJson);
            command.Parameters.AddWithValue("@TargetType", (int)rule.TargetType);
            command.Parameters.AddWithValue("@Priority", rule.Priority);
            command.Parameters.AddWithValue("@DefaultConfidence", rule.DefaultConfidence);
            command.Parameters.AddWithValue("@IsEnabled", rule.IsEnabled ? 1 : 0);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return rule.Id;
        }
    }

    public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default)
    {
        var existing = await GetRuleByIdAsync(id, cancellationToken);
        if (existing == null) return false;

        if (existing.IsBuiltIn)
        {
            throw new InvalidOperationException("Không thể xóa quy tắc mặc định (Built-in) của hệ thống!");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = "DELETE FROM Rules WHERE Id = @Id AND IsBuiltIn = 0";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task ResetDefaultRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Xóa toàn bộ rules mặc định hiện tại
        string deleteSql = "DELETE FROM Rules WHERE IsBuiltIn = 1;";
        await using var deleteCmd = new SqliteCommand(deleteSql, connection);
        await deleteCmd.ExecuteNonQueryAsync(cancellationToken);

        // Khôi phục lại 12 rules gốc
        var defaultRules = GetDefaultContractRules();
        foreach (var rule in defaultRules)
        {
            await SaveRuleAsync(rule, cancellationToken);
        }
    }

    public async Task EnsureSeedRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string countSql = "SELECT COUNT(*) FROM Rules";
        await using var countCommand = new SqliteCommand(countSql, connection);
        long count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));

        if (count > 0) return;

        var defaultRules = GetDefaultContractRules();
        foreach (var rule in defaultRules)
        {
            await SaveRuleAsync(rule, cancellationToken);
        }
    }

    public static List<ClassificationRule> GetDefaultContractRules()
    {
        return new List<ClassificationRule>
        {
            new()
            {
                Name = "Ngày sinh cá nhân",
                Keywords = new List<string> { "sinh ngày", "ngày sinh", "sinh lúc", "năm sinh", "sinh:" },
                TargetType = DateClassificationType.BirthDate,
                Priority = 100,
                DefaultConfidence = 0.98,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày cấp CCCD/CMND/Hộ chiếu",
                Keywords = new List<string> { "cấp ngày", "ngày cấp", "công an cấp ngày", "cục cảnh sát cấp ngày", "được cấp ngày" },
                TargetType = DateClassificationType.IdentityIssueDate,
                Priority = 100,
                DefaultConfidence = 0.98,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày hết hạn giấy tờ tùy thân",
                Keywords = new List<string> { "có giá trị đến", "hết hạn ngày", "ngày hết hạn", "giá trị đến ngày" },
                TargetType = DateClassificationType.IdentityExpiryDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày ký hợp đồng",
                Keywords = new List<string> { "hôm nay, ngày", "hôm nay ngày", "lập ngày", "ký ngày", "hai bên thống nhất ký", "các bên ký ngày", "hợp đồng này được lập ngày", "lập tại" },
                TargetType = DateClassificationType.ContractSignDate,
                Priority = 100,
                DefaultConfidence = 0.96,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày hiệu lực hợp đồng",
                Keywords = new List<string> { "có hiệu lực từ ngày", "hiệu lực kể từ ngày", "có hiệu lực kể từ", "thời điểm có hiệu lực từ ngày", "hiệu lực hợp đồng từ ngày" },
                TargetType = DateClassificationType.ContractEffectiveDate,
                Priority = 95,
                DefaultConfidence = 0.96,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày bắt đầu thực hiện",
                Keywords = new List<string> { "bắt đầu từ ngày", "thời gian thực hiện từ ngày", "ngày bắt đầu", "ngày khởi công" },
                TargetType = DateClassificationType.ContractStartDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày kết thúc / hết hạn hợp đồng",
                Keywords = new List<string> { "đến hết ngày", "kết thúc vào ngày", "ngày kết thúc", "hết hiệu lực ngày", "hết hạn hợp đồng vào ngày" },
                TargetType = DateClassificationType.ContractEndDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày thanh toán",
                Keywords = new List<string> { "thanh toán trước ngày", "thời hạn thanh toán vào ngày", "ngày thanh toán đợt", "thanh toán đợt 1", "thanh toán đợt 2" },
                TargetType = DateClassificationType.PaymentDate,
                Priority = 85,
                DefaultConfidence = 0.93,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày bàn giao / nghiệm thu",
                Keywords = new List<string> { "bàn giao trước ngày", "ngày bàn giao", "thời hạn bàn giao", "nghiệm thu vào ngày" },
                TargetType = DateClassificationType.HandoverDate,
                Priority = 85,
                DefaultConfidence = 0.93,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày ban hành văn bản",
                Keywords = new List<string> { "ban hành ngày", "ngày ký quyết định", "ngày ban hành" },
                TargetType = DateClassificationType.DocumentDate,
                Priority = 80,
                DefaultConfidence = 0.90,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày phụ lục hợp đồng",
                Keywords = new List<string> { "phụ lục số", "phụ lục ngày", "ký phụ lục ngày", "kèm theo phụ lục", "phụ lục đính kèm ngày" },
                TargetType = DateClassificationType.AppendixDate,
                Priority = 85,
                DefaultConfidence = 0.92,
                IsEnabled = true,
                IsBuiltIn = true
            },
            new()
            {
                Name = "Ngày phát sinh tranh chấp / tố tụng",
                Keywords = new List<string> { "tranh chấp ngày", "khiếu nại ngày", "khởi kiện ngày", "hòa giải ngày" },
                TargetType = DateClassificationType.DisputeDate,
                Priority = 80,
                DefaultConfidence = 0.90,
                IsEnabled = true,
                IsBuiltIn = true
            }
        };
    }
}

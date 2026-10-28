using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Data.Sqlite;

namespace ContractDateReplacer.Persistence.Repositories;

/// <summary>
/// Quản lý dữ liệu lưu vết kiểm toán (Audit Logs) trong SQLite.
/// </summary>
public class SqliteAuditLogRepository : IAuditLogRepository
{
    private readonly IAppDataService _appDataService;
    private readonly string _connectionString;

    public SqliteAuditLogRepository(IAppDataService appDataService)
    {
        _appDataService = appDataService;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appDataService.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task SaveAuditLogAsync(BatchAuditLog log, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = @"
            INSERT INTO AuditLogs (BatchId, StartedAt, CompletedAt, TotalFiles, PassedFilesCount, ReviewRequiredFilesCount, ErrorFilesCount, SourceDirectory, OutputDirectory, DetailsJson)
            VALUES (@BatchId, @StartedAt, @CompletedAt, @TotalFiles, @PassedFilesCount, @ReviewRequiredFilesCount, @ErrorFilesCount, @SourceDirectory, @OutputDirectory, @DetailsJson)
            ON CONFLICT(BatchId) DO UPDATE SET
                CompletedAt = @CompletedAt,
                TotalFiles = @TotalFiles,
                PassedFilesCount = @PassedFilesCount,
                ReviewRequiredFilesCount = @ReviewRequiredFilesCount,
                ErrorFilesCount = @ErrorFilesCount,
                DetailsJson = @DetailsJson;
        ";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@BatchId", log.BatchId);
        command.Parameters.AddWithValue("@StartedAt", log.StartedAt.ToString("o"));
        command.Parameters.AddWithValue("@CompletedAt", log.CompletedAt.HasValue ? log.CompletedAt.Value.ToString("o") : (object)DBNull.Value);
        command.Parameters.AddWithValue("@TotalFiles", log.TotalFiles);
        command.Parameters.AddWithValue("@PassedFilesCount", log.PassedFilesCount);
        command.Parameters.AddWithValue("@ReviewRequiredFilesCount", log.ReviewRequiredFilesCount);
        command.Parameters.AddWithValue("@ErrorFilesCount", log.ErrorFilesCount);
        command.Parameters.AddWithValue("@SourceDirectory", log.SourceDirectory ?? string.Empty);
        command.Parameters.AddWithValue("@OutputDirectory", log.OutputDirectory ?? string.Empty);
        command.Parameters.AddWithValue("@DetailsJson", log.DetailsJson ?? "[]");

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<BatchAuditLog>> GetRecentLogsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = "SELECT BatchId, StartedAt, CompletedAt, TotalFiles, PassedFilesCount, ReviewRequiredFilesCount, ErrorFilesCount, SourceDirectory, OutputDirectory, DetailsJson FROM AuditLogs ORDER BY StartedAt DESC LIMIT @Limit";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@Limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<BatchAuditLog>();

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new BatchAuditLog
            {
                BatchId = reader.GetString(0),
                StartedAt = DateTime.Parse(reader.GetString(1)),
                CompletedAt = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                TotalFiles = reader.GetInt32(3),
                PassedFilesCount = reader.GetInt32(4),
                ReviewRequiredFilesCount = reader.GetInt32(5),
                ErrorFilesCount = reader.GetInt32(6),
                SourceDirectory = reader.GetString(7),
                OutputDirectory = reader.GetString(8),
                DetailsJson = reader.GetString(9)
            });
        }

        return list;
    }

    public async Task<BatchAuditLog?> GetLogByBatchIdAsync(string batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = "SELECT BatchId, StartedAt, CompletedAt, TotalFiles, PassedFilesCount, ReviewRequiredFilesCount, ErrorFilesCount, SourceDirectory, OutputDirectory, DetailsJson FROM AuditLogs WHERE BatchId = @BatchId";
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@BatchId", batchId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new BatchAuditLog
            {
                BatchId = reader.GetString(0),
                StartedAt = DateTime.Parse(reader.GetString(1)),
                CompletedAt = reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                TotalFiles = reader.GetInt32(3),
                PassedFilesCount = reader.GetInt32(4),
                ReviewRequiredFilesCount = reader.GetInt32(5),
                ErrorFilesCount = reader.GetInt32(6),
                SourceDirectory = reader.GetString(7),
                OutputDirectory = reader.GetString(8),
                DetailsJson = reader.GetString(9)
            };
        }

        return null;
    }
}

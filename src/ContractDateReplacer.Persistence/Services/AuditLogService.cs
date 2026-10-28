using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.Persistence.Services;

/// <summary>
/// Triển khai dịch vụ nhật ký kiểm toán tuân thủ (Compliance Audit Log Service) với cơ chế bảo vệ Tamper-proof.
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IAppDataService _appDataService;
    private readonly ILogger<AuditLogService> _logger;
    private readonly string _connectionString;

    public AuditLogService(IAppDataService appDataService, ILogger<AuditLogService> logger)
    {
        _appDataService = appDataService;
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appDataService.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task<AuditLogEntry> LogActionAsync(
        AuditActionType actionType,
        string details,
        AuditSeverity severity = AuditSeverity.Info,
        string? targetFile = null,
        string? batchId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditLogEntry
        {
            LogId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            ActionType = actionType,
            UserName = Environment.UserName,
            MachineName = Environment.MachineName,
            BatchId = batchId,
            TargetFile = targetFile,
            DetailsJson = string.IsNullOrWhiteSpace(details) ? "{}" : details,
            Severity = severity
        };

        entry.GenerateIntegrityHash();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = @"
            INSERT INTO ComplianceLogs (LogId, Timestamp, ActionType, UserName, MachineName, BatchId, TargetFile, DetailsJson, Severity, IntegrityHash)
            VALUES (@LogId, @Timestamp, @ActionType, @UserName, @MachineName, @BatchId, @TargetFile, @DetailsJson, @Severity, @IntegrityHash);
        ";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@LogId", entry.LogId);
        command.Parameters.AddWithValue("@Timestamp", entry.Timestamp.ToString("o"));
        command.Parameters.AddWithValue("@ActionType", (int)entry.ActionType);
        command.Parameters.AddWithValue("@UserName", entry.UserName ?? string.Empty);
        command.Parameters.AddWithValue("@MachineName", entry.MachineName ?? string.Empty);
        command.Parameters.AddWithValue("@BatchId", (object?)entry.BatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("@TargetFile", (object?)entry.TargetFile ?? DBNull.Value);
        command.Parameters.AddWithValue("@DetailsJson", entry.DetailsJson);
        command.Parameters.AddWithValue("@Severity", (int)entry.Severity);
        command.Parameters.AddWithValue("@IntegrityHash", entry.IntegrityHash);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Ghi nhận Audit Log [{Action}] ID: {Id} - Severity: {Sev}", actionType, entry.LogId, severity);

        return entry;
    }

    public async Task<List<AuditLogEntry>> QueryLogsAsync(
        AuditLogFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sqlBuilder = new StringBuilder(@"
            SELECT LogId, Timestamp, ActionType, UserName, MachineName, BatchId, TargetFile, DetailsJson, Severity, IntegrityHash
            FROM ComplianceLogs
            WHERE 1=1
        ");

        using var command = new SqliteCommand();
        command.Connection = connection;

        if (filter != null)
        {
            if (filter.FromDate.HasValue)
            {
                sqlBuilder.Append(" AND Timestamp >= @FromDate");
                command.Parameters.AddWithValue("@FromDate", filter.FromDate.Value.ToUniversalTime().ToString("o"));
            }

            if (filter.ToDate.HasValue)
            {
                sqlBuilder.Append(" AND Timestamp <= @ToDate");
                command.Parameters.AddWithValue("@ToDate", filter.ToDate.Value.ToUniversalTime().ToString("o"));
            }

            if (filter.ActionType.HasValue)
            {
                sqlBuilder.Append(" AND ActionType = @ActionType");
                command.Parameters.AddWithValue("@ActionType", (int)filter.ActionType.Value);
            }

            if (filter.Severity.HasValue)
            {
                sqlBuilder.Append(" AND Severity = @Severity");
                command.Parameters.AddWithValue("@Severity", (int)filter.Severity.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchKeyword))
            {
                sqlBuilder.Append(" AND (DetailsJson LIKE @Kw OR TargetFile LIKE @Kw OR BatchId LIKE @Kw OR UserName LIKE @Kw)");
                command.Parameters.AddWithValue("@Kw", $"%{filter.SearchKeyword.Trim()}%");
            }

            int pageSize = filter.PageSize > 0 ? filter.PageSize : 100;
            int page = filter.Page > 0 ? filter.Page : 1;
            int offset = (page - 1) * pageSize;

            sqlBuilder.Append(" ORDER BY Timestamp DESC LIMIT @Limit OFFSET @Offset");
            command.Parameters.AddWithValue("@Limit", pageSize);
            command.Parameters.AddWithValue("@Offset", offset);
        }
        else
        {
            sqlBuilder.Append(" ORDER BY Timestamp DESC LIMIT 100");
        }

        command.CommandText = sqlBuilder.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<AuditLogEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new AuditLogEntry
            {
                LogId = reader.GetString(0),
                Timestamp = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                ActionType = (AuditActionType)reader.GetInt32(2),
                UserName = reader.GetString(3),
                MachineName = reader.GetString(4),
                BatchId = reader.IsDBNull(5) ? null : reader.GetString(5),
                TargetFile = reader.IsDBNull(6) ? null : reader.GetString(6),
                DetailsJson = reader.GetString(7),
                Severity = (AuditSeverity)reader.GetInt32(8),
                IntegrityHash = reader.GetString(9)
            });
        }

        return list;
    }

    public async Task<AuditIntegrityReport> VerifyLogIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql = @"
            SELECT LogId, Timestamp, ActionType, UserName, MachineName, BatchId, TargetFile, DetailsJson, Severity, IntegrityHash
            FROM ComplianceLogs
            ORDER BY Timestamp ASC;
        ";

        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var report = new AuditIntegrityReport();

        while (await reader.ReadAsync(cancellationToken))
        {
            report.TotalLogsScanned++;

            var entry = new AuditLogEntry
            {
                LogId = reader.GetString(0),
                Timestamp = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                ActionType = (AuditActionType)reader.GetInt32(2),
                UserName = reader.GetString(3),
                MachineName = reader.GetString(4),
                BatchId = reader.IsDBNull(5) ? null : reader.GetString(5),
                TargetFile = reader.IsDBNull(6) ? null : reader.GetString(6),
                DetailsJson = reader.GetString(7),
                Severity = (AuditSeverity)reader.GetInt32(8),
                IntegrityHash = reader.GetString(9)
            };

            if (entry.VerifyIntegrity())
            {
                report.ValidLogsCount++;
            }
            else
            {
                report.TamperedLogsCount++;
                report.TamperedLogIds.Add(entry.LogId);
                _logger.LogWarning("Phát hiện log bị sửa đổi trái phép! LogId: {Id}", entry.LogId);
            }
        }

        if (report.TamperedLogsCount == 0)
        {
            report.Message = $"Toàn bộ {report.TotalLogsScanned} bản ghi nhật ký kiểm toán hợp lệ và toàn vẹn 100% (Tamper-Free).";
        }
        else
        {
            report.Message = $"CẢNH BÁO VI PHẠM: Phát hiện {report.TamperedLogsCount} bản ghi nhật ký bị can thiệp sửa đổi trái phép!";
        }

        return report;
    }

    public async Task<string> ExportLogsCsvAsync(
        string destinationFilePath,
        AuditLogFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var logs = await QueryLogsAsync(filter, cancellationToken);

        var sb = new StringBuilder();
        // CSV Header
        sb.AppendLine("\"Mã Log\",\"Thời Điểm (UTC)\",\"Hành Động\",\"Mức Độ\",\"Tệp Mục Tiêu\",\"Mã Batch\",\"Người Thực Hiện\",\"Máy Tính\",\"Chi Tiết Thao Tác\",\"Mã Băm Toàn Vẹn (SHA256)\"");

        foreach (var l in logs)
        {
            string esc(string? val) => (val ?? string.Empty).Replace("\"", "\"\"");

            sb.Append('\"').Append(esc(l.LogId)).Append("\",");
            sb.Append('\"').Append(l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",");
            sb.Append('\"').Append(esc(l.ActionType.ToString())).Append("\",");
            sb.Append('\"').Append(esc(l.Severity.ToString())).Append("\",");
            sb.Append('\"').Append(esc(l.TargetFile)).Append("\",");
            sb.Append('\"').Append(esc(l.BatchId)).Append("\",");
            sb.Append('\"').Append(esc(l.UserName)).Append("\",");
            sb.Append('\"').Append(esc(l.MachineName)).Append("\",");
            sb.Append('\"').Append(esc(l.DetailsJson)).Append("\",");
            sb.Append('\"').Append(esc(l.IntegrityHash)).AppendLine("\"");
        }

        string dir = Path.GetDirectoryName(destinationFilePath)!;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Ghi với UTF-8 BOM
        await File.WriteAllTextAsync(destinationFilePath, sb.ToString(), new UTF8Encoding(true), cancellationToken);
        _logger.LogInformation("Đã xuất {Count} audit logs sang CSV tại {Path}", logs.Count, destinationFilePath);

        return destinationFilePath;
    }

    public async Task<string> ExportLogsJsonAsync(
        string destinationFilePath,
        AuditLogFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var logs = await QueryLogsAsync(filter, cancellationToken);
        string json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });

        string dir = Path.GetDirectoryName(destinationFilePath)!;
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(destinationFilePath, json, Encoding.UTF8, cancellationToken);
        _logger.LogInformation("Đã xuất {Count} audit logs sang JSON tại {Path}", logs.Count, destinationFilePath);

        return destinationFilePath;
    }
}

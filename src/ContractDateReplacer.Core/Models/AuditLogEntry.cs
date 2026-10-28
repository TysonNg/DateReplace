using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Các loại hành động được ghi nhận trong hệ thống kiểm toán tuân thủ.
/// </summary>
public enum AuditActionType
{
    BatchExecution = 0,
    FileRollback = 1,
    BatchRollback = 2,
    RuleModified = 3,
    PresetModified = 4,
    ConfigChanged = 5,
    SecurityAlert = 6
}

/// <summary>
/// Mức độ nghiêm trọng của bản ghi nhật ký kiểm toán.
/// </summary>
public enum AuditSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>
/// Bản ghi nhật ký kiểm toán chống can thiệp (Tamper-proof / Append-only).
/// </summary>
public class AuditLogEntry
{
    private const string Salt = "CDR_COMPLIANCE_SECURE_SALT_v1";

    public string LogId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public AuditActionType ActionType { get; set; }
    public string UserName { get; set; } = Environment.UserName;
    public string MachineName { get; set; } = Environment.MachineName;
    public string? BatchId { get; set; }
    public string? TargetFile { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public AuditSeverity Severity { get; set; } = AuditSeverity.Info;
    public string IntegrityHash { get; set; } = string.Empty;

    public void GenerateIntegrityHash()
    {
        IntegrityHash = ComputeHash(LogId, Timestamp, ActionType, UserName, MachineName, TargetFile, DetailsJson, Severity);
    }

    public bool VerifyIntegrity()
    {
        string expected = ComputeHash(LogId, Timestamp, ActionType, UserName, MachineName, TargetFile, DetailsJson, Severity);
        return string.Equals(IntegrityHash, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeHash(
        string logId,
        DateTime timestamp,
        AuditActionType actionType,
        string userName,
        string machineName,
        string? targetFile,
        string detailsJson,
        AuditSeverity severity)
    {
        string timestampIso = timestamp.ToUniversalTime().ToString("o");
        string payload = $"{Salt}|{logId}|{timestampIso}|{actionType}|{userName}|{machineName}|{targetFile ?? string.Empty}|{severity}|{detailsJson}";
        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes);
    }
}

/// <summary>
/// Bộ lọc tra cứu nhật ký kiểm toán.
/// </summary>
public class AuditLogFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public AuditActionType? ActionType { get; set; }
    public AuditSeverity? Severity { get; set; }
    public string? SearchKeyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

/// <summary>
/// Báo cáo xác thực tính toàn vẹn chuỗi nhật ký kiểm toán.
/// </summary>
public class AuditIntegrityReport
{
    public int TotalLogsScanned { get; set; }
    public int ValidLogsCount { get; set; }
    public int TamperedLogsCount { get; set; }
    public bool IsTamperFree => TamperedLogsCount == 0;
    public List<string> TamperedLogIds { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

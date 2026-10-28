using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Dịch vụ ghi nhận và kiểm tra nhật ký kiểm toán tuân thủ (Compliance Audit Log Service).
/// </summary>
public interface IAuditLogService
{
    Task<AuditLogEntry> LogActionAsync(
        AuditActionType actionType,
        string details,
        AuditSeverity severity = AuditSeverity.Info,
        string? targetFile = null,
        string? batchId = null,
        CancellationToken cancellationToken = default);

    Task<List<AuditLogEntry>> QueryLogsAsync(
        AuditLogFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<AuditIntegrityReport> VerifyLogIntegrityAsync(
        CancellationToken cancellationToken = default);

    Task<string> ExportLogsCsvAsync(
        string destinationFilePath,
        AuditLogFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<string> ExportLogsJsonAsync(
        string destinationFilePath,
        AuditLogFilter? filter = null,
        CancellationToken cancellationToken = default);
}

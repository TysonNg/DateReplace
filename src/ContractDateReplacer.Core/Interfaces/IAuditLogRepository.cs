using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Quản lý lưu trữ và tra cứu lịch sử các phiên thay thế ngày.
/// </summary>
public interface IAuditLogRepository
{
    Task SaveAuditLogAsync(BatchAuditLog log, CancellationToken cancellationToken = default);
    Task<List<BatchAuditLog>> GetRecentLogsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<BatchAuditLog?> GetLogByBatchIdAsync(string batchId, CancellationToken cancellationToken = default);
}

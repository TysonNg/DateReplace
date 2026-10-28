using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ sao lưu an toàn trước khi thay đổi (Backup) và khôi phục (Rollback/Restore).
/// </summary>
public interface IBackupRollbackService
{
    /// <summary>
    /// Sao chép file gốc sang thư mục Backup, tính mã băm SHA256 và lưu siêu dữ liệu vào SQLite.
    /// </summary>
    Task<BackupRecord> CreateBackupAsync(
        string batchId,
        string originalFilePath,
        string? targetModifiedPath,
        int replacementCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khôi phục một file cụ thể từ bản sao lưu, kiểm tra mã băm SHA256 trước và sau khôi phục.
    /// </summary>
    Task<RestoreResult> RestoreFileAsync(
        string backupId,
        string? destinationPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Khôi phục toàn bộ các tệp thuộc một đợt thay thế (Batch).
    /// </summary>
    Task<IReadOnlyList<RestoreResult>> RestoreBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các bản sao lưu theo mã đợt (BatchId).
    /// </summary>
    Task<IReadOnlyList<BackupRecord>> GetBackupsByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy toàn bộ danh sách các đợt sao lưu (sắp xếp giảm dần theo thời gian).
    /// </summary>
    Task<IReadOnlyList<BackupRecord>> GetAllBackupsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tự động dọn dẹp các bản sao lưu đã cũ quá số ngày retention (mặc định 30 ngày).
    /// </summary>
    Task<int> CleanupExpiredBackupsAsync(
        int retentionDays = 30,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa một bản sao lưu thủ công.
    /// </summary>
    Task<bool> DeleteBackupAsync(
        string backupId,
        CancellationToken cancellationToken = default);
}

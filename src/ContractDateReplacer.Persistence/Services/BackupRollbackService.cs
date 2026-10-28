using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.Persistence.Services;

/// <summary>
/// Dịch vụ sao lưu an toàn trước khi thay đổi (Backup) và khôi phục (Rollback/Restore).
/// Đảm bảo tính toàn vẹn 100% bằng mã băm SHA256 và lưu vết lịch sử trong SQLite.
/// </summary>
public class BackupRollbackService : IBackupRollbackService
{
    private readonly IAppDataService _appDataService;
    private readonly ILogger<BackupRollbackService> _logger;

    public BackupRollbackService(
        IAppDataService appDataService,
        ILogger<BackupRollbackService> logger)
    {
        _appDataService = appDataService;
        _logger = logger;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _appDataService.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        return new SqliteConnection(builder.ToString());
    }

    public static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToUpperInvariant();
    }

    public async Task<BackupRecord> CreateBackupAsync(
        string batchId,
        string originalFilePath,
        string? targetModifiedPath,
        int replacementCount,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(originalFilePath))
        {
            throw new FileNotFoundException($"File gốc không tồn tại để sao lưu: {originalFilePath}");
        }

        // 1. Tính mã băm SHA256 của file gốc
        string originalChecksum = ComputeSha256(originalFilePath);

        // 2. Tạo đường dẫn lưu backup an toàn trong AppData
        string backupDir = Path.Combine(_appDataService.RootDirectory, "Backups", batchId);
        Directory.CreateDirectory(backupDir);

        string fileName = Path.GetFileNameWithoutExtension(originalFilePath);
        string ext = Path.GetExtension(originalFilePath);
        string backupFileName = $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmssfff}.bak";
        string backupFilePath = Path.Combine(backupDir, backupFileName);

        // 3. Sao chép file gốc sang thư mục backup
        File.Copy(originalFilePath, backupFilePath, true);

        // 4. Đối soát mã băm của file backup vừa tạo
        string backupChecksum = ComputeSha256(backupFilePath);
        if (!string.Equals(originalChecksum, backupChecksum, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(backupFilePath);
            throw new InvalidOperationException($"Lỗi tính toàn vẹn khi tạo backup: Checksum file gốc và file backup không khớp!");
        }

        var record = new BackupRecord
        {
            BackupId = Guid.NewGuid().ToString("N"),
            BatchId = batchId,
            OriginalPath = originalFilePath,
            BackupPath = backupFilePath,
            TargetModifiedPath = targetModifiedPath ?? string.Empty,
            ChecksumSHA256 = originalChecksum,
            CreatedAt = DateTime.Now,
            ReplacementCount = replacementCount,
            Status = "Available"
        };

        // 5. Lưu siêu dữ liệu vào SQLite
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string insertSql = @"
            INSERT INTO Backups (
                BackupId, BatchId, OriginalPath, BackupPath, TargetModifiedPath,
                ChecksumSHA256, CreatedAt, ReplacementCount, Status
            ) VALUES (
                @BackupId, @BatchId, @OriginalPath, @BackupPath, @TargetModifiedPath,
                @ChecksumSHA256, @CreatedAt, @ReplacementCount, @Status
            );";

        await using var cmd = new SqliteCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("@BackupId", record.BackupId);
        cmd.Parameters.AddWithValue("@BatchId", record.BatchId);
        cmd.Parameters.AddWithValue("@OriginalPath", record.OriginalPath);
        cmd.Parameters.AddWithValue("@BackupPath", record.BackupPath);
        cmd.Parameters.AddWithValue("@TargetModifiedPath", record.TargetModifiedPath);
        cmd.Parameters.AddWithValue("@ChecksumSHA256", record.ChecksumSHA256);
        cmd.Parameters.AddWithValue("@CreatedAt", record.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ReplacementCount", record.ReplacementCount);
        cmd.Parameters.AddWithValue("@Status", record.Status);

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Đã tạo bản sao lưu an toàn cho {File} tại {Backup} (SHA256: {Hash})",
            originalFilePath, backupFilePath, originalChecksum);

        return record;
    }

    public async Task<RestoreResult> RestoreFileAsync(
        string backupId,
        string? destinationPath = null,
        CancellationToken cancellationToken = default)
    {
        var result = new RestoreResult
        {
            BackupId = backupId
        };

        // 1. Tìm thông tin bản sao lưu trong SQLite
        BackupRecord? record = await GetBackupByIdAsync(backupId, cancellationToken);
        if (record == null)
        {
            result.IsSuccess = false;
            result.Message = $"Không tìm thấy bản sao lưu có ID: {backupId}";
            return result;
        }

        result.ExpectedChecksum = record.ChecksumSHA256;

        // 2. Kiểm tra file backup trên đĩa
        if (!File.Exists(record.BackupPath))
        {
            result.IsSuccess = false;
            result.Message = $"File sao lưu không tồn tại trên ổ đĩa: {record.BackupPath}";
            return result;
        }

        // 3. Kiểm tra mã băm SHA256 của file backup trước khi khôi phục (Chống can thiệp/hỏng file)
        string backupChecksum = ComputeSha256(record.BackupPath);
        if (!string.Equals(backupChecksum, record.ChecksumSHA256, StringComparison.OrdinalIgnoreCase))
        {
            result.IsSuccess = false;
            result.ActualChecksum = backupChecksum;
            result.Message = $"Từ chối khôi phục! Checksum file backup ({backupChecksum}) không khớp với checksum gốc ({record.ChecksumSHA256}). File có thể đã bị can thiệp trái phép hoặc bị hỏng.";
            _logger.LogError("Từ chối khôi phục file {BackupId} do checksum không khớp.", backupId);
            return result;
        }

        // 4. Xác định đích đến để khôi phục
        string targetPath = destinationPath 
            ?? (!string.IsNullOrWhiteSpace(record.TargetModifiedPath) && File.Exists(record.TargetModifiedPath)
                ? record.TargetModifiedPath 
                : record.OriginalPath);

        result.RestoredFilePath = targetPath;

        try
        {
            // Đảm bảo thư mục cha tồn tại
            string? parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // 5. Ghi đè file từ backup
            File.Copy(record.BackupPath, targetPath, true);

            // 6. Đối soát tính toàn vẹn của file sau khi ghi đè
            string restoredChecksum = ComputeSha256(targetPath);
            result.ActualChecksum = restoredChecksum;

            if (!string.Equals(restoredChecksum, record.ChecksumSHA256, StringComparison.OrdinalIgnoreCase))
            {
                result.IsSuccess = false;
                result.Message = "Lỗi xác minh sau khi ghi đè: File sau khôi phục không khớp checksum!";
                return result;
            }

            // 7. Cập nhật trạng thái 'Restored' trong SQLite
            await UpdateBackupStatusAsync(backupId, "Restored", cancellationToken);

            result.IsSuccess = true;
            result.Message = $"Khôi phục thành công file {Path.GetFileName(targetPath)}. Checksum toàn vẹn 100%.";
            _logger.LogInformation("Khôi phục thành công file {File} từ backup {BackupId}", targetPath, backupId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi trong quá trình khôi phục file {Path}", targetPath);
            result.IsSuccess = false;
            result.Message = $"Lỗi khi ghi đè file: {ex.Message}";
            return result;
        }
    }

    public async Task<IReadOnlyList<RestoreResult>> RestoreBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RestoreResult>();
        var backups = await GetBackupsByBatchIdAsync(batchId, cancellationToken);

        foreach (var backup in backups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var res = await RestoreFileAsync(backup.BackupId, null, cancellationToken);
            results.Add(res);
        }

        return results;
    }

    public async Task<IReadOnlyList<BackupRecord>> GetBackupsByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var list = new List<BackupRecord>();

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string sql = @"
            SELECT BackupId, BatchId, OriginalPath, BackupPath, TargetModifiedPath,
                   ChecksumSHA256, CreatedAt, ReplacementCount, Status
            FROM Backups
            WHERE BatchId = @BatchId
            ORDER BY CreatedAt DESC;";

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BatchId", batchId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToRecord(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<BackupRecord>> GetAllBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        var list = new List<BackupRecord>();

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string sql = @"
            SELECT BackupId, BatchId, OriginalPath, BackupPath, TargetModifiedPath,
                   ChecksumSHA256, CreatedAt, ReplacementCount, Status
            FROM Backups
            ORDER BY CreatedAt DESC;";

        await using var cmd = new SqliteCommand(sql, conn);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToRecord(reader));
        }

        return list;
    }

    public async Task<int> CleanupExpiredBackupsAsync(
        int retentionDays = 30,
        CancellationToken cancellationToken = default)
    {
        var thresholdDate = DateTime.Now.AddDays(-retentionDays);
        int deletedCount = 0;

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string selectSql = @"
            SELECT BackupId, BackupPath
            FROM Backups
            WHERE CreatedAt < @Threshold AND Status != 'Expired';";

        await using var cmd = new SqliteCommand(selectSql, conn);
        cmd.Parameters.AddWithValue("@Threshold", thresholdDate.ToString("o"));

        var toExpire = new List<(string BackupId, string BackupPath)>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                toExpire.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (id, path) in toExpire)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể xóa tệp backup cũ tại {Path}", path);
            }

            await UpdateBackupStatusAsync(id, "Expired", cancellationToken);
            deletedCount++;
        }

        _logger.LogInformation("Đã dọn dẹp {Count} bản sao lưu hết hạn lưu trữ ({Days} ngày)", deletedCount, retentionDays);
        return deletedCount;
    }

    public async Task<bool> DeleteBackupAsync(
        string backupId,
        CancellationToken cancellationToken = default)
    {
        var record = await GetBackupByIdAsync(backupId, cancellationToken);
        if (record == null) return false;

        try
        {
            if (File.Exists(record.BackupPath))
            {
                File.Delete(record.BackupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể xóa tệp backup tại {Path}", record.BackupPath);
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string deleteSql = "DELETE FROM Backups WHERE BackupId = @BackupId;";
        await using var cmd = new SqliteCommand(deleteSql, conn);
        cmd.Parameters.AddWithValue("@BackupId", backupId);

        int rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    private async Task<BackupRecord?> GetBackupByIdAsync(string backupId, CancellationToken cancellationToken)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string sql = @"
            SELECT BackupId, BatchId, OriginalPath, BackupPath, TargetModifiedPath,
                   ChecksumSHA256, CreatedAt, ReplacementCount, Status
            FROM Backups
            WHERE BackupId = @BackupId LIMIT 1;";

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BackupId", backupId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapReaderToRecord(reader);
        }

        return null;
    }

    private async Task UpdateBackupStatusAsync(string backupId, string status, CancellationToken cancellationToken)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        string sql = "UPDATE Backups SET Status = @Status WHERE BackupId = @BackupId;";
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@BackupId", backupId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static BackupRecord MapReaderToRecord(SqliteDataReader reader)
    {
        return new BackupRecord
        {
            BackupId = reader.GetString(0),
            BatchId = reader.GetString(1),
            OriginalPath = reader.GetString(2),
            BackupPath = reader.GetString(3),
            TargetModifiedPath = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            ChecksumSHA256 = reader.GetString(5),
            CreatedAt = DateTime.Parse(reader.GetString(6)),
            ReplacementCount = reader.GetInt32(7),
            Status = reader.GetString(8)
        };
    }
}

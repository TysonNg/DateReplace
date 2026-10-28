using System;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Bản ghi nhật ký kiểm toán (Audit Log) cho mỗi đợt chạy xử lý batch.
/// </summary>
public class BatchAuditLog
{
    public string BatchId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    public int TotalFiles { get; set; }
    public int PassedFilesCount { get; set; }
    public int ReviewRequiredFilesCount { get; set; }
    public int ErrorFilesCount { get; set; }

    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Chuỗi JSON chi tiết từng thay đổi (file, vị trí, giá trị cũ/mới, hash SHA256).
    /// </summary>
    public string DetailsJson { get; set; } = "[]";
}

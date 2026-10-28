using System;
using System.IO;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Siêu dữ liệu bản sao lưu an toàn trước khi chỉnh sửa file Word.
/// </summary>
public class BackupRecord
{
    public string BackupId { get; set; } = Guid.NewGuid().ToString("N");
    public string BatchId { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string TargetModifiedPath { get; set; } = string.Empty;
    public string ChecksumSHA256 { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int ReplacementCount { get; set; }
    public string Status { get; set; } = "Available"; // "Available", "Restored", "Expired"

    public string FileName => Path.GetFileName(OriginalPath);
    public string FormattedDate => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");

    public string StatusBadgeAppearance => Status switch
    {
        "Available" => "Success",
        "Restored" => "Info",
        "Expired" => "Secondary",
        _ => "Caution"
    };
}

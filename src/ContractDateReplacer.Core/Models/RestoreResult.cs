namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Kết quả thao tác khôi phục file (Restore/Rollback).
/// </summary>
public class RestoreResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string BackupId { get; set; } = string.Empty;
    public string RestoredFilePath { get; set; } = string.Empty;
    public string ExpectedChecksum { get; set; } = string.Empty;
    public string ActualChecksum { get; set; } = string.Empty;
}

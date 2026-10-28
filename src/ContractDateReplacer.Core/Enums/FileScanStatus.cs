namespace ContractDateReplacer.Core.Enums;

/// <summary>
/// Trạng thái quét tài liệu Word trong danh sách batch.
/// </summary>
public enum FileScanStatus
{
    NotScanned = 0,
    Scanning,
    Pass,
    ReviewRequired,
    Error,
    Completed
}

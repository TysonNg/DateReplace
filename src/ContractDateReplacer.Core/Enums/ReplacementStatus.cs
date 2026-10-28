namespace ContractDateReplacer.Core.Enums;

/// <summary>
/// Trạng thái thay thế từng vị trí ngày trong tài liệu.
/// </summary>
public enum ReplacementStatus
{
    Pending = 0,
    Success,
    Skipped,
    Unsafe,
    Failed
}

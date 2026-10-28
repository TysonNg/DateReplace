using System;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Đại diện cho một điểm xuất hiện cụ thể cần thay thế ngày.
/// </summary>
public class ReplacementOccurrence
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    public DateCandidate Candidate { get; set; } = new();
    public DocumentTextSegment Segment { get; set; } = new();

    /// <summary>
    /// Giá trị ngày mới dạng text sẽ được ghi vào Word.
    /// </summary>
    public string NewDateText { get; set; } = string.Empty;

    /// <summary>
    /// Giá trị DateTime mới nếu có.
    /// </summary>
    public DateTime? NewDateTime { get; set; }

    /// <summary>
    /// Người dùng đã tick chọn thay thế vị trí này hay chưa.
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// Trạng thái thực thi thay thế.
    /// </summary>
    public ReplacementStatus Status { get; set; } = ReplacementStatus.Pending;

    /// <summary>
    /// Ghi chú an toàn hoặc lỗi nếu có.
    /// </summary>
    public string? SafetyNote { get; set; }
}

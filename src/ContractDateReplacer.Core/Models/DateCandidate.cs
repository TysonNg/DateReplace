using System;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Đại diện cho một ứng viên ngày được phát hiện trong chuỗi văn bản logic.
/// </summary>
public class DateCandidate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SegmentId { get; set; } = string.Empty;

    /// <summary>
    /// Chuỗi văn bản thô xuất hiện trong tài liệu (ví dụ: "ngày 02 tháng 09 năm 2026", "02/09/2026").
    /// </summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>
    /// Vị trí bắt đầu trong chuỗi LogicalText của Segment.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Chiều dài của chuỗi ngày trong LogicalText.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Giá trị DateTime được parse (nếu có đủ ngày, tháng, năm hợp lệ).
    /// </summary>
    public DateTime? ParsedDate { get; set; }

    public int? Day { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }

    public DatePatternType PatternType { get; set; } = DatePatternType.StandardSlash;

    /// <summary>
    /// Cho biết ngày này có đủ cả ngày, tháng, năm hay chỉ là một phần (vd: chỉ có tháng/năm hoặc điền khuyết).
    /// </summary>
    public bool IsCompleteDate { get; set; } = true;

    /// <summary>
    /// Ngữ cảnh khoảng 100 ký tự đứng trước ngày.
    /// </summary>
    public string ContextBefore { get; set; } = string.Empty;

    /// <summary>
    /// Ngữ cảnh khoảng 100 ký tự đứng sau ngày.
    /// </summary>
    public string ContextAfter { get; set; } = string.Empty;

    /// <summary>
    /// Độ tin cậy nhận diện mẫu ngày (từ 0.0 đến 1.0).
    /// </summary>
    public double DetectionConfidence { get; set; } = 1.0;

    /// <summary>
    /// Kết quả phân loại ngữ cảnh.
    /// </summary>
    public ClassificationResult Classification { get; set; } = new();

    /// <summary>
    /// Hiển thị đoạn ngữ cảnh hoàn chỉnh có bôi nổi ngày để người dùng dễ quan sát.
    /// </summary>
    public string FullContextDisplay => $"...{ContextBefore}[{RawText}]{ContextAfter}...";
}

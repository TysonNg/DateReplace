using System.Collections.Generic;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Kết quả phân loại ngữ cảnh của một ứng viên ngày.
/// </summary>
public class ClassificationResult
{
    public DateClassificationType Type { get; set; } = DateClassificationType.Unknown;

    /// <summary>
    /// Điểm độ tin cậy từ 0.0 đến 1.0 (ví dụ 0.95 = 95%).
    /// </summary>
    public double Confidence { get; set; } = 0.0;

    /// <summary>
    /// Danh sách các từ khóa hoặc rule đã kích hoạt kết quả này.
    /// </summary>
    public List<string> MatchedRules { get; set; } = new();

    /// <summary>
    /// Diễn giải lý do phân loại.
    /// </summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Tên quy tắc chính đã khớp.
    /// </summary>
    public string MatchedRuleName { get; set; } = string.Empty;

    /// <summary>
    /// Khuyến nghị người dùng có nên thay thế ngày này hay không.
    /// Mặc định: Ngày ký, hiệu lực, hết hạn, thanh toán, bàn giao, phụ lục -> true.
    /// Ngày sinh, ngày cấp CCCD -> false (bảo vệ danh tính cá nhân).
    /// </summary>
    public bool IsRecommendedForReplacement { get; set; } = true;

    public bool IsReviewRequired => Confidence < 0.90 || Type == DateClassificationType.Unknown;
}

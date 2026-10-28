using System.Collections.Generic;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Quy tắc nhận diện và phân loại loại ngày dựa trên từ khóa ngữ cảnh.
/// </summary>
public class ClassificationRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Các từ khóa nhận diện trong phạm vi context (ví dụ: "sinh ngày", "ngày sinh").
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// Loại ngày tương ứng khi khớp quy tắc.
    /// </summary>
    public DateClassificationType TargetType { get; set; } = DateClassificationType.Unknown;

    /// <summary>
    /// Mức độ ưu tiên của quy tắc (số càng lớn ưu tiên càng cao).
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Độ tin cậy mặc định khi rule khớp (ví dụ: 0.95 = 95%).
    /// </summary>
    public double DefaultConfidence { get; set; } = 0.95;

    public bool IsEnabled { get; set; } = true;
    public bool IsBuiltIn { get; set; } = false;
}

using System;
using System.Collections.Generic;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Chế độ tính toán ngày thay thế trong mẫu thiết lập (Preset).
/// </summary>
public enum DateOffsetMode
{
    /// <summary>
    /// Sử dụng một ngày cố định cụ thể (VD: 01/01/2026).
    /// </summary>
    FixedDate = 0,

    /// <summary>
    /// Sử dụng ngày hiện tại (Hôm nay).
    /// </summary>
    CurrentDate = 1,

    /// <summary>
    /// Tính toán bù trừ từ một loại ngày khác (VD: Bắt đầu = Ngày ký + 30 ngày).
    /// </summary>
    OffsetFromAnotherDate = 2,

    /// <summary>
    /// Cộng dồn số năm/tháng/ngày từ chính ngày gốc hiện tại của hợp đồng (VD: +1 năm).
    /// </summary>
    OffsetYearsFromSelf = 3,

    /// <summary>
    /// Giữ nguyên ngày gốc, không thay đổi.
    /// </summary>
    KeepOriginal = 4
}

/// <summary>
/// Quy tắc bù trừ / gán ngày cho từng loại ngày hợp đồng.
/// </summary>
public class DateOffsetRule
{
    public DateClassificationType TargetClassificationType { get; set; }
    public DateOffsetMode Mode { get; set; } = DateOffsetMode.CurrentDate;
    public DateTime? FixedValue { get; set; }
    public DateClassificationType? BaseDateType { get; set; }
    public int OffsetDays { get; set; }
    public int OffsetMonths { get; set; }
    public int OffsetYears { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Tính toán ngày mục tiêu dựa theo cấu hình của quy tắc.
    /// </summary>
    public DateTime CalculateDate(
        DateTime? originalDate,
        IReadOnlyDictionary<DateClassificationType, DateTime>? resolvedDates = null,
        DateTime? referenceNow = null)
    {
        var today = (referenceNow ?? DateTime.Today).Date;

        switch (Mode)
        {
            case DateOffsetMode.CurrentDate:
                return today;

            case DateOffsetMode.FixedDate:
                return (FixedValue ?? today).Date;

            case DateOffsetMode.OffsetFromAnotherDate:
                DateTime baseDate = today;
                if (BaseDateType.HasValue && resolvedDates != null && resolvedDates.TryGetValue(BaseDateType.Value, out var foundDate))
                {
                    baseDate = foundDate;
                }
                else if (originalDate.HasValue)
                {
                    baseDate = originalDate.Value;
                }

                return baseDate.AddYears(OffsetYears).AddMonths(OffsetMonths).AddDays(OffsetDays);

            case DateOffsetMode.OffsetYearsFromSelf:
                DateTime selfBase = originalDate ?? today;
                return selfBase.AddYears(OffsetYears).AddMonths(OffsetMonths).AddDays(OffsetDays);

            case DateOffsetMode.KeepOriginal:
            default:
                return originalDate ?? today;
        }
    }
}

/// <summary>
/// Mẫu thiết lập thay thế ngày hợp đồng thường dùng (Preset / Template).
/// </summary>
public class PresetModel
{
    public string PresetId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<DateOffsetRule> DateMappingRules { get; set; } = new();

    public string OutputMode { get; set; } = "Suffix"; // "Suffix" | "NewFolder" | "InPlace"
    public string SuffixPattern { get; set; } = "_2026";
    public string CustomFolder { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Giải quyết và tính toán ánh xạ ngày cho toàn bộ các quy tắc trong Preset.
    /// Thứ tự giải quyết: Ngày độc lập (CurrentDate, FixedDate) trước, sau đó tới ngày phụ thuộc (OffsetFromAnotherDate).
    /// </summary>
    public Dictionary<DateClassificationType, DateTime> ResolveAllTargetDates(
        IReadOnlyDictionary<DateClassificationType, DateTime>? sampleOriginalDates = null,
        DateTime? referenceNow = null)
    {
        var result = new Dictionary<DateClassificationType, DateTime>();
        var now = referenceNow ?? DateTime.Today;

        // 1. Giải quyết các quy tắc độc lập trước
        foreach (var rule in DateMappingRules)
        {
            if (!rule.IsActive) continue;
            if (rule.Mode != DateOffsetMode.OffsetFromAnotherDate)
            {
                DateTime? orig = null;
                if (sampleOriginalDates != null && sampleOriginalDates.TryGetValue(rule.TargetClassificationType, out var oVal))
                {
                    orig = oVal;
                }
                result[rule.TargetClassificationType] = rule.CalculateDate(orig, result, now);
            }
        }

        // 2. Giải quyết các quy tắc phụ thuộc (có thể có chuỗi phụ thuộc)
        foreach (var rule in DateMappingRules)
        {
            if (!rule.IsActive) continue;
            if (rule.Mode == DateOffsetMode.OffsetFromAnotherDate)
            {
                DateTime? orig = null;
                if (sampleOriginalDates != null && sampleOriginalDates.TryGetValue(rule.TargetClassificationType, out var oVal))
                {
                    orig = oVal;
                }
                result[rule.TargetClassificationType] = rule.CalculateDate(orig, result, now);
            }
        }

        return result;
    }
}

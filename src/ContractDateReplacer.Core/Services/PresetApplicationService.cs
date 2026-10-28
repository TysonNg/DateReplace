using System;
using System.Collections.Generic;
using System.Linq;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Services;

/// <summary>
/// Triển khai dịch vụ tính toán và áp dụng mẫu thiết lập ngày (Preset Application Service).
/// </summary>
public class PresetApplicationService : IPresetApplicationService
{
    public Dictionary<DateClassificationType, DateTime> ResolveTargetDates(
        PresetModel preset,
        IReadOnlyDictionary<DateClassificationType, DateTime>? sampleOriginalDates = null,
        DateTime? referenceNow = null)
    {
        return preset.ResolveAllTargetDates(sampleOriginalDates, referenceNow);
    }

    public string? CalculateReplacementDateString(
        PresetModel preset,
        DateClassificationType classificationType,
        DateTime? originalDate,
        IReadOnlyDictionary<DateClassificationType, DateTime> resolvedDates,
        string dateFormat = "dd/MM/yyyy",
        DateTime? referenceNow = null)
    {
        var rule = preset.DateMappingRules.FirstOrDefault(r => r.TargetClassificationType == classificationType && r.IsActive);
        if (rule == null)
        {
            return null; // Không có quy tắc nào ánh xạ cho loại ngày này
        }

        if (rule.Mode == DateOffsetMode.KeepOriginal)
        {
            return null; // Giữ nguyên ngày gốc
        }

        DateTime targetDate = rule.CalculateDate(originalDate, resolvedDates, referenceNow);
        return targetDate.ToString(dateFormat);
    }
}

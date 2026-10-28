using System;
using System.Collections.Generic;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Dịch vụ tính toán và áp dụng mẫu thiết lập (Preset) lên các ứng viên ngày hợp đồng.
/// </summary>
public interface IPresetApplicationService
{
    Dictionary<DateClassificationType, DateTime> ResolveTargetDates(
        PresetModel preset,
        IReadOnlyDictionary<DateClassificationType, DateTime>? sampleOriginalDates = null,
        DateTime? referenceNow = null);

    string? CalculateReplacementDateString(
        PresetModel preset,
        DateClassificationType classificationType,
        DateTime? originalDate,
        IReadOnlyDictionary<DateClassificationType, DateTime> resolvedDates,
        string dateFormat = "dd/MM/yyyy",
        DateTime? referenceNow = null);
}

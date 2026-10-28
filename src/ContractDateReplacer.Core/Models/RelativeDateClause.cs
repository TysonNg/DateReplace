using System;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Loại hình quan hệ thời gian tương đối trong hợp đồng.
/// </summary>
public enum RelativeOffsetType
{
    CalendarDays = 0,
    BusinessDays = 1,
    Months = 2,
    Years = 3,
    RecurringMonthly = 4
}

/// <summary>
/// Đại diện cho mệnh đề thời gian tương đối được suy luận từ văn bản hợp đồng.
/// </summary>
public class RelativeDateClause
{
    public string SourcePhrase { get; set; } = string.Empty;
    public DateClassificationType ReferenceDateType { get; set; } = DateClassificationType.ContractSignDate;
    public RelativeOffsetType OffsetType { get; set; } = RelativeOffsetType.CalendarDays;
    public int OffsetQuantity { get; set; }
    public DateClassificationType? TargetClassification { get; set; }
    public DateTime? CalculatedTargetDate { get; set; }
    public bool IsBusinessDayCalculation => OffsetType == RelativeOffsetType.BusinessDays;
    public string Explanation { get; set; } = string.Empty;
}

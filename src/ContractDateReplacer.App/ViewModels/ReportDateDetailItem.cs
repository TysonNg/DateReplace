namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// Chi tiết của một vị trí ngày trong bảng báo cáo kết quả thay thế hợp đồng.
/// </summary>
public class ReportDateDetailItem
{
    public string OldDateText { get; set; } = string.Empty;
    public string NewDateText { get; set; } = string.Empty;
    public string ClassificationTypeName { get; set; } = string.Empty;
    public string ResultBadgeText { get; set; } = "[OK]";
    public string ResultBadgeAppearance { get; set; } = "Success";
    public string SourceLocation { get; set; } = string.Empty;
    public bool IsReplaced { get; set; }

    public string FormattedSummary => IsReplaced
        ? $"{OldDateText}  ➔  {NewDateText} ({ClassificationTypeName}) {ResultBadgeText}"
        : $"{OldDateText} ({ClassificationTypeName}) {ResultBadgeText}";
}

namespace ContractDateReplacer.Core.Enums;

/// <summary>
/// Kiểu mẫu định dạng ngày được phát hiện.
/// </summary>
public enum DatePatternType
{
    StandardSlash,       // dd/MM/yyyy hoặc d/M/yyyy
    StandardHyphen,      // dd-MM-yyyy hoặc d-M-yyyy
    StandardDot,         // dd.MM.yyyy hoặc d.M.yyyy
    StandardSpace,       // dd MM yyyy
    VietnameseFull,      // Ngày dd tháng MM năm yyyy
    VietnameseMonthYear, // tháng MM năm yyyy
    VietnameseYearOnly,  // năm yyyy
    TemplatePlaceholder, // Ngày ..... tháng ..... năm yyyy hoặc Ngày ... tháng ... năm ...
    AmbiguousShort,      // 01/02/03 hoặc dạng số mơ hồ
    DayMonth,            // d/M hoặc dd/MM, không có năm
    MonthYearDelimited   // MM/yyyy, MM-yyyy, MM.yyyy (tháng/năm phân cách)
}

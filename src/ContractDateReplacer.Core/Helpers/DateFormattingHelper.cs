using System;
using System.Text.RegularExpressions;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Helpers;

/// <summary>
/// Trợ giúp định dạng ngày thay thế mới khớp chuẩn xác với cấu trúc định dạng của chuỗi ngày gốc.
/// </summary>
public static class DateFormattingHelper
{
    public static string FormatReplacementDate(DateCandidate candidate, DateTime newDate)
    {
        if (candidate == null) return newDate.ToString("dd/MM/yyyy");

        string raw = candidate.RawText.Trim();

        switch (candidate.PatternType)
        {
            case DatePatternType.DayMonth:
            {
                var parts = raw.Split('/');
                string day = newDate.Day.ToString(parts[0].Length == 2 ? "D2" : "D", System.Globalization.CultureInfo.InvariantCulture);
                string month = newDate.Month.ToString(parts[1].Length == 2 ? "D2" : "D", System.Globalization.CultureInfo.InvariantCulture);
                return $"{day}/{month}";
            }

            case DatePatternType.StandardSlash:
            {
                var parts = raw.Split('/');
                bool useSingleDigit = parts.Length > 0 && parts[0].Length == 1 && !parts[0].StartsWith("0");
                return useSingleDigit 
                    ? $"{newDate.Day}/{newDate.Month}/{newDate.Year}" 
                    : $"{newDate.Day:D2}/{newDate.Month:D2}/{newDate.Year}";
            }

            case DatePatternType.StandardHyphen:
            {
                var parts = raw.Split('-');
                bool useSingleDigit = parts.Length > 0 && parts[0].Length == 1 && !parts[0].StartsWith("0");
                return useSingleDigit 
                    ? $"{newDate.Day}-{newDate.Month}-{newDate.Year}" 
                    : $"{newDate.Day:D2}-{newDate.Month:D2}-{newDate.Year}";
            }

            case DatePatternType.StandardDot:
            {
                var parts = raw.Split('.');
                bool useSingleDigit = parts.Length > 0 && parts[0].Length == 1 && !parts[0].StartsWith("0");
                return useSingleDigit 
                    ? $"{newDate.Day}.{newDate.Month}.{newDate.Year}" 
                    : $"{newDate.Day:D2}.{newDate.Month:D2}.{newDate.Year}";
            }

            case DatePatternType.StandardSpace:
            {
                return $"{newDate.Day:D2} {newDate.Month:D2} {newDate.Year}";
            }

            case DatePatternType.VietnameseFull:
            {
                bool hasNgayPrefix = raw.StartsWith("ngày ", StringComparison.OrdinalIgnoreCase);
                bool startsWithUpper = raw.Length > 0 && char.IsUpper(raw[0]);

                // Kiểm tra định dạng ngày gốc có dùng 1 chữ số hay 2 chữ số
                var match = Regex.Match(raw, @"(?i)(?:ngày\s+)?(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})");
                bool isSingleDigitDay = match.Success && match.Groups[1].Value.Length == 1;
                bool isSingleDigitMonth = match.Success && match.Groups[2].Value.Length == 1;

                string dayStr = isSingleDigitDay ? newDate.Day.ToString() : newDate.Day.ToString("D2");
                string monthStr = isSingleDigitMonth ? newDate.Month.ToString() : newDate.Month.ToString("D2");

                if (hasNgayPrefix)
                {
                    string prefix = startsWithUpper ? "Ngày" : "ngày";
                    return $"{prefix} {dayStr} tháng {monthStr} năm {newDate.Year}";
                }
                else
                {
                    return $"{dayStr} tháng {monthStr} năm {newDate.Year}";
                }
            }

            case DatePatternType.TemplatePlaceholder:
            {
                bool startsWithUpper = raw.Length > 0 && char.IsUpper(raw[0]);
                string prefix = startsWithUpper ? "Ngày" : "ngày";
                return $"{prefix} {newDate.Day:D2} tháng {newDate.Month:D2} năm {newDate.Year}";
            }

            case DatePatternType.VietnameseMonthYear:
            {
                bool startsWithUpper = raw.Length > 0 && char.IsUpper(raw[0]);
                string prefix = startsWithUpper ? "Tháng" : "tháng";
                return $"{prefix} {newDate.Month:D2} năm {newDate.Year}";
            }

            case DatePatternType.VietnameseYearOnly:
            {
                bool startsWithUpper = raw.Length > 0 && char.IsUpper(raw[0]);
                string prefix = startsWithUpper ? "Năm" : "năm";
                return $"{prefix} {newDate.Year}";
            }

            case DatePatternType.AmbiguousShort:
            {
                int shortYear = newDate.Year % 100;
                return $"{newDate.Day:D2}/{newDate.Month:D2}/{shortYear:D2}";
            }

            case DatePatternType.MonthYearDelimited:
            {
                var match = Regex.Match(raw, @"^(\d{1,2})(\D+)(\d{4})$");
                if (match.Success)
                {
                    bool useSingleDigit = match.Groups[1].Value.Length == 1 && !match.Groups[1].Value.StartsWith("0");
                    string sep = match.Groups[2].Value;
                    string monthStr = useSingleDigit 
                        ? newDate.Month.ToString(System.Globalization.CultureInfo.InvariantCulture) 
                        : newDate.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                    return $"{monthStr}{sep}{newDate.Year}";
                }
                return $"{newDate.Month:D2}/{newDate.Year}";
            }

            default:
                return newDate.ToString("dd/MM/yyyy");
        }
    }
}

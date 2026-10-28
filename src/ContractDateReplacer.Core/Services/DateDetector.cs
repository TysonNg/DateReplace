using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Services;

/// <summary>
/// Bộ máy phát hiện các mẫu ngày tháng độc lập hoàn toàn với Word.
/// Hỗ trợ đầy đủ các mẫu ngày tiếng Việt, ngày số phân cách, biểu mẫu điền khuyết và định dạng mơ hồ.
/// </summary>
public class DateDetector : IDateDetector
{
    private const int ContextWindowSize = 100;

    // 1. Mẫu văn bản điền khuyết (Template Placeholders): Ngày ... tháng ... năm ...
    private static readonly Regex RegexTemplatePlaceholder = new(
        @"(?i)(?<raw>(?:[Nn]gày\s+)?(?<day>\d{1,2}|[\._\-]{2,})\s+tháng\s+(?<month>\d{1,2}|[\._\-]{2,})\s+năm\s+(?<year>\d{4}|[\._\-]{2,}))",
        RegexOptions.Compiled);

    // 2. Mẫu ngày tiếng Việt hoàn chỉnh: (ngày)? dd tháng MM năm yyyy
    private static readonly Regex RegexVietnameseFull = new(
        @"(?i)(?<raw>(?:[Nn]gày\s+)?(?<day>\d{1,2})\s+tháng\s+(?<month>\d{1,2})\s+năm\s+(?<year>\d{4}))",
        RegexOptions.Compiled);

    // 3. Mẫu tiền tố ngày có dấu hai chấm hoặc chữ "ngày": Ngày:? dd/MM/yyyy
    private static readonly Regex RegexPrefixedDelimited = new(
        @"(?i)(?<raw>[Nn]gày:?\s*(?<day>\d{1,2})(?<sep>[\/\-\.])(?<month>\d{1,2})\k<sep>(?<year>\d{4}))",
        RegexOptions.Compiled);

    // 4. Mẫu ngày số tiêu chuẩn phân cách bằng /, -, ., hoặc khoảng trắng: dd/MM/yyyy hoặc dd MM yyyy
    private static readonly Regex RegexStandardDelimited = new(
        @"(?<!\d)(?<day>\d{1,2})(?<sep>[\/\-\.\s])(?<month>\d{1,2})\k<sep>(?<year>\d{4}|\d{2})(?!\d)",
        RegexOptions.Compiled);

    // 5. Mẫu một phần: tháng MM năm yyyy
    private static readonly Regex RegexMonthYear = new(
        @"(?i)(?<raw>[Tt]háng\s+(?<month>\d{1,2})\s+năm\s+(?<year>\d{4}))",
        RegexOptions.Compiled);

    // 6. Mẫu một phần: MM/yyyy hoặc MM-yyyy hoặc MM.yyyy hoặc MM/.yyyy (vd: 09/2026, 09/.2026)
    private static readonly Regex RegexMonthYearDelimited = new(
        @"(?<![\w/\-])(?<!\.)(?<month>0?[1-9]|1[0-2])(?<sep>\s*[\/\-\.]\s*|\s*[\/\.]\s*[\/\.]\s*)(?<year>(?:19|20)\d{2})(?![\w/\-]|\.[\w/\-])",
        RegexOptions.Compiled);

    // 7. Mẫu một phần: năm yyyy
    private static readonly Regex RegexYearOnly = new(
        @"(?i)(?<raw>[Nn]ăm\s+(?<year>19\d{2}|20\d{2}))\b",
        RegexOptions.Compiled);

    // Reject identifiers and delimiter chains while allowing sentence-ending punctuation.
    private static readonly Regex RegexDayMonth = new(
        @"(?<![\w/\-])(?<!\.)(?<day>[0-9]{1,2})/(?<month>[0-9]{1,2})(?![\w/\-]|\.[\w/\-])",
        RegexOptions.Compiled);

    public List<DateCandidate> DetectDates(DocumentTextSegment segment)
    {
        if (segment == null || string.IsNullOrWhiteSpace(segment.LogicalText))
        {
            return new List<DateCandidate>();
        }

        return DetectDates(segment.LogicalText, segment.Id);
    }

    public List<DateCandidate> DetectDates(string text, string segmentId = "")
    {
        var rawCandidates = new List<DateCandidate>();
        if (string.IsNullOrWhiteSpace(text)) return rawCandidates;

        // 1. Quét mẫu điền khuyết (Template Placeholders)
        foreach (Match match in RegexTemplatePlaceholder.Matches(text))
        {
            string dayStr = match.Groups["day"].Value;
            string monthStr = match.Groups["month"].Value;
            string yearStr = match.Groups["year"].Value;

            bool hasPlaceholder = IsPlaceholder(dayStr) || IsPlaceholder(monthStr) || IsPlaceholder(yearStr);
            if (!hasPlaceholder) continue; // Để RegexVietnameseFull xử lý nếu là số thực sự

            int? day = int.TryParse(dayStr, out int d) ? d : null;
            int? month = int.TryParse(monthStr, out int m) ? m : null;
            int? year = int.TryParse(yearStr, out int y) ? y : null;

            rawCandidates.Add(CreateCandidate(
                text, segmentId, match.Index, match.Length, match.Value,
                day, month, year, parsedDate: null,
                patternType: DatePatternType.TemplatePlaceholder,
                isComplete: false,
                confidence: 0.85));
        }

        // 2. Quét mẫu tiếng Việt đầy đủ (ngày dd tháng MM năm yyyy)
        foreach (Match match in RegexVietnameseFull.Matches(text))
        {
            if (int.TryParse(match.Groups["day"].Value, out int day) &&
                int.TryParse(match.Groups["month"].Value, out int month) &&
                int.TryParse(match.Groups["year"].Value, out int year))
            {
                var (isValidDate, parsedDate) = TryCreateDateTime(year, month, day);

                rawCandidates.Add(CreateCandidate(
                    text, segmentId, match.Index, match.Length, match.Value,
                    day, month, year, parsedDate,
                    patternType: DatePatternType.VietnameseFull,
                    isComplete: true,
                    confidence: isValidDate ? 0.98 : 0.45));
            }
        }

        // 3. Quét mẫu tiền tố "Ngày:? dd/MM/yyyy"
        foreach (Match match in RegexPrefixedDelimited.Matches(text))
        {
            if (int.TryParse(match.Groups["day"].Value, out int day) &&
                int.TryParse(match.Groups["month"].Value, out int month) &&
                int.TryParse(match.Groups["year"].Value, out int year))
            {
                var (isValidDate, parsedDate) = TryCreateDateTime(year, month, day);
                string sep = match.Groups["sep"].Value;
                var patternType = GetPatternTypeBySeparator(sep);

                rawCandidates.Add(CreateCandidate(
                    text, segmentId, match.Index, match.Length, match.Value,
                    day, month, year, parsedDate,
                    patternType: patternType,
                    isComplete: true,
                    confidence: isValidDate ? 0.98 : 0.45));
            }
        }

        // 4. Quét mẫu số phân cách chuẩn (dd/MM/yyyy, dd-MM-yyyy, dd.MM.yyyy, dd MM yyyy, 01/02/03)
        foreach (Match match in RegexStandardDelimited.Matches(text))
        {
            string dayStr = match.Groups["day"].Value;
            string monthStr = match.Groups["month"].Value;
            string yearStr = match.Groups["year"].Value;
            string sep = match.Groups["sep"].Value;

            if (int.TryParse(dayStr, out int day) &&
                int.TryParse(monthStr, out int month) &&
                int.TryParse(yearStr, out int rawYear))
            {
                int year = rawYear;
                bool isShortYear = yearStr.Length == 2;
                if (isShortYear)
                {
                    // Năm 2 chữ số (vd: 01/02/03 -> năm 2003)
                    year = rawYear >= 50 ? 1900 + rawYear : 2000 + rawYear;
                }

                // Kiểm tra biên ngày tháng hợp lý
                if (day >= 1 && day <= 31 && month >= 1 && month <= 12)
                {
                    var (isValidDate, parsedDate) = TryCreateDateTime(year, month, day);

                    var patternType = isShortYear 
                        ? DatePatternType.AmbiguousShort 
                        : GetPatternTypeBySeparator(sep);

                    double confidence = isShortYear ? 0.50 : (isValidDate ? 0.95 : 0.40);

                    // Nếu dùng khoảng trắng nhưng không phải 4 số năm thì bỏ qua để tránh nhận nhầm số
                    if (sep == " " && isShortYear) continue;

                    rawCandidates.Add(CreateCandidate(
                        text, segmentId, match.Index, match.Length, match.Value,
                        day, month, year, parsedDate,
                        patternType: patternType,
                        isComplete: true,
                        confidence: confidence));
                }
            }
        }

        // 5. Quét mẫu tháng MM năm yyyy
        foreach (Match match in RegexMonthYear.Matches(text))
        {
            if (int.TryParse(match.Groups["month"].Value, out int month) &&
                int.TryParse(match.Groups["year"].Value, out int year))
            {
                if (month >= 1 && month <= 12 && year >= 1900 && year <= 2100)
                {
                    DateTime? parsedDate = new DateTime(year, month, 1);
                    rawCandidates.Add(CreateCandidate(
                        text, segmentId, match.Index, match.Length, match.Value,
                        day: null, month, year, parsedDate,
                        patternType: DatePatternType.VietnameseMonthYear,
                        isComplete: false,
                        confidence: 0.90));
                }
            }
        }

        // 6. Quét mẫu MM/yyyy, MM-yyyy, MM.yyyy, MM/.yyyy
        foreach (Match match in RegexMonthYearDelimited.Matches(text))
        {
            string monthStr = match.Groups["month"].Value;
            string yearStr = match.Groups["year"].Value;
            string sep = match.Groups["sep"].Value;

            if (int.TryParse(monthStr, out int month6) &&
                int.TryParse(yearStr, out int year6))
            {
                if (month6 >= 1 && month6 <= 12 && year6 >= 1900 && year6 <= 2100)
                {
                    // Nếu là dấu chấm đơn lẻ và tháng chỉ có 1 chữ số, yêu cầu ngữ cảnh tháng/kỳ để tránh số thập phân (vd: 3.2026)
                    if (sep.Trim() == "." && monthStr.Length == 1)
                    {
                        int beforeStart = Math.Max(0, match.Index - 20);
                        string prefix = text.Substring(beforeStart, match.Index - beforeStart).ToLowerInvariant();
                        if (!prefix.Contains("tháng") && !prefix.Contains("kỳ") && !prefix.Contains("t"))
                            continue;
                    }

                    DateTime? parsedDate = new DateTime(year6, month6, 1);
                    rawCandidates.Add(CreateCandidate(
                        text, segmentId, match.Index, match.Length, match.Value,
                        day: null, month6, year6, parsedDate,
                        patternType: DatePatternType.MonthYearDelimited,
                        isComplete: false,
                        confidence: 0.88));
                }
            }
        }

        // 7. Quét mẫu năm yyyy
        foreach (Match match in RegexYearOnly.Matches(text))
        {
            if (int.TryParse(match.Groups["year"].Value, out int year))
            {
                DateTime? parsedDate = new DateTime(year, 1, 1);
                rawCandidates.Add(CreateCandidate(
                    text, segmentId, match.Index, match.Length, match.Value,
                    day: null, month: null, year, parsedDate,
                    patternType: DatePatternType.VietnameseYearOnly,
                    isComplete: false,
                    confidence: 0.80));
            }
        }

        foreach (Match match in RegexDayMonth.Matches(text))
        {
            int day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            int month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            // A leap year is used only to validate month lengths; never stored as a year.
            if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month))
                continue;

            rawCandidates.Add(CreateCandidate(
                text, segmentId, match.Index, match.Length, match.Value,
                day, month, year: null, parsedDate: null,
                patternType: DatePatternType.DayMonth, isComplete: false, confidence: 0.80));
        }

        // 7. Giải quyết xung đột và loại bỏ trùng lặp (Deduplication)
        // Ưu tiên: Match dài hơn, hoàn chỉnh hơn và xuất hiện trước
        return ResolveOverlaps(rawCandidates);
    }

    private static bool IsPlaceholder(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return true;
        return val.Contains('.') || val.Contains('_') || val.Contains('-');
    }

    private static (bool IsValid, DateTime? Date) TryCreateDateTime(int year, int month, int day)
    {
        try
        {
            if (year < 1900 || year > 2100) return (false, null);
            if (month < 1 || month > 12) return (false, null);
            int maxDays = DateTime.DaysInMonth(year, month);
            if (day < 1 || day > maxDays) return (false, null);

            return (true, new DateTime(year, month, day));
        }
        catch
        {
            return (false, null);
        }
    }

    private static DatePatternType GetPatternTypeBySeparator(string sep)
    {
        return sep switch
        {
            "/" => DatePatternType.StandardSlash,
            "-" => DatePatternType.StandardHyphen,
            "." => DatePatternType.StandardDot,
            " " => DatePatternType.StandardSpace,
            _ => DatePatternType.StandardSlash
        };
    }

    private static DateCandidate CreateCandidate(
        string text,
        string segmentId,
        int startIndex,
        int length,
        string rawText,
        int? day,
        int? month,
        int? year,
        DateTime? parsedDate,
        DatePatternType patternType,
        bool isComplete,
        double confidence)
    {
        // Trích xuất ngữ cảnh trước (tối đa 100 ký tự)
        int beforeStart = Math.Max(0, startIndex - ContextWindowSize);
        int beforeLen = startIndex - beforeStart;
        string contextBefore = text.Substring(beforeStart, beforeLen);

        // Trích xuất ngữ cảnh sau (tối đa 100 ký tự)
        int afterStart = startIndex + length;
        int afterLen = Math.Min(text.Length - afterStart, ContextWindowSize);
        string contextAfter = text.Substring(afterStart, afterLen);

        return new DateCandidate
        {
            SegmentId = segmentId,
            StartIndex = startIndex,
            Length = length,
            RawText = rawText,
            Day = day,
            Month = month,
            Year = year,
            ParsedDate = parsedDate,
            PatternType = patternType,
            IsCompleteDate = isComplete,
            ContextBefore = contextBefore,
            ContextAfter = contextAfter,
            DetectionConfidence = confidence
        };
    }

    private static List<DateCandidate> ResolveOverlaps(List<DateCandidate> candidates)
    {
        if (candidates.Count <= 1) return candidates;

        // Sắp xếp: Ưu tiên bắt đầu trước, nếu trùng bắt đầu thì lấy cái dài hơn (bao trùm hơn)
        var sorted = candidates
            .OrderBy(c => c.StartIndex)
            .ThenByDescending(c => c.Length)
            .ThenByDescending(c => c.IsCompleteDate)
            .ThenByDescending(c => c.DetectionConfidence)
            .ToList();

        var result = new List<DateCandidate>();
        DateCandidate? current = null;

        foreach (var next in sorted)
        {
            if (current == null)
            {
                current = next;
                continue;
            }

            int currentEnd = current.StartIndex + current.Length;

            // Nếu không trùng lặp vị trí
            if (next.StartIndex >= currentEnd)
            {
                result.Add(current);
                current = next;
            }
            else
            {
                // Có trùng lặp: Ưu tiên ứng viên hoàn chỉnh hơn hoặc có độ tin cậy cao hơn
                if (!current.IsCompleteDate && next.IsCompleteDate)
                {
                    current = next;
                }
                else if (current.IsCompleteDate == next.IsCompleteDate && next.Length > current.Length)
                {
                    current = next;
                }
                // Ngược lại giữ current
            }
        }

        if (current != null)
        {
            result.Add(current);
        }

        return result;
    }
}

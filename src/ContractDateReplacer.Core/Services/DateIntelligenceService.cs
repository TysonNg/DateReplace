using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Services;

/// <summary>
/// Trí thông minh ngày nâng cao: Lịch nghỉ lễ Việt Nam, ngày làm việc và suy luận quan hệ thời gian tương đối.
/// </summary>
public class DateIntelligenceService : IDateIntelligenceService
{
    public bool IsWeekend(DateTime date)
    {
        return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
    }

    public bool IsVietnameseHoliday(DateTime date, out string holidayName)
    {
        holidayName = string.Empty;

        // 1. Các ngày nghỉ lễ dương lịch cố định
        if (date.Month == 1 && date.Day == 1)
        {
            holidayName = "Tết Dương Lịch";
            return true;
        }

        if (date.Month == 4 && date.Day == 30)
        {
            holidayName = "Ngày Giải Phóng Miền Nam (30/04)";
            return true;
        }

        if (date.Month == 5 && date.Day == 1)
        {
            holidayName = "Ngày Quốc Tế Lao Động (01/05)";
            return true;
        }

        if (date.Month == 9)
        {
            if (date.Day == 2)
            {
                holidayName = "Ngày Quốc Khánh (02/09)";
                return true;
            }
            if (date.Day == 1 || date.Day == 3)
            {
                holidayName = "Nghỉ Liền Kề Ngày Quốc Khánh";
                return true;
            }
        }

        // 2. Giỗ Tổ Hùng Vương (10/03 Âm lịch)
        try
        {
            DateTime hungKing = VietnameseLunarCalendar.ConvertLunarToSolar(10, 3, date.Year);
            if (date.Date == hungKing.Date)
            {
                holidayName = "Giỗ Tổ Hùng Vương (10/03 Âm lịch)";
                return true;
            }
        }
        catch { }

        // 3. Tết Nguyên Đán (Khoảng 7 ngày: từ 29/30 tháng Chạp đến hết mùng 5 tháng Giêng)
        int[] checkYears = { date.Year - 1, date.Year, date.Year + 1 };
        foreach (int y in checkYears)
        {
            try
            {
                DateTime tetMung1 = VietnameseLunarCalendar.ConvertLunarToSolar(1, 1, y);
                // Nghỉ từ 2 ngày trước mùng 1 (29, 30 Tết) đến mùng 5 (tổng 7 ngày)
                DateTime tetStart = tetMung1.AddDays(-2);
                DateTime tetEnd = tetMung1.AddDays(4);

                if (date.Date >= tetStart.Date && date.Date <= tetEnd.Date)
                {
                    holidayName = "Kỳ Nghỉ Tết Nguyên Đán";
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    public bool IsBusinessDay(DateTime date)
    {
        return !IsWeekend(date) && !IsVietnameseHoliday(date, out _);
    }

    public DateTime GetNextBusinessDay(DateTime date)
    {
        DateTime current = date;
        while (!IsBusinessDay(current))
        {
            current = current.AddDays(1);
        }
        return current;
    }

    public DateTime AddBusinessDays(DateTime startDate, int businessDays)
    {
        if (businessDays == 0)
        {
            return IsBusinessDay(startDate) ? startDate : GetNextBusinessDay(startDate);
        }

        int step = businessDays > 0 ? 1 : -1;
        int remaining = Math.Abs(businessDays);
        DateTime current = startDate;

        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsBusinessDay(current))
            {
                remaining--;
            }
        }

        return current;
    }

    public DateTime ConvertLunarToSolar(int lunarDay, int lunarMonth, int lunarYear, bool isLeapMonth = false)
    {
        return VietnameseLunarCalendar.ConvertLunarToSolar(lunarDay, lunarMonth, lunarYear, isLeapMonth);
    }

    public IReadOnlyList<RelativeDateClause> ParseRelativeDateClauses(string text, DateTime? baseSigningDate = null)
    {
        var clauses = new List<RelativeDateClause>();
        if (string.IsNullOrWhiteSpace(text)) return clauses;

        DateTime baseDate = baseSigningDate ?? DateTime.Today;

        // Pattern 1: "trong vòng X ngày kể từ ngày (ký|hiệu lực|bàn giao)"
        var matchDays = Regex.Matches(text, @"trong vòng\s+(\d+)\s+ngày\s+kể từ\s+ngày\s+(ký|kí|hiệu lực|bắt đầu|bàn giao|nghiệm thu)", RegexOptions.IgnoreCase);
        foreach (Match m in matchDays)
        {
            int qty = int.Parse(m.Groups[1].Value);
            string refType = m.Groups[2].Value.ToLowerInvariant();
            DateClassificationType classification = refType.Contains("ký") || refType.Contains("kí") 
                ? DateClassificationType.ContractSignDate 
                : DateClassificationType.ContractEffectiveDate;

            clauses.Add(new RelativeDateClause
            {
                SourcePhrase = m.Value,
                ReferenceDateType = classification,
                OffsetType = RelativeOffsetType.CalendarDays,
                OffsetQuantity = qty,
                TargetClassification = DateClassificationType.ContractEndDate,
                CalculatedTargetDate = baseDate.AddDays(qty),
                Explanation = $"Cộng {qty} ngày theo lịch kể từ ngày mốc ({baseDate:dd/MM/yyyy})"
            });
        }

        // Pattern 2: "sau X ngày làm việc" hoặc "trong vòng X ngày làm việc"
        var matchBusiness = Regex.Matches(text, @"(?:trong vòng|sau)\s+(\d+)\s+ngày làm việc(?: kể từ| tính từ)?(?:\s+ngày\s+(ký|kí|bàn giao|nghiệm thu))?", RegexOptions.IgnoreCase);
        foreach (Match m in matchBusiness)
        {
            int qty = int.Parse(m.Groups[1].Value);
            clauses.Add(new RelativeDateClause
            {
                SourcePhrase = m.Value,
                ReferenceDateType = DateClassificationType.ContractSignDate,
                OffsetType = RelativeOffsetType.BusinessDays,
                OffsetQuantity = qty,
                TargetClassification = DateClassificationType.PaymentDate,
                CalculatedTargetDate = AddBusinessDays(baseDate, qty),
                Explanation = $"Cộng {qty} ngày làm việc (bỏ qua Thứ 7, Chủ Nhật và ngày lễ) kể từ ngày mốc ({baseDate:dd/MM/yyyy})"
            });
        }

        // Pattern 3: "X (tháng|năm) kể từ ngày (ký|hiệu lực|bàn giao)"
        var matchPeriod = Regex.Matches(text, @"(\d+)\s+(tháng|năm)\s+kể từ\s+ngày\s+(ký|kí|hiệu lực|bàn giao|nghiệm thu)", RegexOptions.IgnoreCase);
        foreach (Match m in matchPeriod)
        {
            int qty = int.Parse(m.Groups[1].Value);
            string unit = m.Groups[2].Value.ToLowerInvariant();

            if (unit == "tháng")
            {
                clauses.Add(new RelativeDateClause
                {
                    SourcePhrase = m.Value,
                    ReferenceDateType = DateClassificationType.ContractSignDate,
                    OffsetType = RelativeOffsetType.Months,
                    OffsetQuantity = qty,
                    TargetClassification = DateClassificationType.ContractEndDate,
                    CalculatedTargetDate = baseDate.AddMonths(qty),
                    Explanation = $"Thời hạn {qty} tháng kể từ mốc ({baseDate:dd/MM/yyyy})"
                });
            }
            else if (unit == "năm")
            {
                clauses.Add(new RelativeDateClause
                {
                    SourcePhrase = m.Value,
                    ReferenceDateType = DateClassificationType.ContractSignDate,
                    OffsetType = RelativeOffsetType.Years,
                    OffsetQuantity = qty,
                    TargetClassification = DateClassificationType.ContractEndDate,
                    CalculatedTargetDate = baseDate.AddYears(qty),
                    Explanation = $"Thời hạn {qty} năm kể từ mốc ({baseDate:dd/MM/yyyy})"
                });
            }
        }

        // Pattern 4: "định kỳ vào ngày X hàng tháng"
        var matchRecurring = Regex.Matches(text, @"định kỳ vào ngày\s+(\d{1,2})\s+hàng tháng", RegexOptions.IgnoreCase);
        foreach (Match m in matchRecurring)
        {
            int day = int.Parse(m.Groups[1].Value);
            if (day >= 1 && day <= 31)
            {
                DateTime nextDate;
                int daysInMonth = DateTime.DaysInMonth(baseDate.Year, baseDate.Month);
                int targetDay = Math.Min(day, daysInMonth);

                if (baseDate.Day <= targetDay)
                {
                    nextDate = new DateTime(baseDate.Year, baseDate.Month, targetDay);
                }
                else
                {
                    DateTime nextMonth = baseDate.AddMonths(1);
                    int daysInNext = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                    nextDate = new DateTime(nextMonth.Year, nextMonth.Month, Math.Min(day, daysInNext));
                }

                clauses.Add(new RelativeDateClause
                {
                    SourcePhrase = m.Value,
                    ReferenceDateType = DateClassificationType.ContractSignDate,
                    OffsetType = RelativeOffsetType.RecurringMonthly,
                    OffsetQuantity = day,
                    TargetClassification = DateClassificationType.PaymentDate,
                    CalculatedTargetDate = nextDate,
                    Explanation = $"Kỳ thanh toán tiếp theo: ngày {day} hàng tháng ({nextDate:dd/MM/yyyy})"
                });
            }
        }

        return clauses;
    }

    public (bool IsNonWorkingDay, string Reason, DateTime SuggestedNextBusinessDay) AnalyzeDateCompliance(DateTime date)
    {
        if (IsVietnameseHoliday(date, out string holidayName))
        {
            DateTime nextDay = GetNextBusinessDay(date.AddDays(1));
            return (true, $"Ngày {date:dd/MM/yyyy} là {holidayName} (nghỉ lễ). Đề xuất chuyển sang ngày làm việc tiếp theo: {nextDay:dd/MM/yyyy}", nextDay);
        }

        if (IsWeekend(date))
        {
            string dayName = date.DayOfWeek == DayOfWeek.Saturday ? "Thứ Bảy" : "Chủ Nhật";
            DateTime nextDay = GetNextBusinessDay(date.AddDays(1));
            return (true, $"Ngày {date:dd/MM/yyyy} là {dayName} (ngày nghỉ cuối tuần). Đề xuất chuyển sang ngày làm việc tiếp theo: {nextDay:dd/MM/yyyy}", nextDay);
        }

        return (false, "Ngày làm việc tiêu chuẩn", date);
    }
}

using System;
using System.Collections.Generic;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Dịch vụ trí thông minh ngày nâng cao (Lịch Việt Nam, ngày lễ, ngày làm việc, suy luận ngày tương đối).
/// </summary>
public interface IDateIntelligenceService
{
    /// <summary>
    /// Kiểm tra xem ngày có phải ngày nghỉ lễ theo luật lao động Việt Nam không.
    /// </summary>
    bool IsVietnameseHoliday(DateTime date, out string holidayName);

    /// <summary>
    /// Kiểm tra xem ngày có phải Thứ 7 hoặc Chủ Nhật không.
    /// </summary>
    bool IsWeekend(DateTime date);

    /// <summary>
    /// Kiểm tra xem ngày có phải ngày làm việc (không phải cuối tuần và không phải ngày lễ).
    /// </summary>
    bool IsBusinessDay(DateTime date);

    /// <summary>
    /// Tìm ngày làm việc tiếp theo nếu ngày hiện tại rơi vào ngày nghỉ hoặc ngày lễ.
    /// </summary>
    DateTime GetNextBusinessDay(DateTime date);

    /// <summary>
    /// Cộng thêm số ngày làm việc (bỏ qua Thứ 7, Chủ Nhật và các ngày nghỉ lễ Việt Nam).
    /// </summary>
    DateTime AddBusinessDays(DateTime startDate, int businessDays);

    /// <summary>
    /// Chuyển đổi ngày Âm lịch sang Dương lịch Việt Nam.
    /// </summary>
    DateTime ConvertLunarToSolar(int lunarDay, int lunarMonth, int lunarYear, bool isLeapMonth = false);

    /// <summary>
    /// Trích xuất và suy luận các mệnh đề thời gian tương đối trong văn bản hợp đồng.
    /// </summary>
    IReadOnlyList<RelativeDateClause> ParseRelativeDateClauses(string text, DateTime? baseSigningDate = null);

    /// <summary>
    /// Phân tích tính hợp lệ và tuân thủ lịch làm việc của ngày chỉ định.
    /// </summary>
    (bool IsNonWorkingDay, string Reason, DateTime SuggestedNextBusinessDay) AnalyzeDateCompliance(DateTime date);
}

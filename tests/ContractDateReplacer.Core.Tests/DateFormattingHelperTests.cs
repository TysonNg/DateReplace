using System;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Helpers;
using ContractDateReplacer.Core.Models;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class DateFormattingHelperTests
{
    [Theory]
    [InlineData("02/09/2026", DatePatternType.StandardSlash, 2, 9, 2026, "05/10/2026")]
    [InlineData("2/9/2026", DatePatternType.StandardSlash, 2, 9, 2026, "5/10/2026")]
    [InlineData("02-09-2026", DatePatternType.StandardHyphen, 2, 9, 2026, "05-10-2026")]
    [InlineData("02.09.2026", DatePatternType.StandardDot, 2, 9, 2026, "05.10.2026")]
    [InlineData("02 09 2026", DatePatternType.StandardSpace, 2, 9, 2026, "05 10 2026")]
    [InlineData("ngày 02 tháng 09 năm 2026", DatePatternType.VietnameseFull, 2, 9, 2026, "ngày 05 tháng 10 năm 2026")]
    [InlineData("Ngày 2 tháng 9 năm 2026", DatePatternType.VietnameseFull, 2, 9, 2026, "Ngày 5 tháng 10 năm 2026")]
    [InlineData("02 tháng 09 năm 2026", DatePatternType.VietnameseFull, 2, 9, 2026, "05 tháng 10 năm 2026")]
    [InlineData("Ngày ..... tháng ..... năm 2026", DatePatternType.TemplatePlaceholder, null, null, 2026, "Ngày 05 tháng 10 năm 2026")]
    [InlineData("tháng 09 năm 2026", DatePatternType.VietnameseMonthYear, null, 9, 2026, "tháng 10 năm 2026")]
    [InlineData("năm 2026", DatePatternType.VietnameseYearOnly, null, null, 2026, "năm 2026")]
    [InlineData("09/2026", DatePatternType.MonthYearDelimited, null, 9, 2026, "10/2026")]
    [InlineData("9/2026", DatePatternType.MonthYearDelimited, null, 9, 2026, "10/2026")]
    [InlineData("09-2026", DatePatternType.MonthYearDelimited, null, 9, 2026, "10-2026")]
    [InlineData("09.2026", DatePatternType.MonthYearDelimited, null, 9, 2026, "10.2026")]
    [InlineData("09/.2026", DatePatternType.MonthYearDelimited, null, 9, 2026, "10/.2026")]
    [InlineData("09 / 2026", DatePatternType.MonthYearDelimited, null, 9, 2026, "10 / 2026")]
    public void FormatReplacementDate_ShouldPreserveOriginalFormatAndCasing(
        string rawText,
        DatePatternType patternType,
        int? day,
        int? month,
        int year,
        string expectedFormatted)
    {
        var candidate = new DateCandidate
        {
            RawText = rawText,
            PatternType = patternType,
            Day = day,
            Month = month,
            Year = year
        };

        var targetNewDate = new DateTime(2026, 10, 5);
        string result = DateFormattingHelper.FormatReplacementDate(candidate, targetNewDate);

        result.Should().Be(expectedFormatted);
    }
}

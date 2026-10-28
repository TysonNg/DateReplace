using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Helpers;
using ContractDateReplacer.Core.Services;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class PartialDateTests
{
    [Theory]
    [InlineData("1/2", 1, 2)]
    [InlineData("01/02", 1, 2)]
    [InlineData("1/02", 1, 2)]
    [InlineData("01/2", 1, 2)]
    [InlineData("29/2", 29, 2)]
    [InlineData("Ngày 31/12.", 31, 12)]
    public void Partial_dates_have_no_invented_year(string text, int day, int month)
    {
        var candidate = Assert.Single(new DateDetector().DetectDates(text, "segment"));
        Assert.Equal("DayMonth", candidate.PatternType.ToString());
        Assert.Equal(day, candidate.Day);
        Assert.Equal(month, candidate.Month);
        Assert.Null(candidate.Year);
        Assert.Null(candidate.ParsedDate);
        Assert.False(candidate.IsCompleteDate);
        Assert.Equal("segment", candidate.SegmentId);
        Assert.Equal(candidate.RawText, text.Substring(candidate.StartIndex, candidate.Length));
    }

    [Theory]
    [InlineData("31/4")] [InlineData("30/2")] [InlineData("0/2")] [InlineData("1/13")]
    [InlineData("ABC1/2")] [InlineData("1/2ABC")] [InlineData("HD_01/02")]
    [InlineData("HD-01/02")] [InlineData("01/02-HD")]
    [InlineData("1/2/3")] [InlineData("1/2/202")] [InlineData("/1/2")]
    [InlineData("1/2/")] [InlineData("1/2-3")] [InlineData("3-1/2")]
    [InlineData("1/2.3")] [InlineData("3.1/2")]
    public void Invalid_dates_codes_and_chains_do_not_produce_partial_dates(string text)
    {
        Assert.DoesNotContain(new DateDetector().DetectDates(text), c => c.PatternType.ToString() == "DayMonth");
    }

    [Theory]
    [InlineData("01/02/2026")] [InlineData("Ngày 01/02/2026")] [InlineData("01/02/26")]
    public void Full_dates_are_not_duplicated(string text)
    {
        var candidate = Assert.Single(new DateDetector().DetectDates(text));
        Assert.True(candidate.IsCompleteDate);
        Assert.NotNull(candidate.Year);
    }

    [Theory]
    [InlineData("1/2", "3/4")] [InlineData("01/02", "03/04")]
    [InlineData("1/02", "3/04")] [InlineData("01/2", "03/4")]
    public void Replacement_preserves_each_width_without_year(string raw, string expected)
    {
        var candidate = Assert.Single(new DateDetector().DetectDates(raw));
        Assert.Equal(expected, DateFormattingHelper.FormatReplacementDate(candidate, new DateTime(2028, 4, 3)));
    }

    [Fact]
    public void Enum_appends_day_month_without_changing_existing_values()
    {
        Assert.Equal(8, (int)DatePatternType.AmbiguousShort);
        Assert.Equal("DayMonth", Enum.GetName(typeof(DatePatternType), 9));
        Assert.Equal("MonthYearDelimited", Enum.GetName(typeof(DatePatternType), 10));
    }

    [Theory]
    [InlineData("09/2026", 9, 2026)]
    [InlineData("9/2026", 9, 2026)]
    [InlineData("09-2026", 9, 2026)]
    [InlineData("09.2026", 9, 2026)]
    [InlineData("09/.2026", 9, 2026)]
    [InlineData("09 / 2026", 9, 2026)]
    [InlineData("BẢNG KÊ PHÍ DỊCH VỤ BẢO VỆ THÁNG 09/2026", 9, 2026)]
    [InlineData("BẢNG KÊ PHÍ DỊCH VỤ BẢO VỆ THÁNG 09/.2026", 9, 2026)]
    public void MonthYearDelimited_dates_are_detected_correctly(string text, int month, int year)
    {
        var candidate = Assert.Single(new DateDetector().DetectDates(text));
        Assert.Equal(DatePatternType.MonthYearDelimited, candidate.PatternType);
        Assert.Null(candidate.Day);
        Assert.Equal(month, candidate.Month);
        Assert.Equal(year, candidate.Year);
        Assert.False(candidate.IsCompleteDate);
    }

    [Theory]
    [InlineData("HD-09/2026")]
    [InlineData("09/2026/QĐ-UBND")]
    [InlineData("436/59/40 Cách mạng Tháng Tám")]
    public void Non_date_codes_and_addresses_are_not_detected_as_month_year(string text)
    {
        Assert.DoesNotContain(new DateDetector().DetectDates(text), c => c.PatternType == DatePatternType.MonthYearDelimited);
    }

}

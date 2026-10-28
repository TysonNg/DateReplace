using System;
using System.Linq;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class DateDetectorTests
{
    private readonly DateDetector _detector = new();

    [Theory]
    [InlineData("02/09/2026", 2, 9, 2026, DatePatternType.StandardSlash)]
    [InlineData("2/9/2026", 2, 9, 2026, DatePatternType.StandardSlash)]
    [InlineData("02-09-2026", 2, 9, 2026, DatePatternType.StandardHyphen)]
    [InlineData("2-9-2026", 2, 9, 2026, DatePatternType.StandardHyphen)]
    [InlineData("02.09.2026", 2, 9, 2026, DatePatternType.StandardDot)]
    [InlineData("02 09 2026", 2, 9, 2026, DatePatternType.StandardSpace)]
    public void DetectDates_StandardDelimitedFormats_ShouldParseCorrectly(
        string input, int expectedDay, int expectedMonth, int expectedYear, DatePatternType expectedPattern)
    {
        var candidates = _detector.DetectDates(input);

        candidates.Should().HaveCount(1);
        var c = candidates[0];
        c.Day.Should().Be(expectedDay);
        c.Month.Should().Be(expectedMonth);
        c.Year.Should().Be(expectedYear);
        c.ParsedDate.Should().Be(new DateTime(expectedYear, expectedMonth, expectedDay));
        c.PatternType.Should().Be(expectedPattern);
        c.IsCompleteDate.Should().BeTrue();
        c.DetectionConfidence.Should().BeGreaterThanOrEqualTo(0.95);
    }

    [Theory]
    [InlineData("ngày 02 tháng 09 năm 2026", 2, 9, 2026)]
    [InlineData("Ngày 2 tháng 9 năm 2026", 2, 9, 2026)]
    [InlineData("02 tháng 09 năm 2026", 2, 9, 2026)]
    public void DetectDates_VietnameseTextualFormats_ShouldParseCorrectly(
        string input, int expectedDay, int expectedMonth, int expectedYear)
    {
        var candidates = _detector.DetectDates(input);

        candidates.Should().HaveCount(1);
        var c = candidates[0];
        c.Day.Should().Be(expectedDay);
        c.Month.Should().Be(expectedMonth);
        c.Year.Should().Be(expectedYear);
        c.ParsedDate.Should().Be(new DateTime(expectedYear, expectedMonth, expectedDay));
        c.PatternType.Should().Be(DatePatternType.VietnameseFull);
        c.IsCompleteDate.Should().BeTrue();
        c.DetectionConfidence.Should().Be(0.98);
    }

    [Theory]
    [InlineData("ngày 02/09/2026", 2, 9, 2026)]
    [InlineData("Ngày: 02/09/2026", 2, 9, 2026)]
    [InlineData("Ngày: 02-09-2026", 2, 9, 2026)]
    public void DetectDates_PrefixedDelimitedFormats_ShouldParseCorrectly(
        string input, int expectedDay, int expectedMonth, int expectedYear)
    {
        var candidates = _detector.DetectDates(input);

        candidates.Should().HaveCount(1);
        var c = candidates[0];
        c.Day.Should().Be(expectedDay);
        c.Month.Should().Be(expectedMonth);
        c.Year.Should().Be(expectedYear);
        c.IsCompleteDate.Should().BeTrue();
        c.DetectionConfidence.Should().Be(0.98);
    }

    [Fact]
    public void DetectDates_WithLocationPrefix_ShouldRetainLocationInContextBefore()
    {
        string input = "TP.HCM, ngày 02 tháng 09 năm 2026 chúng tôi gồm có các bên:";
        var candidates = _detector.DetectDates(input);

        candidates.Should().HaveCount(1);
        var c = candidates[0];
        c.RawText.Should().Be("ngày 02 tháng 09 năm 2026");
        c.Day.Should().Be(2);
        c.Month.Should().Be(9);
        c.Year.Should().Be(2026);
        c.ContextBefore.Should().Be("TP.HCM, ");
        c.ContextAfter.Should().Be(" chúng tôi gồm có các bên:");
    }

    [Fact]
    public void DetectDates_PartialVietnameseDates_MonthYearAndYearOnly()
    {
        string input = "Hợp đồng có hiệu lực từ tháng 09 năm 2026 đến hết năm 2028.";
        var candidates = _detector.DetectDates(input);

        candidates.Should().HaveCount(2);

        var monthYear = candidates[0];
        monthYear.RawText.Should().Be("tháng 09 năm 2026");
        monthYear.Day.Should().BeNull();
        monthYear.Month.Should().Be(9);
        monthYear.Year.Should().Be(2026);
        monthYear.IsCompleteDate.Should().BeFalse();
        monthYear.PatternType.Should().Be(DatePatternType.VietnameseMonthYear);

        var yearOnly = candidates[1];
        yearOnly.RawText.Should().Be("năm 2028");
        yearOnly.Day.Should().BeNull();
        yearOnly.Month.Should().BeNull();
        yearOnly.Year.Should().Be(2028);
        yearOnly.IsCompleteDate.Should().BeFalse();
        yearOnly.PatternType.Should().Be(DatePatternType.VietnameseYearOnly);
    }

    [Theory]
    [InlineData("Ngày ..... tháng ..... năm 2026", null, null, 2026)]
    [InlineData("Ngày ... tháng ... năm ...", null, null, null)]
    [InlineData("Ngày 02 tháng ... năm 2026", 2, null, 2026)]
    [InlineData("Ngày __ tháng __ năm ____", null, null, null)]
    public void DetectDates_TemplatePlaceholders_ShouldIdentifyAsIncompleteTemplate(
        string input, int? expectedDay, int? expectedMonth, int? expectedYear)
    {
        var candidates = _detector.DetectDates(input);

        candidates.Should().HaveCount(1);
        var c = candidates[0];
        c.Day.Should().Be(expectedDay);
        c.Month.Should().Be(expectedMonth);
        c.Year.Should().Be(expectedYear);
        c.ParsedDate.Should().BeNull();
        c.IsCompleteDate.Should().BeFalse();
        c.PatternType.Should().Be(DatePatternType.TemplatePlaceholder);
        c.DetectionConfidence.Should().Be(0.85);
    }

    [Fact]
    public void DetectDates_AmbiguousShortDate_ShouldHaveLowConfidence()
    {
        string input = "Mã số hồ sơ: 01/02/03 cấp theo quyết định.";
        var candidates = _detector.DetectDates(input);

        candidates.Should().HaveCount(1);
        var c = candidates[0];
        c.RawText.Should().Be("01/02/03");
        c.PatternType.Should().Be(DatePatternType.AmbiguousShort);
        c.DetectionConfidence.Should().Be(0.50);
    }

    [Fact]
    public void DetectDates_LeapYearCalendarValidity_ShouldDifferentiateValidAndInvalidDates()
    {
        // 29/02/2024: Năm nhuận -> Hợp lệ
        var validLeap = _detector.DetectDates("Hạn chót: 29/02/2024.");
        validLeap.Should().HaveCount(1);
        validLeap[0].ParsedDate.Should().Be(new DateTime(2024, 2, 29));
        validLeap[0].DetectionConfidence.Should().Be(0.95);

        // 29/02/2025: Không phải năm nhuận -> Lịch không hợp lệ -> Confidence thấp
        var invalidLeap = _detector.DetectDates("Hạn chót: 29/02/2025.");
        invalidLeap.Should().HaveCount(1);
        invalidLeap[0].ParsedDate.Should().BeNull();
        invalidLeap[0].DetectionConfidence.Should().Be(0.40);

        // 31/04/2026: Tháng 4 chỉ có 30 ngày -> Lịch không hợp lệ
        var invalidDay = _detector.DetectDates("Hạn chót: 31/04/2026.");
        invalidDay.Should().HaveCount(1);
        invalidDay[0].ParsedDate.Should().BeNull();
        invalidDay[0].DetectionConfidence.Should().Be(0.40);
    }

    [Fact]
    public void DetectDates_ContextExtraction_ShouldCapAt100Characters()
    {
        string longPrefix = new string('A', 150);
        string longSuffix = new string('B', 150);
        string text = $"{longPrefix} 15/08/2026 {longSuffix}";

        var candidates = _detector.DetectDates(text);

        candidates.Should().HaveCount(1);
        var c = candidates[0];
        c.ContextBefore.Length.Should().Be(100);
        c.ContextBefore.Should().Be(new string('A', 99) + " ");
        c.ContextAfter.Length.Should().Be(100);
        c.ContextAfter.Should().Be(" " + new string('B', 99));
    }

    [Fact]
    public void DetectDates_FromDocumentTextSegment_ShouldPopulateSegmentId()
    {
        var seg = new DocumentTextSegment
        {
            Id = "SEG_12345",
            LogicalText = "Hà Nội, ngày 05 tháng 11 năm 2026 hai bên đồng ý ký kết."
        };

        var candidates = _detector.DetectDates(seg);

        candidates.Should().HaveCount(1);
        candidates[0].SegmentId.Should().Be("SEG_12345");
        candidates[0].RawText.Should().Be("ngày 05 tháng 11 năm 2026");
        candidates[0].ParsedDate.Should().Be(new DateTime(2026, 11, 5));
    }
}

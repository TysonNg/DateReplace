using System;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class DateIntelligenceTests
{
    private readonly DateIntelligenceService _service = new();

    [Fact]
    public void ConvertLunarToSolar_CalculatesAccuratelyForVietnameseTraditionalHolidays()
    {
        // 1. Giỗ Tổ Hùng Vương (10/03 Âm lịch):
        // Năm 2024 (Giáp Thìn): 10/03 Âm lịch là ngày 18/04/2024 Dương lịch
        var solarHungKing2024 = _service.ConvertLunarToSolar(10, 3, 2024);
        solarHungKing2024.Date.Should().Be(new DateTime(2024, 4, 18));

        // 2. Mùng 1 Tết Bính Ngọ 2026: 01/01 Âm lịch là ngày 17/02/2026 Dương lịch
        var tet2026 = _service.ConvertLunarToSolar(1, 1, 2026);
        tet2026.Date.Should().Be(new DateTime(2026, 2, 17));
    }

    [Fact]
    public void IsVietnameseHoliday_IdentifiesNationalHolidaysAccurately()
    {
        // Tết Dương Lịch (01/01)
        _service.IsVietnameseHoliday(new DateTime(2026, 1, 1), out string h1).Should().BeTrue();
        h1.Should().Contain("Tết Dương Lịch");

        // Giải Phóng Miền Nam (30/04)
        _service.IsVietnameseHoliday(new DateTime(2026, 4, 30), out string h2).Should().BeTrue();
        h2.Should().Contain("Giải Phóng");

        // Quốc Tế Lao Động (01/05)
        _service.IsVietnameseHoliday(new DateTime(2026, 5, 1), out string h3).Should().BeTrue();
        h3.Should().Contain("Lao Động");

        // Quốc Khánh (02/09)
        _service.IsVietnameseHoliday(new DateTime(2026, 9, 2), out string h4).Should().BeTrue();
        h4.Should().Contain("Quốc Khánh");

        // Ngày thường không phải nghỉ lễ (15/07)
        _service.IsVietnameseHoliday(new DateTime(2026, 7, 15), out string h5).Should().BeFalse();
        h5.Should().BeEmpty();
    }

    [Fact]
    public void AddBusinessDays_SkipsWeekendsAndHolidaysCorrectly()
    {
        // 07/08/2026 là Thứ Sáu
        DateTime friday = new DateTime(2026, 8, 7);

        // Cộng 1 ngày làm việc -> phải bỏ qua Thứ Bảy (08/08) và Chủ Nhật (09/08), thành Thứ Hai (10/08/2026)
        DateTime monday = _service.AddBusinessDays(friday, 1);
        monday.Should().Be(new DateTime(2026, 8, 10));

        // Cộng 5 ngày làm việc từ Thứ Hai (10/08/2026) -> Thứ Hai tuần tiếp (17/08/2026)
        DateTime nextMonday = _service.AddBusinessDays(monday, 5);
        nextMonday.Should().Be(new DateTime(2026, 8, 17));
    }

    [Fact]
    public void ParseRelativeDateClauses_ExtractsAndCalculatesTargetDates()
    {
        string contractClause = "Hợp đồng có hiệu lực trong vòng 30 ngày kể từ ngày ký và thanh toán sau 15 ngày làm việc.";
        DateTime signingDate = new DateTime(2026, 6, 1); // Thứ Hai

        var clauses = _service.ParseRelativeDateClauses(contractClause, signingDate);

        clauses.Should().HaveCount(2);

        // Mệnh đề 1: trong vòng 30 ngày kể từ ngày ký (Lịch thường)
        var c1 = clauses[0];
        c1.OffsetType.Should().Be(RelativeOffsetType.CalendarDays);
        c1.OffsetQuantity.Should().Be(30);
        c1.CalculatedTargetDate.Should().Be(new DateTime(2026, 7, 1));

        // Mệnh đề 2: sau 15 ngày làm việc (Ngày làm việc)
        var c2 = clauses[1];
        c2.OffsetType.Should().Be(RelativeOffsetType.BusinessDays);
        c2.OffsetQuantity.Should().Be(15);
        c2.CalculatedTargetDate.Should().BeAfter(new DateTime(2026, 6, 15));
    }

    [Fact]
    public void AnalyzeDateCompliance_WhenDateIsWeekendOrHoliday_SuggestsNextBusinessDay()
    {
        // 1. Ngày cuối tuần (08/08/2026 là Thứ Bảy)
        DateTime saturday = new DateTime(2026, 8, 8);
        var resSat = _service.AnalyzeDateCompliance(saturday);

        resSat.IsNonWorkingDay.Should().BeTrue();
        resSat.Reason.Should().Contain("Thứ Bảy");
        resSat.SuggestedNextBusinessDay.Should().Be(new DateTime(2026, 8, 10)); // Thứ Hai

        // 2. Ngày Quốc Khánh (02/09/2026)
        DateTime natHoliday = new DateTime(2026, 9, 2);
        var resHol = _service.AnalyzeDateCompliance(natHoliday);

        resHol.IsNonWorkingDay.Should().BeTrue();
        resHol.Reason.Should().Contain("Quốc Khánh");
        _service.IsBusinessDay(resHol.SuggestedNextBusinessDay).Should().BeTrue();
    }
}

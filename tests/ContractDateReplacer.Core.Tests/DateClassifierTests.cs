using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class DateClassifierTests
{
    private readonly DateDetector _detector = new();
    private readonly DateClassifier _classifier = new();

    private DateCandidate DetectSingle(string text)
    {
        var candidates = _detector.DetectDates(text);
        candidates.Should().HaveCount(1);
        return candidates[0];
    }

    [Theory]
    [InlineData("Hôm nay, ngày 02 tháng 09 năm 2026, tại trụ sở công ty")]
    [InlineData("Hợp đồng này được lập ngày 02/09/2026 giữa các bên:")]
    [InlineData("TP.HCM, ngày 02 tháng 09 năm 2026 chúng tôi gồm có:")]
    [InlineData("Hà Nội, ngày 15/10/2026 hai bên thống nhất ký kết")]
    public void Classify_ContractSignDate_ShouldClassifyCorrectlyAndRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.ContractSignDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
        result.IsRecommendedForReplacement.Should().BeTrue();
    }

    [Theory]
    [InlineData("Hợp đồng có hiệu lực từ ngày 01/10/2026 cho đến khi thanh lý.")]
    [InlineData("Hiệu lực thi hành kể từ ngày 01 tháng 10 năm 2026.")]
    public void Classify_EffectiveDate_ShouldClassifyCorrectlyAndRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.ContractEffectiveDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
        result.IsRecommendedForReplacement.Should().BeTrue();
    }

    [Theory]
    [InlineData("Hợp đồng có giá trị đến ngày 31/12/2027.")]
    [InlineData("Thời hạn hợp đồng kết thúc vào ngày 31 tháng 12 năm 2027.")]
    public void Classify_ExpirationDate_ShouldClassifyCorrectlyAndRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.ContractEndDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
        result.IsRecommendedForReplacement.Should().BeTrue();
    }

    [Theory]
    [InlineData("Bên A: Ông Nguyễn Văn A, sinh ngày: 01/01/1990 tại Hà Nội.")]
    [InlineData("Người đại diện: Bà Trần Thị B, ngày sinh 15/08/1985.")]
    public void Classify_BirthDate_ShouldClassifyCorrectlyAndNEVERRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.BirthDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);

        // BẢO VỆ DỮ LIỆU CÁ NHÂN: Tuyệt đối không khuyến nghị thay thế ngày sinh!
        result.IsRecommendedForReplacement.Should().BeFalse();
    }

    [Theory]
    [InlineData("CCCD số 0123456789 do Cục Cảnh sát cấp ngày 15/05/2021.")]
    [InlineData("CMND số 123456789, ngày cấp: 20/10/2018 tại Công an TP.HCM.")]
    public void Classify_IdentityIssueDate_ShouldClassifyCorrectlyAndNEVERRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.IdentityIssueDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);

        // BẢO VỆ DỮ LIỆU CÁ NHÂN: Tuyệt đối không khuyến nghị thay thế ngày cấp CCCD!
        result.IsRecommendedForReplacement.Should().BeFalse();
    }

    [Theory]
    [InlineData("Bên B có nghĩa vụ thanh toán đợt 1 trước ngày 15/10/2026.")]
    [InlineData("Hạn thanh toán đợt 2: 20/11/2026 vào tài khoản ngân hàng.")]
    public void Classify_PaymentDueDate_ShouldClassifyCorrectlyAndRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.PaymentDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
        result.IsRecommendedForReplacement.Should().BeTrue();
    }

    [Theory]
    [InlineData("Bên B bàn giao công trình vào ngày 30/11/2026 cho Bên A.")]
    [InlineData("Thời hạn bàn giao thiết bị nghiệm thu vào ngày 05/12/2026.")]
    public void Classify_HandoverDate_ShouldClassifyCorrectlyAndRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.HandoverDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
        result.IsRecommendedForReplacement.Should().BeTrue();
    }

    [Theory]
    [InlineData("Chi tiết được quy định tại phụ lục số 01 ký phụ lục ngày 10/10/2026.")]
    [InlineData("Kèm theo phụ lục hợp đồng ngày 12/10/2026 giữa hai bên.")]
    public void Classify_AppendixDate_ShouldClassifyCorrectlyAndRecommendReplacement(string text)
    {
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.AppendixDate);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
        result.IsRecommendedForReplacement.Should().BeTrue();
    }

    [Fact]
    public void Classify_UnknownDate_WithoutContextKeywords_ShouldBeUnknownAndNotRecommended()
    {
        string text = "Mã số định danh lô hàng 05/06/2026 được niêm phong.";
        var candidate = DetectSingle(text);
        var result = _classifier.Classify(candidate);

        result.Type.Should().Be(DateClassificationType.Unknown);
        result.Confidence.Should().BeLessThan(0.70);
        result.IsRecommendedForReplacement.Should().BeFalse();
        result.IsReviewRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyBatchAsync_ShouldClassifyAllCandidatesInList()
    {
        var list = new List<DateCandidate>
        {
            DetectSingle("Hôm nay, ngày 02/09/2026 hai bên ký kết."),
            DetectSingle("Sinh ngày: 01/01/1990."),
            DetectSingle("Thanh toán đợt 1 trước ngày 15/10/2026.")
        };

        await _classifier.ClassifyBatchAsync(list);

        list[0].Classification.Type.Should().Be(DateClassificationType.ContractSignDate);
        list[0].Classification.IsRecommendedForReplacement.Should().BeTrue();

        list[1].Classification.Type.Should().Be(DateClassificationType.BirthDate);
        list[1].Classification.IsRecommendedForReplacement.Should().BeFalse();

        list[2].Classification.Type.Should().Be(DateClassificationType.PaymentDate);
        list[2].Classification.IsRecommendedForReplacement.Should().BeTrue();
    }
}



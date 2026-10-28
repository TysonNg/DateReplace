using System.Collections.Generic;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class ContractFileItemTests
{
    [Fact]
    public void ContractFileItem_ShouldFormatFileSizeCorrectly()
    {
        var itemSmall = new ContractFileItem { FileSizeBytes = 500 };
        itemSmall.FormattedFileSize.Should().Be("500 B");

        var itemKb = new ContractFileItem { FileSizeBytes = 1024 * 15 };
        itemKb.FormattedFileSize.Should().Be("15.0 KB");

        var itemMb = new ContractFileItem { FileSizeBytes = 1024 * 1024 * 3 + 512 * 1024 };
        itemMb.FormattedFileSize.Should().Be("3.5 MB");
    }

    [Theory]
    [InlineData(FileScanStatus.NotScanned, "CHƯA QUÉT", "Secondary")]
    [InlineData(FileScanStatus.Scanning, "ĐANG QUÉT", "Info")]
    [InlineData(FileScanStatus.Pass, "✓ AN TOÀN", "Success")]
    [InlineData(FileScanStatus.ReviewRequired, "⚠ CẦN KIỂM TRA", "Caution")]
    [InlineData(FileScanStatus.Error, "✕ LỖI", "Danger")]
    [InlineData(FileScanStatus.Completed, "✓ HOÀN TẤT", "Success")]
    public void ContractFileItem_StatusBadges_ShouldMatchSpecification(
        FileScanStatus status, string expectedBadgeText, string expectedAppearance)
    {
        var item = new ContractFileItem { Status = status };

        item.StatusBadgeText.Should().Be(expectedBadgeText);
        item.StatusBadgeAppearance.Should().Be(expectedAppearance);
    }

    [Fact]
    public void ContractFileItem_PropertyChanged_ShouldFireOnStatusChange()
    {
        var item = new ContractFileItem();
        var changedProperties = new List<string>();
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        item.Status = FileScanStatus.Scanning;
        changedProperties.Should().Contain(nameof(ContractFileItem.Status));
        changedProperties.Should().Contain(nameof(ContractFileItem.StatusBadgeText));
        changedProperties.Should().Contain(nameof(ContractFileItem.StatusBadgeAppearance));
        changedProperties.Should().Contain(nameof(ContractFileItem.SummaryText));

        item.IsSelected = false;
        changedProperties.Should().Contain(nameof(ContractFileItem.IsSelected));
    }

    [Fact]
    public void ContractFileItem_SummaryText_ShouldReflectDetailedState()
    {
        var item = new ContractFileItem
        {
            Status = FileScanStatus.Error,
            StatusMessage = "Không thể đọc một TextBox trong file"
        };
        item.SummaryText.Should().Be("Không thể đọc một TextBox trong file");

        item.Status = FileScanStatus.Pass;
        item.ReplaceableDatesCount = 4;
        item.DetectedCandidates.Add(new DateCandidate());
        item.DetectedCandidates.Add(new DateCandidate());
        item.DetectedCandidates.Add(new DateCandidate());
        item.DetectedCandidates.Add(new DateCandidate());
        item.DetectedCandidates.Add(new DateCandidate());
        item.DetectedCandidates.Add(new DateCandidate());
        item.DetectedCandidates.Add(new DateCandidate());

        item.SummaryText.Should().Be("7 ngày phát hiện • 4 ngày có thể thay");
    }
}

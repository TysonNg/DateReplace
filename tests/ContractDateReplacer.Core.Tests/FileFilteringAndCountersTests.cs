using System;
using System.Collections.Generic;
using System.Linq;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class FileFilteringAndCountersTests
{
    private List<ContractFileItem> CreateSampleFiles()
    {
        return new List<ContractFileItem>
        {
            new() { FilePath = @"D:\Contracts\HD_NguyenVanA.docx", Status = FileScanStatus.Pass, IsSelected = true },
            new() { FilePath = @"D:\Contracts\HD_TranVanB.docx", Status = FileScanStatus.ReviewRequired, IsSelected = true },
            new() { FilePath = @"D:\Contracts\HD_CongTyABC.docx", Status = FileScanStatus.Error, IsSelected = false },
            new() { FilePath = @"D:\Contracts\HD_LeThiC.docx", Status = FileScanStatus.NotScanned, IsSelected = true },
            new() { FilePath = @"D:\Contracts\SubFolder\HD_PhamVanD.docx", Status = FileScanStatus.Pass, IsSelected = true },
        };
    }

    [Theory]
    [InlineData("All", 5)]
    [InlineData("Pass", 2)]
    [InlineData("Review", 1)]
    [InlineData("Error", 1)]
    public void Filtering_ByStatus_ShouldReturnMatchingItems(string filter, int expectedCount)
    {
        var files = CreateSampleFiles();

        var filtered = files.Where(item => filter switch
        {
            "Pass" => item.Status == FileScanStatus.Pass || item.Status == FileScanStatus.Completed,
            "Review" => item.Status == FileScanStatus.ReviewRequired,
            "Error" => item.Status == FileScanStatus.Error,
            _ => true
        }).ToList();

        filtered.Count.Should().Be(expectedCount);
    }

    [Theory]
    [InlineData("NguyenVanA", 1)]
    [InlineData("HD_", 5)]
    [InlineData("SubFolder", 1)]
    [InlineData("xyz_non_existent", 0)]
    public void Filtering_BySearchKeyword_ShouldMatchFileNameOrPath(string keyword, int expectedCount)
    {
        var files = CreateSampleFiles();

        var filtered = files.Where(item =>
            item.FileName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            item.FilePath.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        filtered.Count.Should().Be(expectedCount);
    }

    [Fact]
    public void FileCounters_ShouldCalculateAccurately()
    {
        var files = CreateSampleFiles();

        int total = files.Count;
        int selected = files.Count(f => f.IsSelected);
        int pass = files.Count(f => f.Status == FileScanStatus.Pass || f.Status == FileScanStatus.Completed);
        int review = files.Count(f => f.Status == FileScanStatus.ReviewRequired);
        int error = files.Count(f => f.Status == FileScanStatus.Error);
        int notScanned = files.Count(f => f.Status == FileScanStatus.NotScanned);

        total.Should().Be(5);
        selected.Should().Be(4);
        pass.Should().Be(2);
        review.Should().Be(1);
        error.Should().Be(1);
        notScanned.Should().Be(1);
    }

    [Fact]
    public void DuplicateFileCheck_ShouldIdentifyIdenticalPaths()
    {
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\Contracts\HD_NguyenVanA.docx"
        };

        bool isDuplicate = existingPaths.Contains(@"d:\contracts\hd_nguyenvana.docx");
        isDuplicate.Should().BeTrue();

        bool isDifferent = existingPaths.Contains(@"D:\Contracts\HD_NguyenVanB.docx");
        isDifferent.Should().BeFalse();
    }
}

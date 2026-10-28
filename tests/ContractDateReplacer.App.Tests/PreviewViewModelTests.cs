using System;
using System.Collections.Generic;
using System.Linq;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class PreviewViewModelTests
{
    private List<DateItemViewModel> CreateSampleDateItems()
    {
        var file = new ContractFileItem
        {
            FilePath = @"D:\Contracts\HD_01.docx",
            Status = FileScanStatus.Pass
        };

        var segment = new DocumentTextSegment
        {
            ParagraphIndex = 3,
            SourceLocation = "Nội dung chính, Đoạn 3"
        };

        // Mục 1: Ngày ký (Được chọn thay)
        var candidate1 = new DateCandidate
        {
            RawText = "ngày 02 tháng 09 năm 2026",
            PatternType = DatePatternType.VietnameseFull,
            Day = 2,
            Month = 9,
            Year = 2026,
            ContextBefore = "Hôm nay, ",
            ContextAfter = ", tại trụ sở công ty",
            Classification = new ClassificationResult
            {
                Type = DateClassificationType.ContractSignDate,
                Confidence = 0.98,
                IsRecommendedForReplacement = true
            }
        };
        var item1 = new DateItemViewModel(candidate1, file, segment)
        {
            IsSelected = true,
            TargetNewDate = new DateTime(2026, 10, 5)
        };

        // Mục 2: Ngày sinh (KHÔNG chọn thay)
        var candidate2 = new DateCandidate
        {
            RawText = "01/01/1990",
            PatternType = DatePatternType.StandardSlash,
            Day = 1,
            Month = 1,
            Year = 1990,
            ContextBefore = "Ông Nguyễn Văn A, sinh ngày ",
            ContextAfter = " tại TP.HCM",
            Classification = new ClassificationResult
            {
                Type = DateClassificationType.BirthDate,
                Confidence = 0.99,
                IsRecommendedForReplacement = false
            }
        };
        var item2 = new DateItemViewModel(candidate2, file, segment)
        {
            IsSelected = false
        };

        return new List<DateItemViewModel> { item1, item2 };
    }

    [Fact]
    public void DiffItemViewModel_ShouldGenerateCorrectDiffTextForSideBySideAndInline()
    {
        var items = CreateSampleDateItems();
        var diffVm = new DiffItemViewModel(items[0]);

        diffVm.IsChanged.Should().BeTrue();
        diffVm.OldDateText.Should().Be("ngày 02 tháng 09 năm 2026");
        diffVm.NewDateText.Should().Be("ngày 05 tháng 10 năm 2026");

        // Side-by-side
        diffVm.SideBySideBeforeDisplay.Should().Contain("ngày 02 tháng 09 năm 2026");
        diffVm.SideBySideAfterDisplay.Should().Contain("ngày 05 tháng 10 năm 2026");

        // Inline
        diffVm.InlinePrefix.Should().Contain("Hôm nay, ");
        diffVm.InlineOldText.Should().Be("ngày 02 tháng 09 năm 2026");
        diffVm.InlineNewText.Should().Be("ngày 05 tháng 10 năm 2026");
        diffVm.InlineSuffix.Should().Contain(", tại trụ sở công ty");
    }

    [Fact]
    public void PreviewViewModel_ViewModeSwitching_ShouldToggleModes()
    {
        var vm = new PreviewViewModel();
        vm.IsSideBySideMode.Should().BeTrue();
        vm.IsInlineMode.Should().BeFalse();

        // Chuyển sang Inline
        vm.SetInlineMode();
        vm.IsSideBySideMode.Should().BeFalse();
        vm.IsInlineMode.Should().BeTrue();

        // Chuyển lại SideBySide
        vm.SetSideBySideMode();
        vm.IsSideBySideMode.Should().BeTrue();
        vm.IsInlineMode.Should().BeFalse();
    }

    [Fact]
    public void PreviewViewModel_OnlyShowChangedFilter_ShouldFilterUnchangedRows()
    {
        var vm = new PreviewViewModel();
        vm.LoadDiffItems(CreateSampleDateItems());

        // Mặc định OnlyShowChanged = true -> Chỉ có 1 mục thay đổi được hiển thị
        vm.OnlyShowChanged.Should().BeTrue();
        var visibleChangedOnly = vm.FilteredDiffItemsView.Cast<DiffItemViewModel>().ToList();
        visibleChangedOnly.Should().HaveCount(1);
        visibleChangedOnly[0].IsChanged.Should().BeTrue();

        // Bỏ chọn OnlyShowChanged -> Hiển thị toàn bộ 2 mục
        vm.OnlyShowChanged = false;
        var visibleAll = vm.FilteredDiffItemsView.Cast<DiffItemViewModel>().ToList();
        visibleAll.Should().HaveCount(2);
    }

    [Fact]
    public void PreviewViewModel_SafetyConfirmation_RequiresUserAgreementToExecute()
    {
        var vm = new PreviewViewModel();
        vm.LoadDiffItems(CreateSampleDateItems());

        // Chưa tick xác nhận -> Không thể thực hiện thay thế
        vm.IsConfirmedByUser.Should().BeFalse();
        vm.CanExecuteReplacement.Should().BeFalse();

        // Tick xác nhận đồng ý -> Nút thực hiện được kích hoạt
        vm.IsConfirmedByUser = true;
        vm.CanExecuteReplacement.Should().BeTrue();

        bool executeTriggered = false;
        vm.RequestExecuteReplacement += () => executeTriggered = true;

        vm.ExecuteReplacement();
        executeTriggered.Should().BeTrue();
    }

    [Fact]
    public void PreviewViewModel_NavigationBack_ShouldTriggerEvent()
    {
        var vm = new PreviewViewModel();
        vm.LoadDiffItems(CreateSampleDateItems());

        bool backTriggered = false;
        vm.RequestNavigateBack += () => backTriggered = true;

        vm.NavigateBack();
        backTriggered.Should().BeTrue();
    }

    [Fact]
    public void PreviewViewModel_HasDiffItems_ShouldFollowTheFilteredResults()
    {
        var vm = new PreviewViewModel();
        vm.LoadDiffItems(CreateSampleDateItems());

        vm.HasDiffItems.Should().BeTrue();

        vm.SearchKeyword = "không có kết quả phù hợp";
        vm.HasDiffItems.Should().BeFalse();

        vm.SearchKeyword = string.Empty;
        vm.HasDiffItems.Should().BeTrue();
    }

    [Fact]
    public void DiffItemViewModel_SelectedWithoutTargetDate_ShouldNotBeMarkedChanged()
    {
        var item = CreateSampleDateItems()[0];
        item.TargetNewDate = null;

        var diff = new DiffItemViewModel(item);

        diff.IsChanged.Should().BeFalse();
        diff.NewDateText.Should().Be(diff.OldDateText);
    }

    [Fact]
    public void PreviewViewModel_DuplicateFileNames_ShouldFilterAndCountByFullPath()
    {
        var first = CreateSampleDateItems()[0];
        var secondFile = new ContractFileItem { FilePath = @"E:\Archive\HD_01.docx", Status = FileScanStatus.Pass };
        var second = new DateItemViewModel(first.Candidate, secondFile, first.Segment)
        {
            IsSelected = true,
            TargetNewDate = first.TargetNewDate
        };
        var vm = new PreviewViewModel();

        vm.LoadDiffItems(new[] { first, second });

        vm.FilesAffectedCount.Should().Be(2);
        vm.FileFilterOptions.Should().HaveCount(3);
        vm.SelectedFileFilter = vm.FileFilterOptions.Last();
        vm.FilteredDiffItemsView.Cast<DiffItemViewModel>().Should().ContainSingle(x => x.FilePath == second.FilePath);
    }

    [Fact]
    public void DateItemViewModel_TargetFormattingToOriginalText_ShouldNotBeAnEffectiveChange()
    {
        var item = CreateSampleDateItems()[0];
        item.TargetNewDate = new DateTime(2026, 9, 2);

        item.FormattedReplacementText.Should().Be(item.RawText);
        item.HasEffectiveChange.Should().BeFalse();

        item.TargetNewDate = new DateTime(2026, 10, 5);
        item.HasEffectiveChange.Should().BeTrue();
    }

    [Fact]
    public void PreviewViewModel_SelectedFileFilterNull_ShouldNotThrowAndTreatAsAllFiles()
    {
        var vm = new PreviewViewModel();
        vm.LoadDiffItems(CreateSampleDateItems());

        // Giả lập ComboBox reset SelectedItem về null
        vm.SelectedFileFilter = null;

        var act = () => vm.FilteredDiffItemsView.Refresh();
        act.Should().NotThrow();
        vm.FilteredDiffItemsView.Cast<DiffItemViewModel>().Should().HaveCount(1);
    }

    [Fact]
    public void PreviewViewModel_LoadDiffItems_WhenSelectedFileFilterWasNull_ShouldSucceed()
    {
        var vm = new PreviewViewModel();
        vm.SelectedFileFilter = null;

        var act = () => vm.LoadDiffItems(CreateSampleDateItems());
        act.Should().NotThrow();
        vm.SelectedFileFilter.Should().Be("Tất cả các file");
    }
}

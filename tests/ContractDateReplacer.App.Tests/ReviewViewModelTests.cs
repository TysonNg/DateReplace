using System;
using System.Collections.Generic;
using System.Linq;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class ReviewViewModelTests
{
    private (ContractFileItem File, DocumentTextSegment Segment, List<DateCandidate> Candidates) CreateSampleDetectedData()
    {
        var file = new ContractFileItem
        {
            FilePath = @"D:\Contracts\HD_MuaBan_2026.docx",
            Status = FileScanStatus.Pass
        };

        var segment = new DocumentTextSegment
        {
            ParagraphIndex = 1,
            SourceLocation = "Nội dung chính, Đoạn 1"
        };

        var candidates = new List<DateCandidate>
        {
            // 1. Ngày ký (đề xuất thay: true)
            new()
            {
                RawText = "ngày 02 tháng 09 năm 2026",
                ContextBefore = "Hôm nay, ",
                ContextAfter = " chúng tôi gồm có:",
                Classification = new ClassificationResult
                {
                    Type = DateClassificationType.ContractSignDate,
                    Confidence = 0.98,
                    IsRecommendedForReplacement = true
                }
            },
            // 2. Ngày hiệu lực (đề xuất thay: true)
            new()
            {
                RawText = "01/10/2026",
                ContextBefore = "Hợp đồng có hiệu lực từ ",
                ContextAfter = " đến khi kết thúc.",
                Classification = new ClassificationResult
                {
                    Type = DateClassificationType.ContractEffectiveDate,
                    Confidence = 0.96,
                    IsRecommendedForReplacement = true
                }
            },
            // 3. Ngày sinh (đề xuất thay: false - BẢO VỆ NHÂN THÂN)
            new()
            {
                RawText = "01/01/1990",
                ContextBefore = "Bên A sinh ngày: ",
                ContextAfter = " tại Hà Nội.",
                Classification = new ClassificationResult
                {
                    Type = DateClassificationType.BirthDate,
                    Confidence = 0.99,
                    IsRecommendedForReplacement = false
                }
            },
            // 4. Ngày cấp CCCD (đề xuất thay: false - BẢO VỆ NHÂN THÂN)
            new()
            {
                RawText = "15/05/2021",
                ContextBefore = "CCCD số 0123456789 cấp ngày ",
                ContextAfter = " bởi Cục CS QLHC.",
                Classification = new ClassificationResult
                {
                    Type = DateClassificationType.IdentityIssueDate,
                    Confidence = 0.98,
                    IsRecommendedForReplacement = false
                }
            },
            // 5. Ngày thanh toán (đề xuất thay: true)
            new()
            {
                RawText = "20/11/2026",
                ContextBefore = "Thanh toán đợt 1 trước ",
                ContextAfter = " vào tài khoản.",
                Classification = new ClassificationResult
                {
                    Type = DateClassificationType.PaymentDate,
                    Confidence = 0.94,
                    IsRecommendedForReplacement = true
                }
            }
        };

        return (file, segment, candidates);
    }

    [Fact]
    public void DateItemViewModel_DefaultSelection_ShouldFollowRecommendations()
    {
        var (file, segment, candidates) = CreateSampleDetectedData();

        var signItem = new DateItemViewModel(candidates[0], file, segment);
        signItem.IsSelected.Should().BeTrue();
        signItem.IsRecommendedForReplacement.Should().BeTrue();
        signItem.HasSensitiveWarning.Should().BeFalse();

        var birthItem = new DateItemViewModel(candidates[2], file, segment);
        birthItem.IsSelected.Should().BeFalse();
        birthItem.IsRecommendedForReplacement.Should().BeFalse();
        birthItem.IsSensitivePersonalData.Should().BeTrue();
        birthItem.HasSensitiveWarning.Should().BeFalse();
    }

    [Fact]
    public void DateItemViewModel_WhenSensitiveDateSelected_ShouldTriggerWarning()
    {
        var (file, segment, candidates) = CreateSampleDetectedData();
        var birthItem = new DateItemViewModel(candidates[2], file, segment);

        // Ban đầu chưa chọn -> không cảnh báo
        birthItem.HasSensitiveWarning.Should().BeFalse();

        // Người dùng tick chọn ngày sinh -> Kích hoạt cảnh báo an toàn
        birthItem.IsSelected = true;
        birthItem.HasSensitiveWarning.Should().BeTrue();
        birthItem.SensitiveWarningMessage.Should().Contain("CẢNH BÁO");
    }

    [Fact]
    public void ReviewViewModel_SelectAllRecommended_ShouldOnlySelectSafeDates()
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });

        // Người dùng bấm Bỏ chọn toàn bộ
        vm.DeselectAll();
        vm.SelectedDatesCount.Should().Be(0);

        // Bấm Chọn tất cả ngày được đề xuất
        vm.SelectAllRecommended();

        // 3 ngày an toàn (Ký, Hiệu lực, Thanh toán) được chọn, 2 ngày nhân thân (Sinh, CCCD) không được chọn
        vm.SelectedDatesCount.Should().Be(3);
        vm.DateItems.First(d => d.ClassificationType == DateClassificationType.ContractSignDate).IsSelected.Should().BeTrue();
        vm.DateItems.First(d => d.ClassificationType == DateClassificationType.BirthDate).IsSelected.Should().BeFalse();
        vm.DateItems.First(d => d.ClassificationType == DateClassificationType.IdentityIssueDate).IsSelected.Should().BeFalse();
        vm.SensitiveWarningsCount.Should().Be(0);
    }

    [Fact]
    public void ReviewViewModel_ApplyBatchDate_ShouldUpdateOnlySelectedDates()
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });

        var newTargetDate = new DateTime(2026, 12, 25);
        vm.BatchTargetDate = newTargetDate;

        // Áp dụng ngày hàng loạt
        vm.ApplyBatchDate();

        // Các ngày được tick (Ký, Hiệu lực, Thanh toán) nhận ngày mới
        var selectedItems = vm.DateItems.Where(d => d.IsSelected).ToList();
        selectedItems.Should().HaveCount(3);
        selectedItems.All(d => d.TargetNewDate == newTargetDate).Should().BeTrue();

        // Các ngày không tick vẫn giữ nguyên null (người dùng tự chọn)
        var unselectedItems = vm.DateItems.Where(d => !d.IsSelected).ToList();
        unselectedItems.Should().HaveCount(2);
        unselectedItems.All(d => d.TargetNewDate == null).Should().BeTrue();
    }

    [Theory]
    [InlineData("All", 5)]
    [InlineData("SignAndEffective", 2)]
    [InlineData("PaymentAndHandover", 1)]
    [InlineData("Personal", 2)]
    public void ReviewViewModel_CategoryFiltering_ShouldFilterCorrectly(string category, int expectedCount)
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });

        vm.SetCategoryFilter(category);
        var visibleItems = vm.FilteredDateItemsView.Cast<DateItemViewModel>().ToList();

        visibleItems.Count.Should().Be(expectedCount);
    }

    [Fact]
    public void ReviewViewModel_SearchKeyword_ShouldFilterByContent()
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });

        vm.SearchKeyword = "Hà Nội";
        var visible = vm.FilteredDateItemsView.Cast<DateItemViewModel>().ToList();

        visible.Should().HaveCount(1);
        visible[0].RawText.Should().Be("01/01/1990");
    }

    [Fact]
    public void ReviewViewModel_NavigationEvents_ShouldTriggerCorrectly()
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });

        bool backRequested = false;
        bool previewRequested = false;

        vm.RequestNavigateBack += () => backRequested = true;
        vm.RequestNavigateToPreview += () => previewRequested = true;

        vm.NavigateBack();
        backRequested.Should().BeTrue();

        // Mọi mục đã chọn đều phải có ngày đích trước khi tiếp tục.
        foreach (var selectedItem in vm.DateItems.Where(d => d.IsSelected))
        {
            selectedItem.TargetNewDate = DateTime.Today;
        }
        vm.CanContinueToPreview.Should().BeTrue();
        vm.ContinueToPreview();
        previewRequested.Should().BeTrue();
    }

    [Fact]
    public void ReviewViewModel_CanContinue_ShouldUpdateImmediatelyAndRequireTargetsForEverySelectedDate()
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });
        var selectedItems = vm.DateItems.Where(x => x.IsSelected).ToList();

        vm.CanContinueToPreview.Should().BeFalse();
        foreach (var item in selectedItems)
        {
            item.TargetNewDate = DateTime.Today;
        }
        vm.CanContinueToPreview.Should().BeTrue();

        selectedItems[0].TargetNewDate = null;
        vm.CanContinueToPreview.Should().BeFalse();
    }

    [Fact]
    public void ReviewViewModel_SelectDateForPreview_ShouldFocusExactOccurrenceWithoutChangingReplacementSelection()
    {
        var data = CreateSampleDetectedData();
        data.Segment.Id = "segment-main-1";
        data.Segment.LogicalText = "Hôm nay, ngày 02 tháng 09 năm 2026 chúng tôi gồm có: ngày 02 tháng 09 năm 2026.";
        data.Candidates[0].SegmentId = data.Segment.Id;
        data.Candidates[0].StartIndex = 9;
        data.Candidates[0].Length = data.Candidates[0].RawText.Length;
        var secondOccurrence = new DateCandidate
        {
            SegmentId = data.Segment.Id,
            RawText = data.Candidates[0].RawText,
            StartIndex = 53,
            Length = data.Candidates[0].RawText.Length,
            Classification = new ClassificationResult
            {
                Type = DateClassificationType.ContractSignDate,
                Confidence = 0.98,
                IsRecommendedForReplacement = true
            }
        };
        data.Candidates.Add(secondOccurrence);

        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { data });
        var item = vm.DateItems.Last();
        var originalSelections = vm.DateItems.Select(x => x.IsSelected).ToArray();

        vm.SelectDateForPreview(item);

        vm.SelectedDateItem.Should().BeSameAs(item);
        vm.PreviewFileName.Should().Be(data.File.FileName);
        vm.PreviewLocation.Should().Be(data.Segment.SourceLocation);
        vm.PreviewSegments.Should().ContainSingle(x =>
            x.SegmentId == "segment-main-1" &&
            x.HighlightStart == 53 &&
            x.HighlightLength == item.RawText.Length);
        vm.DateItems.Select(x => x.IsSelected).Should().Equal(originalSelections);
    }

    [Fact]
    public void ReviewViewModel_FilePreview_ShouldIncludeAllScannedSegmentsAndExcludeOtherFiles()
    {
        var first = CreateSampleDetectedData();
        first.Segment.Id = "main";
        first.Segment.LogicalText = "Nội dung chính";
        first.File.Segments = new List<DocumentTextSegment>
        {
            first.Segment,
            new() { Id = "header", StoryType = StoryType.Header, SourceLocation = "Đầu trang", LogicalText = "Nội dung đầu trang" }
        };

        var second = CreateSampleDetectedData();
        second.File.FilePath = @"D:\Contracts\HD_Khac.docx";
        second.Segment.Id = "other";
        second.Segment.LogicalText = "Không được xuất hiện";
        second.File.Segments = new List<DocumentTextSegment> { second.Segment };

        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { first, second });
        vm.SelectedFileFilter = first.File.FileName;

        vm.PreviewSegments.Select(x => x.Text).Should().Equal("Nội dung chính", "Nội dung đầu trang");
        vm.PreviewSegments.Should().Contain(x => x.StoryLabel == "Đầu trang");
        vm.PreviewSegments.Should().NotContain(x => x.Text.Contains("Không được xuất hiện"));
    }

    [Fact]
    public void ReviewViewModel_FilterHidesSelectedDate_ShouldSelectFirstVisibleDate()
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });
        vm.SelectDateForPreview(vm.DateItems.Last());

        vm.SetCategoryFilter("Personal");

        vm.SelectedDateItem.Should().BeSameAs(vm.FilteredDateItemsView.Cast<DateItemViewModel>().First());
    }

    [Fact]
    public void ReviewViewModel_SameFileNames_ShouldResolvePreviewByFullPath()
    {
        var first = CreateSampleDetectedData();
        first.Segment.LogicalText = "Văn bản thứ nhất";
        first.File.Segments = new List<DocumentTextSegment> { first.Segment };
        var second = CreateSampleDetectedData();
        second.File.FilePath = @"E:\Archive\HD_MuaBan_2026.docx";
        second.Segment.LogicalText = "Văn bản thứ hai";
        second.File.Segments = new List<DocumentTextSegment> { second.Segment };

        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { first, second });
        vm.SelectDateForPreview(vm.DateItems.Last());

        vm.PreviewFileName.Should().Be("HD_MuaBan_2026.docx");
        vm.PreviewSegments.Should().ContainSingle(x => x.Text == "Văn bản thứ hai");
        vm.PreviewSegments.Should().NotContain(x => x.Text == "Văn bản thứ nhất");
    }

    [Fact]
    public void ReviewViewModel_ReloadingWithNoCandidates_ShouldClearPreview()
    {
        var data = CreateSampleDetectedData();
        data.Segment.LogicalText = "Nội dung đã quét";
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { data });
        vm.HasPreviewContent.Should().BeTrue();

        vm.LoadCandidates(Array.Empty<(ContractFileItem, DocumentTextSegment, List<DateCandidate>)>());

        vm.SelectedDateItem.Should().BeNull();
        vm.PreviewSegments.Should().BeEmpty();
        vm.HasPreviewContent.Should().BeFalse();
        vm.PreviewFileName.Should().Be("Chưa chọn file");
    }

    [Fact]
    public void ReviewViewModel_SelectedFileFilterNull_ShouldNotThrowAndTreatAsAllFiles()
    {
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { CreateSampleDetectedData() });

        // Giả lập hành vi của WPF ComboBox khi SelectedItem bị reset về null
        vm.SelectedFileFilter = null;

        // Thao tác này không được ném ArgumentNullException (Parameter 'key')
        vm.FilteredDateItemsView.Refresh();
        vm.FilteredDateItemsView.Cast<DateItemViewModel>().Should().HaveCount(5);
        vm.CurrentFileInfoText.Should().Contain("TẤT CẢ FILE");
    }

    [Fact]
    public void ReviewViewModel_LoadCandidates_WhenSelectedFileFilterWasNull_ShouldSucceed()
    {
        var vm = new ReviewViewModel();
        vm.SelectedFileFilter = null;

        // Khi load candidates mới, vm phải khởi tạo lại SelectedFileFilter thành "Tất cả các file" mà không lỗi
        var act = () => vm.LoadCandidates(new[] { CreateSampleDetectedData() });
        act.Should().NotThrow();
        vm.SelectedFileFilter.Should().Be("Tất cả các file");
        vm.FilteredDateItemsView.Cast<DateItemViewModel>().Should().HaveCount(5);
    }
}

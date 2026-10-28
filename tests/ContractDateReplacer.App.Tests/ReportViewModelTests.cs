using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class ReportViewModelTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IAppDataService> _mockAppDataService;

    public ReportViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ReportViewModelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _mockAppDataService = new Mock<IAppDataService>();
        _mockAppDataService.Setup(a => a.RootDirectory).Returns(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public void LoadReport_AggregatesSummaryMetricsCorrectly()
    {
        var vm = new ReportViewModel(_mockAppDataService.Object, NullLogger<ReportViewModel>.Instance);

        string file1 = Path.Combine(_tempDirectory, "HD_MuaBan.docx");
        string file2 = Path.Combine(_tempDirectory, "HD_DichVu.docx");

        var fileItem1 = new FileExecutionItemViewModel(file1, 2)
        {
            OutputFilePath = Path.Combine(_tempDirectory, "HD_MuaBan_modified.docx"),
            Status = ExecutionItemStatus.Success,
            VerificationResult = new VerificationResult
            {
                FinalStatus = FileScanStatus.Pass,
                SafetyScore = 100,
                ActualReplacementsDone = 2
            }
        };

        var fileItem2 = new FileExecutionItemViewModel(file2, 1)
        {
            OutputFilePath = Path.Combine(_tempDirectory, "HD_DichVu_modified.docx"),
            Status = ExecutionItemStatus.Failed,
            VerificationResult = new VerificationResult
            {
                FinalStatus = FileScanStatus.ReviewRequired,
                SafetyScore = 80,
                ActualReplacementsDone = 1,
                Issues = new List<string> { "Cần kiểm tra ngày không rõ ngữ cảnh" }
            }
        };

        var occurrences = new List<ReplacementOccurrence>
        {
            // File 1 occurrences
            new()
            {
                FilePath = file1,
                Segment = new DocumentTextSegment { SourceLocation = "Đoạn 1" },
                Candidate = new DateCandidate
                {
                    RawText = "02/09/2026",
                    Classification = new ClassificationResult { Type = DateClassificationType.ContractSignDate }
                },
                NewDateText = "05/10/2026",
                IsSelected = true
            },
            new()
            {
                FilePath = file1,
                Segment = new DocumentTextSegment { SourceLocation = "Đoạn 2" },
                Candidate = new DateCandidate
                {
                    RawText = "01/10/2026",
                    Classification = new ClassificationResult { Type = DateClassificationType.ContractEffectiveDate }
                },
                NewDateText = "05/10/2026",
                IsSelected = true
            },
            new()
            {
                FilePath = file1,
                Segment = new DocumentTextSegment { SourceLocation = "Đoạn 3" },
                Candidate = new DateCandidate
                {
                    RawText = "01/01/1990",
                    Classification = new ClassificationResult { Type = DateClassificationType.BirthDate }
                },
                NewDateText = "01/01/1990",
                IsSelected = false // Giữ nguyên
            },

            // File 2 occurrences
            new()
            {
                FilePath = file2,
                Segment = new DocumentTextSegment { SourceLocation = "Đoạn 1" },
                Candidate = new DateCandidate
                {
                    RawText = "15/08/2026",
                    Classification = new ClassificationResult { Type = DateClassificationType.PaymentDate }
                },
                NewDateText = "20/09/2026",
                IsSelected = true
            },
            new()
            {
                FilePath = file2,
                Segment = new DocumentTextSegment { SourceLocation = "Đoạn 4" },
                Candidate = new DateCandidate
                {
                    RawText = "30/12/2025",
                    Classification = new ClassificationResult { Type = DateClassificationType.Unknown }
                },
                NewDateText = "30/12/2025",
                IsSelected = false // Giữ nguyên
            }
        };

        vm.LoadReport(new[] { fileItem1, fileItem2 }, occurrences, _tempDirectory);

        vm.TotalFilesProcessed.Should().Be(2);
        vm.SuccessFilesCount.Should().Be(1);
        vm.WarningFilesCount.Should().Be(1);
        vm.FailedFilesCount.Should().Be(0);
        vm.TotalDatesReplaced.Should().Be(3);
        vm.TotalDatesPreserved.Should().Be(2);
        vm.AverageSafetyScore.Should().Be(90);

        vm.FileReports.Should().HaveCount(2);

        var rep1 = vm.FileReports.First(f => f.FileName == "HD_MuaBan.docx");
        rep1.SafetyScore.Should().Be(100);
        rep1.StatusBadgeText.Should().Be("PASS");
        rep1.ReplacedItems.Should().HaveCount(3);
        rep1.ReplacedItems.Count(i => i.ResultBadgeText == "[OK]").Should().Be(2);
        rep1.ReplacedItems.Count(i => i.ResultBadgeText == "[GIỮ NGUYÊN THEO YÊU CẦU]").Should().Be(1);

        var rep2 = vm.FileReports.First(f => f.FileName == "HD_DichVu.docx");
        rep2.SafetyScore.Should().Be(80);
        rep2.StatusBadgeText.Should().Be("WARNING");
        rep2.Issues.Should().Contain("Cần kiểm tra ngày không rõ ngữ cảnh");
    }

    [Fact]
    public void ExportCsv_WhenCalled_CreatesUtf8BomCsvFileWithAllDetails()
    {
        var vm = new ReportViewModel(_mockAppDataService.Object, NullLogger<ReportViewModel>.Instance);

        string file1 = Path.Combine(_tempDirectory, "HD_Test.docx");
        var fileItem = new FileExecutionItemViewModel(file1, 1)
        {
            OutputFilePath = Path.Combine(_tempDirectory, "HD_Test_modified.docx"),
            Status = ExecutionItemStatus.Success,
            VerificationResult = new VerificationResult
            {
                FinalStatus = FileScanStatus.Pass,
                SafetyScore = 100,
                ActualReplacementsDone = 1
            }
        };

        var occurrences = new List<ReplacementOccurrence>
        {
            new()
            {
                FilePath = file1,
                Segment = new DocumentTextSegment { SourceLocation = "Điều 1" },
                Candidate = new DateCandidate
                {
                    RawText = "02/09/2026",
                    Classification = new ClassificationResult { Type = DateClassificationType.ContractSignDate }
                },
                NewDateText = "05/10/2026",
                IsSelected = true
            }
        };

        vm.LoadReport(new[] { fileItem }, occurrences, _tempDirectory);

        vm.ExportCsv();

        var csvFiles = Directory.GetFiles(_tempDirectory, "*.csv");
        csvFiles.Should().NotBeEmpty();

        string content = File.ReadAllText(csvFiles[0]);
        content.Should().Contain("Tên File,Đường Dẫn File Mới,Điểm An Toàn");
        content.Should().Contain("HD_Test.docx");
        content.Should().Contain("02/09/2026");
        content.Should().Contain("05/10/2026");
        content.Should().Contain("[OK]");
        vm.ExportMessage.Should().Contain("thành công");
    }

    [Fact]
    public void ResetForNewBatch_WhenExecuted_TriggersEvent()
    {
        var vm = new ReportViewModel(_mockAppDataService.Object, NullLogger<ReportViewModel>.Instance);
        bool eventFired = false;
        vm.RequestResetToNewBatch += () => eventFired = true;

        vm.ResetForNewBatch();

        eventFired.Should().BeTrue();
    }
}

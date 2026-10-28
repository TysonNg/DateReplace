using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class ExecutionViewModelTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IDocumentReplacementService> _mockReplacementService;
    private readonly Mock<IDocumentVerificationService> _mockVerificationService;
    private readonly Mock<IBackupRollbackService> _mockBackupService;
    private readonly Mock<IAppDataService> _mockAppDataService;

    public ExecutionViewModelTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ExecutionViewModelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _mockReplacementService = new Mock<IDocumentReplacementService>();
        _mockVerificationService = new Mock<IDocumentVerificationService>();
        _mockBackupService = new Mock<IBackupRollbackService>();
        _mockBackupService
            .Setup(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupRecord { BackupPath = Path.Combine(_tempDirectory, "backup.bak"), ChecksumSHA256 = "1234567890ABCDEF" });

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

    private List<ReplacementOccurrence> CreateSampleOccurrences()
    {
        string dummyFile1 = Path.Combine(_tempDirectory, "HD_01.docx");
        string dummyFile2 = Path.Combine(_tempDirectory, "HD_02.docx");

        File.WriteAllText(dummyFile1, "dummy 1");
        File.WriteAllText(dummyFile2, "dummy 2");

        var occ1 = new ReplacementOccurrence
        {
            FilePath = dummyFile1,
            Segment = new DocumentTextSegment { SourceLocation = "Đoạn 1" },
            Candidate = new DateCandidate { RawText = "02/09/2026" },
            NewDateText = "05/10/2026",
            IsSelected = true
        };

        var occ2 = new ReplacementOccurrence
        {
            FilePath = dummyFile2,
            Segment = new DocumentTextSegment { SourceLocation = "Đoạn 2" },
            Candidate = new DateCandidate { RawText = "01/10/2026" },
            NewDateText = "15/10/2026",
            IsSelected = true
        };

        return new List<ReplacementOccurrence> { occ1, occ2 };
    }

    [Fact]
    public async Task StartExecutionAsync_DuplicateNamesAndExistingOutput_UsesDistinctPaths()
    {
        var occurrences = CreateSampleOccurrences();
        occurrences[0].FilePath = Path.Combine(_tempDirectory, "A", "Contract.docx");
        occurrences[1].FilePath = Path.Combine(_tempDirectory, "B", "Contract.docx");
        string output = Path.Combine(_tempDirectory, "duplicate_output");
        Directory.CreateDirectory(output);
        string existing = Path.Combine(output, "Contract_modified.docx");
        File.WriteAllText(existing, "existing result");
        var vm = new ExecutionViewModel(_mockReplacementService.Object, _mockVerificationService.Object,
            _mockBackupService.Object, _mockAppDataService.Object, NullLogger<ExecutionViewModel>.Instance);
        await vm.StartExecutionAsync(occurrences, output);
        vm.ExecutionFiles.Select(f => f.OutputFilePath).Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(2);
        vm.ExecutionFiles.Should().NotContain(f => f.OutputFilePath == existing);
        File.ReadAllText(existing).Should().Be("existing result");
    }
    [Fact]
    public async Task StartExecutionAsync_WhenAllPass_UpdatesProgressAndMarksSuccess()
    {
        _mockReplacementService
            .Setup(r => r.ReplaceDatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ReplacementOccurrence>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVerificationService
            .Setup(v => v.VerifyOutputAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ReplacementOccurrence>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationResult
            {
                FinalStatus = FileScanStatus.Pass,
                ActualReplacementsDone = 1,
                SafetyScore = 100,
                IsValidXmlStructure = true
            });

        var vm = new ExecutionViewModel(
            _mockReplacementService.Object,
            _mockVerificationService.Object,
            _mockBackupService.Object,
            _mockAppDataService.Object,
            NullLogger<ExecutionViewModel>.Instance);

        var occurrences = CreateSampleOccurrences();
        string customOutDir = Path.Combine(_tempDirectory, "OutDir");

        await vm.StartExecutionAsync(occurrences, customOutDir);

        vm.IsCompleted.Should().BeTrue();
        vm.IsRunning.Should().BeFalse();
        vm.ProgressPercentage.Should().Be(100);
        vm.ExecutionFiles.Should().HaveCount(2);
        vm.ExecutionFiles.All(f => f.Status == ExecutionItemStatus.Success).Should().BeTrue();
        vm.ActivityLogs.Should().Contain(l => l.Message.Contains("Verify PASS"));
        vm.SummaryResultsText.Should().Contain("2/2");
    }

    [Fact]
    public async Task StartExecutionAsync_WhenVerificationFails_MarksFileAsFailed()
    {
        _mockReplacementService
            .Setup(r => r.ReplaceDatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ReplacementOccurrence>>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVerificationService
            .Setup(v => v.VerifyOutputAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ReplacementOccurrence>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationResult
            {
                FinalStatus = FileScanStatus.Error,
                Issues = new List<string> { "Phát hiện sót ngày cũ" },
                SafetyScore = 40,
                IsValidXmlStructure = true
            });

        var vm = new ExecutionViewModel(
            _mockReplacementService.Object,
            _mockVerificationService.Object,
            _mockBackupService.Object,
            _mockAppDataService.Object,
            NullLogger<ExecutionViewModel>.Instance);

        var occurrences = CreateSampleOccurrences();
        await vm.StartExecutionAsync(occurrences, Path.Combine(_tempDirectory, "OutDir2"));

        vm.IsCompleted.Should().BeTrue();
        vm.ExecutionFiles.All(f => f.Status == ExecutionItemStatus.Failed).Should().BeTrue();
        vm.ActivityLogs.Should().Contain(l => l.Level == "Error");
    }

    [Fact]
    public async Task CancelCommand_WhenExecuted_StopsExecutionAndSetsCancelled()
    {
        var cancelSignal = new ManualResetEvent(false);

        _mockReplacementService
            .Setup(r => r.ReplaceDatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ReplacementOccurrence>>(), true, It.IsAny<CancellationToken>()))
            .Returns<string, string, IEnumerable<ReplacementOccurrence>, bool, CancellationToken>(async (src, dest, occ, preserve, ct) =>
            {
                cancelSignal.Set();
                await Task.Delay(100, ct);
                ct.ThrowIfCancellationRequested();
                return true;
            });

        var vm = new ExecutionViewModel(
            _mockReplacementService.Object,
            _mockVerificationService.Object,
            _mockBackupService.Object,
            _mockAppDataService.Object,
            NullLogger<ExecutionViewModel>.Instance);

        var occurrences = CreateSampleOccurrences();

        var execTask = vm.StartExecutionAsync(occurrences, Path.Combine(_tempDirectory, "OutDir3"));

        // Chờ tác vụ bắt đầu rồi hủy
        cancelSignal.WaitOne(1000);
        vm.Cancel();

        await execTask;

        vm.IsCancelled.Should().BeTrue();
        vm.ActivityLogs.Should().Contain(l => l.Message.Contains("hủy") || l.Message.Contains("dừng"));
    }
}


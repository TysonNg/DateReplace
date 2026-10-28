using System.IO;
using ContractDateReplacer.App.Services;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public sealed class MixedDocumentWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MixedDocumentTests_" + Guid.NewGuid().ToString("N"));
    public MixedDocumentWorkflowTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public void Dashboard_HiddenSheetsDisabledAfterScan_AreExcludedFromReview()
    {
        var dashboard = new DashboardViewModel(Mock.Of<IDocumentScanner>(), new DateDetector(), new DateClassifier(), Mock.Of<IFileDialogService>(), NullLogger<DashboardViewModel>.Instance);
        dashboard.Files.Add(new ContractFileItem { FilePath = "dates.xlsx", Status = FileScanStatus.Pass,
            Segments = new() { new() { IsHiddenSheet = true, WorksheetName = "Hidden", DetectedDates = new() { new() { RawText = "15/10/2026" } } } } });
        dashboard.IncludeHiddenSheets = true;
        Assert.Single(dashboard.GetDetectedItems());
        dashboard.IncludeHiddenSheets = false;
        Assert.Empty(dashboard.GetDetectedItems());
    }

    [Fact]
    public void Review_SheetOptionsAndBulkActions_RespectVisibleSheet()
    {
        var file = new ContractFileItem { FilePath = "dates.xlsx" };
        var first = new DocumentTextSegment { Id = "1", WorksheetName = "Hợp đồng", CellAddress = "A1" };
        var second = new DocumentTextSegment { Id = "2", WorksheetName = "Thanh toán", CellAddress = "B2" };
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] {
            (file, first, new List<DateCandidate> { new() { RawText = "15/10/2026", Classification = new ClassificationResult { IsRecommendedForReplacement = false } } }),
            (file, second, new List<DateCandidate> { new() { RawText = "20/10/2026", Classification = new ClassificationResult { IsRecommendedForReplacement = false } } }) });
        Assert.Contains("Hợp đồng", vm.SheetFilterOptions);
        Assert.Contains("Thanh toán", vm.SheetFilterOptions);
        vm.SelectedSheetFilter = "Thanh toán";
        vm.SelectAll();
        Assert.False(vm.DateItems[0].IsSelected);
        Assert.True(vm.DateItems[1].IsSelected);
    }
    [Fact]
    public void Dashboard_OnlySelectedSuccessfullyScannedFiles_EnterReview()
    {
        var dashboard = new DashboardViewModel(Mock.Of<IDocumentScanner>(), new DateDetector(), new DateClassifier(), Mock.Of<IFileDialogService>(), NullLogger<DashboardViewModel>.Instance);
        ContractFileItem Item(string name, bool selected, FileScanStatus status) => new()
        {
            FilePath = name, IsSelected = selected, Status = status,
            Segments = new() { new() { DetectedDates = new() { new() { RawText = "15/10/2026" } } } }
        };
        dashboard.Files.Add(Item("selected.docx", true, FileScanStatus.Pass));
        dashboard.Files.Add(Item("deselected.xlsx", false, FileScanStatus.Pass));
        dashboard.Files.Add(Item("failed.xlsx", true, FileScanStatus.Error));
        var detected = dashboard.GetDetectedItems();
        Assert.Single(detected);
        Assert.Equal("selected.docx", detected[0].File.FileName);
    }
    [Fact]
    public async Task ExcelWorkflow_TextNumericFormulaAndHiddenSheets_AreReviewedAndSavedSafely()
    {
        string source = Path.Combine(_directory, "NgayHopDong.xlsx");
        using (var book = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(source, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var workbook = book.AddWorkbookPart();
            workbook.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
            var styles = workbook.AddNewPart<DocumentFormat.OpenXml.Packaging.WorkbookStylesPart>();
            styles.Stylesheet = new DocumentFormat.OpenXml.Spreadsheet.Stylesheet(
                new DocumentFormat.OpenXml.Spreadsheet.Fonts(new DocumentFormat.OpenXml.Spreadsheet.Font()),
                new DocumentFormat.OpenXml.Spreadsheet.Fills(new DocumentFormat.OpenXml.Spreadsheet.Fill(new DocumentFormat.OpenXml.Spreadsheet.PatternFill { PatternType = DocumentFormat.OpenXml.Spreadsheet.PatternValues.None })),
                new DocumentFormat.OpenXml.Spreadsheet.Borders(new DocumentFormat.OpenXml.Spreadsheet.Border()),
                new DocumentFormat.OpenXml.Spreadsheet.CellStyleFormats(new DocumentFormat.OpenXml.Spreadsheet.CellFormat()),
                new DocumentFormat.OpenXml.Spreadsheet.CellFormats(new DocumentFormat.OpenXml.Spreadsheet.CellFormat(), new DocumentFormat.OpenXml.Spreadsheet.CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
            var sheets = workbook.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
            var worksheet = workbook.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
            var data = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
            worksheet.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(data);
            DocumentFormat.OpenXml.Spreadsheet.Cell Text(string address, string text) => new()
            {
                CellReference = address, DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString,
                InlineString = new DocumentFormat.OpenXml.Spreadsheet.InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(text))
            };
            data.Append(new DocumentFormat.OpenXml.Spreadsheet.Row(Text("A1", "Ngày ký"), Text("B1", "Ngày sinh"), Text("C1", "Ghi chú"), Text("D1", "Ngày công thức")) { RowIndex = 1 });
            data.Append(new DocumentFormat.OpenXml.Spreadsheet.Row(
                new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference = "A2", StyleIndex = 1, CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(new DateTime(2026, 10, 15).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)) },
                Text("B2", "01/01/1990"), Text("C2", "Ngày bàn giao: 20/10/2026"),
                new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference = "D2", StyleIndex = 1, CellFormula = new DocumentFormat.OpenXml.Spreadsheet.CellFormula("A2+30"), CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(new DateTime(2026, 11, 14).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture)) }
            ) { RowIndex = 2 });
            sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Name = "Hợp đồng", SheetId = 1, Id = workbook.GetIdOfPart(worksheet) });
            var hidden = workbook.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
            hidden.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData(new DocumentFormat.OpenXml.Spreadsheet.Row(Text("A1", "Ngày ký: 15/10/2026")) { RowIndex = 1 }));
            sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Name = "Lưu trữ", SheetId = 2, Id = workbook.GetIdOfPart(hidden), State = DocumentFormat.OpenXml.Spreadsheet.SheetStateValues.Hidden });
        }
        string? smokePath = Environment.GetEnvironmentVariable("DATEREPLACE_UI_SMOKE_PATH");
        if (!string.IsNullOrEmpty(smokePath)) File.Copy(source, smokePath, true);
        var excel = new ContractDateReplacer.Infrastructure.Services.ExcelDocumentService();
        var dashboard = new DashboardViewModel(excel, new DateDetector(), new DateClassifier(), Mock.Of<IFileDialogService>(), NullLogger<DashboardViewModel>.Instance);
        dashboard.AddFilePaths(new[] { source });
        await dashboard.ScanDocumentsAsync();
        Assert.Equal(4, dashboard.Files[0].TotalDatesDetected);
        Assert.DoesNotContain(dashboard.Files[0].Segments, s => s.IsHiddenSheet);
        var signing = dashboard.Files[0].Segments.Single(s => s.CellAddress == "A2").DetectedDates.Single();
        Assert.Equal(DateClassificationType.ContractSignDate, signing.Classification.Type);
        Assert.True(signing.Classification.IsRecommendedForReplacement);
        Assert.Equal(dashboard.Files[0].DetectedCandidates.Count(c => c.Classification.IsReviewRequired), dashboard.Files[0].UnknownCount);
        var review = new ReviewViewModel();
        review.LoadCandidates(dashboard.GetDetectedItems());
        review.SelectAll();
        review.BatchTargetDate = new DateTime(2026, 12, 1);
        review.ApplyBatchDate();
        Assert.False(review.DateItems.Single(d => d.Segment!.CellAddress == "D2").IsSelected);
        Assert.True(review.IsSpreadsheetPreview);
        Assert.Equal(7, review.ExcelContextRows.Count);
        Assert.True(review.CanContinueToPreview);
        var occurrences = review.DateItems.Select(d => new ReplacementOccurrence
        {
            FilePath = d.FilePath, Segment = d.Segment!, Candidate = d.Candidate, IsSelected = d.HasEffectiveChange,
            NewDateTime = d.TargetNewDate, NewDateText = d.FormattedReplacementText
        }).ToList();
        string output = Path.Combine(_directory, "modified.xlsx");
        Assert.True(await excel.ReplaceDatesAsync(source, output, occurrences));
        Assert.True((await excel.VerifyOutputAsync(source, output, occurrences)).IsPassed);
        dashboard.IncludeHiddenSheets = true;
        await dashboard.ScanDocumentsAsync();
        Assert.Equal(5, dashboard.Files[0].TotalDatesDetected);
    }
    [Fact]
    public void AddFilePaths_ImportsWordAndExcel_IgnoresTemporaryAndUnsupportedFiles()
    {
        foreach (string name in new[] { "contract.docx", "dates.xlsx", "~$locked.xlsx", "old.xls", "notes.txt" })
            File.WriteAllText(Path.Combine(_directory, name), "fixture");
        var vm = new DashboardViewModel(Mock.Of<IDocumentScanner>(), new DateDetector(), new DateClassifier(),
            Mock.Of<IFileDialogService>(), NullLogger<DashboardViewModel>.Instance);
        vm.AddFilePaths(new[] { _directory });
        Assert.Equal(2, vm.Files.Count);
        Assert.Contains(vm.Files, f => f.FileName == "dates.xlsx");
    }

    [Fact]
    public void Review_ProtectedCell_CannotBeSelectedOrExecuted()
    {
        var file = new ContractFileItem { FilePath = "dates.xlsx" };
        var segment = new DocumentTextSegment { Id = "formula", IsSupportedForReplacement = false };
        var candidate = new DateCandidate { RawText = "15/10/2026", Classification = new ClassificationResult
            { Type = DateClassificationType.ContractSignDate, Confidence = 0.98, IsRecommendedForReplacement = true } };
        var vm = new ReviewViewModel();
        vm.LoadCandidates(new[] { (file, segment, new List<DateCandidate> { candidate }) });
        vm.SelectAll();
        vm.ApplyBatchDate();
        Assert.False(vm.DateItems[0].IsSelected);
        Assert.False(vm.DateItems[0].HasEffectiveChange);
        Assert.False(vm.CanContinueToPreview);
    }

    [Fact]
    public async Task Execution_Excel_PreservesXlsxOutputExtension()
    {
        string source = Path.Combine(_directory, "dates.xlsx");
        File.WriteAllText(source, "fixture");
        var replacement = new Mock<IDocumentReplacementService>();
        string? output = null;
        replacement.Setup(r => r.ReplaceDatesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<ReplacementOccurrence>>(), true, It.IsAny<CancellationToken>()))
            .Callback<string,string,IEnumerable<ReplacementOccurrence>,bool,CancellationToken>((_, dest, _, _, _) => output = dest)
            .ReturnsAsync(true);
        var verification = new Mock<IDocumentVerificationService>();
        verification.Setup(v => v.VerifyOutputAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<ReplacementOccurrence>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationResult());
        var backup = new Mock<IBackupRollbackService>();
        backup.Setup(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupRecord { BackupPath = "backup", ChecksumSHA256 = "12345678" });
        var appData = new Mock<IAppDataService>();
        appData.Setup(a => a.RootDirectory).Returns(_directory);
        var vm = new ExecutionViewModel(replacement.Object, verification.Object, backup.Object, appData.Object, NullLogger<ExecutionViewModel>.Instance);
        await vm.StartExecutionAsync(new[] { new ReplacementOccurrence { FilePath = source, IsSelected = true,
            Candidate = new DateCandidate { RawText = "15/10/2026" }, NewDateText = "22/10/2026" } }, Path.Combine(_directory, "output"));
        Assert.Equal(".xlsx", Path.GetExtension(output));
    }
}





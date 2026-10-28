using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContractDateReplacer.Word.Tests;

public class WordReplacementTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly WordOpenXmlValidator _validator;
    private readonly WordDocumentScanner _scanner;
    private readonly WordDocumentReplacementService _service;

    public WordReplacementTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WordReplacementTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _validator = new WordOpenXmlValidator();
        _scanner = new WordDocumentScanner();
        _service = new WordDocumentReplacementService(_validator, NullLogger<WordDocumentReplacementService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task ReplaceDate_InSingleRun_PreservesFormattingAndReplacesText()
    {
        string srcPath = Path.Combine(_tempDirectory, "SingleRun_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "SingleRun_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(
                    new Run(new Text("Hôm nay, ngày ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new RunProperties(new Bold(), new Color { Val = "FF0000" }), new Text("02/09/2026")),
                    new Run(new Text(", tại trụ sở công ty.") { Space = SpaceProcessingModeValues.Preserve })
                )
            ));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        segments.Should().HaveCount(1);
        var seg = segments[0];

        // "Hôm nay, ngày 02/09/2026, tại trụ sở công ty."
        int dateStart = seg.LogicalText.IndexOf("02/09/2026");
        dateStart.Should().BeGreaterThan(-1);

        var occurrence = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "02/09/2026",
                StartIndex = dateStart,
                Length = "02/09/2026".Length
            },
            NewDateText = "05/10/2026",
            IsSelected = true
        };

        bool result = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        result.Should().BeTrue();

        // 1. Kiểm tra OpenXML schema hợp lệ tuyệt đối
        var validation = _validator.ValidateDocument(destPath);
        validation.IsValid.Should().BeTrue();
        validation.Errors.Should().BeEmpty();

        // 2. Kiểm tra text sau khi thay thế
        var destSegments = await _scanner.ScanDocumentAsync(destPath);
        destSegments[0].LogicalText.Should().Be("Hôm nay, ngày 05/10/2026, tại trụ sở công ty.");

        // 3. Kiểm tra định dạng Bold và Color của Run ngày mới vẫn được giữ nguyên
        using (var doc = WordprocessingDocument.Open(destPath, false))
        {
            var runs = doc.MainDocumentPart!.Document.Body!.Descendants<Run>().ToList();
            runs.Should().HaveCount(3);
            runs[1].RunProperties?.Bold.Should().NotBeNull();
            runs[1].RunProperties?.Color?.Val?.Value.Should().Be("FF0000");
            runs[1].InnerText.Should().Be("05/10/2026");
        }
    }

    [Fact]
    public async Task ReplaceDate_WhenDateSpansAcrossTwoRuns_ReplacesCorrectly()
    {
        string srcPath = Path.Combine(_tempDirectory, "TwoRuns_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "TwoRuns_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(
                    new Run(new Text("Hôm nay, ngày ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new RunProperties(new Bold()), new Text("02/")),
                    new Run(new Text("09/2026 tại Hà Nội.") { Space = SpaceProcessingModeValues.Preserve })
                )
            ));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var seg = segments[0];

        int dateStart = seg.LogicalText.IndexOf("02/09/2026");
        dateStart.Should().BeGreaterThan(-1);

        var occurrence = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "02/09/2026",
                StartIndex = dateStart,
                Length = "02/09/2026".Length
            },
            NewDateText = "05/10/2026",
            IsSelected = true
        };

        bool result = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        result.Should().BeTrue();

        var validation = _validator.ValidateDocument(destPath);
        validation.IsValid.Should().BeTrue();

        var destSegments = await _scanner.ScanDocumentAsync(destPath);
        destSegments[0].LogicalText.Should().Be("Hôm nay, ngày 05/10/2026 tại Hà Nội.");
    }

    [Fact]
    public async Task ReplaceDate_WhenDateSpansAcrossThreeRuns_ReplacesCorrectlyAndLeavesNoDebris()
    {
        string srcPath = Path.Combine(_tempDirectory, "ThreeRuns_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "ThreeRuns_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(
                    new Run(new Text("Ký ngày ")),
                    new Run(new Text("02/")),
                    new Run(new Text("09/")),
                    new Run(new Text("2026"))
                )
            ));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var seg = segments[0];

        int dateStart = seg.LogicalText.IndexOf("02/09/2026");
        dateStart.Should().BeGreaterThan(-1);

        var occurrence = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "02/09/2026",
                StartIndex = dateStart,
                Length = "02/09/2026".Length
            },
            NewDateText = "05/10/2026",
            IsSelected = true
        };

        bool result = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        result.Should().BeTrue();

        var validation = _validator.ValidateDocument(destPath);
        validation.IsValid.Should().BeTrue();

        var destSegments = await _scanner.ScanDocumentAsync(destPath);
        destSegments[0].LogicalText.Should().Be("Ký ngày 05/10/2026");
    }

    [Fact]
    public async Task ReplaceDates_MultipleDatesInSameParagraph_ReplacesFromRightToLeft()
    {
        string srcPath = Path.Combine(_tempDirectory, "MultipleDates_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "MultipleDates_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(
                    new Run(new Text("Hợp đồng lập ngày 02/09/2026, hết hạn vào ngày 31/12/2027.") { Space = SpaceProcessingModeValues.Preserve })
                )
            ));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var seg = segments[0];

        int date1Start = seg.LogicalText.IndexOf("02/09/2026");
        int date2Start = seg.LogicalText.IndexOf("31/12/2027");

        var occ1 = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate { RawText = "02/09/2026", StartIndex = date1Start, Length = 10 },
            NewDateText = "05/10/2026",
            IsSelected = true
        };

        var occ2 = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate { RawText = "31/12/2027", StartIndex = date2Start, Length = 10 },
            NewDateText = "15/01/2028",
            IsSelected = true
        };

        // Gửi danh sách theo thứ tự bất kỳ, engine phải tự sắp xếp thay thế từ phải qua trái
        bool result = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occ1, occ2 });
        result.Should().BeTrue();

        var validation = _validator.ValidateDocument(destPath);
        validation.IsValid.Should().BeTrue();

        var destSegments = await _scanner.ScanDocumentAsync(destPath);
        destSegments[0].LogicalText.Should().Be("Hợp đồng lập ngày 05/10/2026, hết hạn vào ngày 15/01/2028.");
    }

    [Fact]
    public async Task ReplaceDate_InTable_ReplacesCorrectly()
    {
        string srcPath = Path.Combine(_tempDirectory, "Table_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "Table_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Table(
                    new TableProperties(),
                    new TableGrid(new GridColumn(), new GridColumn()),
                    new TableRow(
                        new TableCell(new Paragraph(new Run(new Text("Điều khoản")))),
                        new TableCell(new Paragraph(new Run(new Text("Bàn giao nghiệm thu ngày 20/11/2026.") { Space = SpaceProcessingModeValues.Preserve })))
                    )
                )
            ));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var tableSeg = segments.First(s => s.StoryType == StoryType.Table && s.LogicalText.Contains("20/11/2026"));

        int dateStart = tableSeg.LogicalText.IndexOf("20/11/2026");

        var occ = new ReplacementOccurrence
        {
            Segment = tableSeg,
            Candidate = new DateCandidate { RawText = "20/11/2026", StartIndex = dateStart, Length = 10 },
            NewDateText = "25/12/2026",
            IsSelected = true
        };

        bool result = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occ });
        result.Should().BeTrue();

        var validation = _validator.ValidateDocument(destPath);
        validation.IsValid.Should().BeTrue();

        var destSegments = await _scanner.ScanDocumentAsync(destPath);
        var replacedSeg = destSegments.First(s => s.StoryType == StoryType.Table && s.LogicalText.Contains("25/12/2026"));
        replacedSeg.LogicalText.Should().Be("Bàn giao nghiệm thu ngày 25/12/2026.");
    }

    [Fact]
    public async Task ReplaceDate_InHeader_ReplacesCorrectly()
    {
        string srcPath = Path.Combine(_tempDirectory, "Header_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "Header_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Nội dung chính")))));

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(
                new Paragraph(new Run(new Text("Hợp đồng dịch vụ ngày 01/10/2026") { Space = SpaceProcessingModeValues.Preserve }))
            );
            headerPart.Header.Save();

            mainPart.Document.Body!.Append(
                new SectionProperties(
                    new HeaderReference { Type = HeaderFooterValues.Default, Id = mainPart.GetIdOfPart(headerPart) }
                )
            );
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var headerSeg = segments.First(s => s.StoryType == StoryType.Header);

        int dateStart = headerSeg.LogicalText.IndexOf("01/10/2026");

        var occ = new ReplacementOccurrence
        {
            Segment = headerSeg,
            Candidate = new DateCandidate { RawText = "01/10/2026", StartIndex = dateStart, Length = 10 },
            NewDateText = "15/10/2026",
            IsSelected = true
        };

        bool result = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occ });
        result.Should().BeTrue();

        var validation = _validator.ValidateDocument(destPath);
        validation.IsValid.Should().BeTrue();

        var destSegments = await _scanner.ScanDocumentAsync(destPath);
        var replacedHeader = destSegments.First(s => s.StoryType == StoryType.Header);
        replacedHeader.LogicalText.Should().Be("Hợp đồng dịch vụ ngày 15/10/2026");
    }
}

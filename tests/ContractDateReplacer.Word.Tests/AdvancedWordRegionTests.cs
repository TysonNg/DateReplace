using System;
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

public class AdvancedWordRegionTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly WordOpenXmlValidator _validator;
    private readonly WordDocumentScanner _scanner;
    private readonly WordDocumentReplacementService _service;

    public AdvancedWordRegionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AdvancedWordRegionTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task ContentControl_SdtDatePicker_ScansAndReplacesAndUpdatesFullDate()
    {
        string srcPath = Path.Combine(_tempDirectory, "SdtDatePicker_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "SdtDatePicker_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var sdtBlock = new SdtBlock(
                new SdtProperties(
                    new Tag { Val = "SigningDateTag" },
                    new SdtAlias { Val = "Ngày ký" },
                    new SdtContentDate { FullDate = DateTime.Parse("2024-04-18T00:00:00Z") }
                ),
                new SdtContentBlock(
                    new Paragraph(
                        new Run(new Text("Hợp đồng ký ngày 18/04/2024 tại Hà Nội"))
                    )
                )
            );

            mainPart.Document = new Document(new Body(sdtBlock));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        segments.Should().HaveCount(1);
        var seg = segments[0];

        seg.IsContentControl.Should().BeTrue();
        seg.ContentControlTitle.Should().Be("SigningDateTag");
        seg.IsDatePickerContentControl.Should().BeTrue();

        int dateIndex = seg.LogicalText.IndexOf("18/04/2024");
        dateIndex.Should().BeGreaterThan(-1);

        var occurrence = new ReplacementOccurrence
        {
            FilePath = srcPath,
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "18/04/2024",
                StartIndex = dateIndex,
                Length = 10
            },
            NewDateText = "02/09/2026",
            IsSelected = true
        };

        bool success = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        success.Should().BeTrue();

        // Kiểm tra tài liệu sau khi thay thế
        using (var doc = WordprocessingDocument.Open(destPath, false))
        {
            var mainPart = doc.MainDocumentPart!;
            var sdt = mainPart.Document.Descendants<SdtBlock>().First();
            string text = sdt.InnerText;
            text.Should().Contain("02/09/2026");
            text.Should().NotContain("18/04/2024");

            // Kiểm tra thuộc tính w:fullDate của thẻ SDT DatePicker đã được cập nhật thành 2026-09-02
            var sdtDate = sdt.SdtProperties?.GetFirstChild<SdtContentDate>();
            sdtDate.Should().NotBeNull();
            sdtDate!.FullDate.Should().NotBeNull();
            sdtDate.FullDate!.Value.Year.Should().Be(2026);
            sdtDate.FullDate!.Value.Month.Should().Be(9);
            sdtDate.FullDate!.Value.Day.Should().Be(2);
        }
    }

    [Fact]
    public async Task NestedTables_ScansAndReplacesDatesAtDeepestLevel()
    {
        string srcPath = Path.Combine(_tempDirectory, "NestedTables_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "NestedTables_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();

            // Bảng lồng nhau (Nested Table) bên trong TableCell của Bảng ngoài
            var innerTable = new Table(
                new TableProperties(),
                new TableGrid(new GridColumn()),
                new TableRow(
                    new TableCell(
                        new Paragraph(
                            new Run(new Text("Biên bản nghiệm thu ngày 15/05/2024 tại hiện trường"))
                        )
                    )
                )
            );

            var outerTable = new Table(
                new TableProperties(),
                new TableGrid(new GridColumn()),
                new TableRow(
                    new TableCell(
                        new Paragraph(new Run(new Text("Mục 1:"))),
                        innerTable,
                        new Paragraph()
                    )
                )
            );

            mainPart.Document = new Document(new Body(outerTable, new Paragraph()));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var targetSeg = segments.FirstOrDefault(s => s.LogicalText.Contains("15/05/2024"));
        targetSeg.Should().NotBeNull();
        targetSeg!.StoryType.Should().Be(StoryType.Table);

        int dateIndex = targetSeg.LogicalText.IndexOf("15/05/2024");
        var occurrence = new ReplacementOccurrence
        {
            FilePath = srcPath,
            Segment = targetSeg,
            Candidate = new DateCandidate
            {
                RawText = "15/05/2024",
                StartIndex = dateIndex,
                Length = 10
            },
            NewDateText = "20/11/2026",
            IsSelected = true
        };

        bool success = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        success.Should().BeTrue();

        using (var doc = WordprocessingDocument.Open(destPath, false))
        {
            string bodyText = doc.MainDocumentPart!.Document.InnerText;
            bodyText.Should().Contain("20/11/2026");
            bodyText.Should().NotContain("15/05/2024");
        }
    }

    [Fact]
    public async Task FieldCodes_DetectsSimpleFieldAndUnlinksDuringReplacement()
    {
        string srcPath = Path.Combine(_tempDirectory, "SimpleField_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "SimpleField_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var p = new Paragraph(
                new Run(new Text("Ngày phát hành: ") { Space = SpaceProcessingModeValues.Preserve }),
                new SimpleField(new Run(new Text("01/01/2024"))) { Instruction = "DATE \\@ \"dd/MM/yyyy\"" }
            );

            mainPart.Document = new Document(new Body(p));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        segments.Should().HaveCount(1);
        var seg = segments[0];

        seg.IsFieldCode.Should().BeTrue();
        seg.FieldInstruction.Should().Contain("DATE");

        int dateIndex = seg.LogicalText.IndexOf("01/01/2024");
        var occurrence = new ReplacementOccurrence
        {
            FilePath = srcPath,
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "01/01/2024",
                StartIndex = dateIndex,
                Length = 10
            },
            NewDateText = "01/01/2026",
            IsSelected = true
        };

        bool success = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        success.Should().BeTrue();

        using (var doc = WordprocessingDocument.Open(destPath, false))
        {
            var mainPart = doc.MainDocumentPart!;
            string text = mainPart.Document.InnerText;
            text.Should().Contain("01/01/2026");

            // SimpleField đã được unlink thành text tĩnh để Word không tự động ghi đè
            var fields = mainPart.Document.Descendants<SimpleField>().ToList();
            fields.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task FootnotesAndEndnotes_ScansAndReplacesDatesCorrectly()
    {
        string srcPath = Path.Combine(_tempDirectory, "Footnotes_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "Footnotes_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Hợp đồng nguyên tắc tham chiếu theo chú thích.")))
            ));

            var fnPart = mainPart.AddNewPart<FootnotesPart>();
            fnPart.Footnotes = new Footnotes(
                new Footnote(
                    new Paragraph(
                        new Run(new Text("Chú thích 1: Căn cứ Nghị định số 15 ký ngày 12/03/2021"))
                    )
                ) { Id = 1 }
            );

            mainPart.Document.Save();
            fnPart.Footnotes.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var fnSeg = segments.FirstOrDefault(s => s.StoryType == StoryType.Footnote);
        fnSeg.Should().NotBeNull();
        fnSeg!.LogicalText.Should().Contain("12/03/2021");

        int dateIndex = fnSeg.LogicalText.IndexOf("12/03/2021");
        var occurrence = new ReplacementOccurrence
        {
            FilePath = srcPath,
            Segment = fnSeg,
            Candidate = new DateCandidate
            {
                RawText = "12/03/2021",
                StartIndex = dateIndex,
                Length = 10
            },
            NewDateText = "15/08/2026",
            IsSelected = true
        };

        bool success = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        success.Should().BeTrue();

        using (var doc = WordprocessingDocument.Open(destPath, false))
        {
            var fnPart = doc.MainDocumentPart!.FootnotesPart!;
            string fnText = fnPart.Footnotes.InnerText;
            fnText.Should().Contain("15/08/2026");
            fnText.Should().NotContain("12/03/2021");
        }
    }

    [Fact]
    public async Task DrawingML_TextBox_ScansAndReplacesDatesWithinShape()
    {
        string srcPath = Path.Combine(_tempDirectory, "TextBox_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "TextBox_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();

            var txbx = new TextBoxContent(
                new Paragraph(
                    new Run(new Text("Hiệu lực chứng chỉ bảo mật: 25/12/2024"))
                )
            );

            var shape = new DocumentFormat.OpenXml.Vml.Shape(
                new DocumentFormat.OpenXml.Vml.TextBox(txbx)
            );
            var pict = new Picture(shape);
            var run = new Run(pict);
            var p = new Paragraph(run);

            mainPart.Document = new Document(new Body(p));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var txbxSeg = segments.FirstOrDefault(s => s.StoryType == StoryType.TextBox);
        txbxSeg.Should().NotBeNull();
        txbxSeg!.LogicalText.Should().Contain("25/12/2024");

        int dateIndex = txbxSeg.LogicalText.IndexOf("25/12/2024");
        var occurrence = new ReplacementOccurrence
        {
            FilePath = srcPath,
            Segment = txbxSeg,
            Candidate = new DateCandidate
            {
                RawText = "25/12/2024",
                StartIndex = dateIndex,
                Length = 10
            },
            NewDateText = "25/12/2026",
            IsSelected = true
        };

        bool success = await _service.ReplaceDatesAsync(srcPath, destPath, new[] { occurrence });
        success.Should().BeTrue();

        using (var doc = WordprocessingDocument.Open(destPath, false))
        {
            string bodyText = doc.MainDocumentPart!.Document.InnerText;
            bodyText.Should().Contain("25/12/2026");
            bodyText.Should().NotContain("25/12/2024");
        }
    }
}

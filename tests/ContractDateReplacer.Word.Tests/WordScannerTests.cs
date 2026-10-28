using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Word.Tests;

public class WordScannerTests : IDisposable
{
    private readonly string _tempDirectory;

    public WordScannerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WordScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task ScanDocument_WhenTextIsSplitAcrossMultipleRuns_ConcatenatesLogicalTextAndPreservesMappings()
    {
        string filePath = Path.Combine(_tempDirectory, "SplitRunsTest.docx");

        using (var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();
            var p = new Paragraph();

            // Run 1: Thường
            p.Append(new Run(new Text("Hôm nay, ngày ") { Space = SpaceProcessingModeValues.Preserve }));

            // Run 2: Đậm (02)
            var r2 = new Run(new Text("02"));
            r2.RunProperties = new RunProperties(new Bold());
            p.Append(r2);

            // Run 3: Thường ( tháng )
            p.Append(new Run(new Text(" tháng ") { Space = SpaceProcessingModeValues.Preserve }));

            // Run 4: Nghiêng (09)
            var r4 = new Run(new Text("09"));
            r4.RunProperties = new RunProperties(new Italic());
            p.Append(r4);

            // Run 5: Thường ( năm 2026)
            p.Append(new Run(new Text(" năm 2026") { Space = SpaceProcessingModeValues.Preserve }));

            body.Append(p);
            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        var scanner = new WordDocumentScanner();
        var segments = await scanner.ScanDocumentAsync(filePath);

        segments.Should().HaveCount(1);
        var seg = segments[0];

        seg.LogicalText.Should().Be("Hôm nay, ngày 02 tháng 09 năm 2026");
        seg.StoryType.Should().Be(StoryType.MainDocument);
        seg.RunMappings.Should().HaveCount(5);

        // Mapping checks
        seg.RunMappings[0].LogicalStart.Should().Be(0);
        seg.RunMappings[0].LogicalLength.Should().Be(14);
        seg.RunMappings[0].OriginalText.Should().Be("Hôm nay, ngày ");

        seg.RunMappings[1].LogicalStart.Should().Be(14);
        seg.RunMappings[1].LogicalLength.Should().Be(2);
        seg.RunMappings[1].IsBold.Should().BeTrue();

        seg.RunMappings[2].LogicalStart.Should().Be(16);
        seg.RunMappings[2].LogicalLength.Should().Be(7);

        seg.RunMappings[3].LogicalStart.Should().Be(23);
        seg.RunMappings[3].LogicalLength.Should().Be(2);
        seg.RunMappings[3].IsItalic.Should().BeTrue();

        seg.RunMappings[4].LogicalStart.Should().Be(25);
        seg.RunMappings[4].LogicalLength.Should().Be(9);
    }

    [Fact]
    public async Task ScanDocument_WhenParagraphContainsHyperlinkAndContentControl_ExtractsAllTextTogether()
    {
        string filePath = Path.Combine(_tempDirectory, "HyperlinkSdtTest.docx");

        using (var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();
            var p = new Paragraph();

            p.Append(new Run(new Text("Xem chi tiết tại ") { Space = SpaceProcessingModeValues.Preserve }));

            // Hyperlink run
            var link = new Hyperlink(new Run(new Text("https://example.com/hopdong")));
            p.Append(link);

            p.Append(new Run(new Text(" ký ngày ") { Space = SpaceProcessingModeValues.Preserve }));

            // Content Control inline (SdtRun)
            var sdtRun = new SdtRun(new SdtContentRun(new Run(new Text("02/09/2026"))));
            p.Append(sdtRun);

            body.Append(p);
            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        var scanner = new WordDocumentScanner();
        var segments = await scanner.ScanDocumentAsync(filePath);

        segments.Should().HaveCount(1);
        segments[0].LogicalText.Should().Be("Xem chi tiết tại https://example.com/hopdong ký ngày 02/09/2026");
        segments[0].RunMappings.Should().HaveCount(4);
    }

    [Fact]
    public async Task ScanDocument_WhenContainsTableAndNestedTable_ExtractsBothWithCorrectStoryType()
    {
        string filePath = Path.Combine(_tempDirectory, "NestedTableTest.docx");

        using (var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            // Outer Table
            var outerTable = new Table();
            var outerRow = new TableRow();
            var outerCell = new TableCell();

            outerCell.Append(new Paragraph(new Run(new Text("Văn bản trong ô bảng ngoài"))));

            // Nested Table inside Cell
            var innerTable = new Table();
            var innerRow = new TableRow();
            var innerCell = new TableCell();
            innerCell.Append(new Paragraph(new Run(new Text("Văn bản trong bảng lồng"))));
            innerRow.Append(innerCell);
            innerTable.Append(innerRow);

            outerCell.Append(innerTable);
            outerRow.Append(outerCell);
            outerTable.Append(outerRow);

            body.Append(outerTable);
            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        var scanner = new WordDocumentScanner();
        var segments = await scanner.ScanDocumentAsync(filePath);

        segments.Should().HaveCount(2);
        segments.All(s => s.StoryType == StoryType.Table).Should().BeTrue();
        segments[0].LogicalText.Should().Be("Văn bản trong ô bảng ngoài");
        segments[1].LogicalText.Should().Be("Văn bản trong bảng lồng");
        segments[1].SourceLocation.Should().Contain("Bảng lồng");
    }

    [Fact]
    public async Task ScanDocument_WhenContainsHeaderAndFooter_ExtractsHeaderAndFooterSegments()
    {
        string filePath = Path.Combine(_tempDirectory, "HeaderFooterTest.docx");

        using (var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Nội dung chính")))));

            // Header Part
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(new Paragraph(new Run(new Text("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM"))));
            string headerId = mainPart.GetIdOfPart(headerPart);

            // Footer Part
            var footerPart = mainPart.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(new Paragraph(new Run(new Text("Trang 1 / Hợp đồng kinh tế"))));
            string footerId = mainPart.GetIdOfPart(footerPart);

            // Link to document body via section properties
            var sectionProps = new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = headerId },
                new FooterReference { Type = HeaderFooterValues.Default, Id = footerId }
            );
            mainPart.Document.Body!.Append(sectionProps);
            mainPart.Document.Save();
        }

        var scanner = new WordDocumentScanner();
        var segments = await scanner.ScanDocumentAsync(filePath);

        segments.Should().HaveCount(3);
        segments.Should().Contain(s => s.StoryType == StoryType.MainDocument && s.LogicalText == "Nội dung chính");
        segments.Should().Contain(s => s.StoryType == StoryType.Header && s.LogicalText == "CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM");
        segments.Should().Contain(s => s.StoryType == StoryType.Footer && s.LogicalText == "Trang 1 / Hợp đồng kinh tế");
    }

    [Fact]
    public async Task ScanDocument_WhenContainsTextBoxContent_ExtractsAsTextBoxStoryType()
    {
        string filePath = Path.Combine(_tempDirectory, "TextBoxTest.docx");

        using (var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            var p = new Paragraph();
            p.Append(new Run(new Text("Đoạn văn chứa khung chữ: ")));

            // Textbox Content đặt trong Run -> Picture -> Vml.Shape -> Vml.TextBox chuẩn OpenXML
            var txbx = new TextBoxContent(
                new Paragraph(new Run(new Text("Nội dung bên trong TextBox ngày 15/10/2026")))
            );
            var picture = new Picture(
                new DocumentFormat.OpenXml.Vml.Shape(
                    new DocumentFormat.OpenXml.Vml.TextBox(txbx)
                )
            );
            p.Append(new Run(picture));

            body.Append(p);
            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        var scanner = new WordDocumentScanner();
        var segments = await scanner.ScanDocumentAsync(filePath);

        segments.Should().HaveCount(2);

        var bodySeg = segments.First(s => s.StoryType == StoryType.MainDocument);
        bodySeg.LogicalText.Should().Be("Đoạn văn chứa khung chữ: ");

        var tbSeg = segments.First(s => s.StoryType == StoryType.TextBox);
        tbSeg.LogicalText.Should().Be("Nội dung bên trong TextBox ngày 15/10/2026");
        tbSeg.SourceLocation.Should().Contain("Khung văn bản");
    }

    [Fact]
    public async Task ScanDocument_WhenContainsFootnotesAndEndnotes_ExtractsBoth()
    {
        string filePath = Path.Combine(_tempDirectory, "FootnoteEndnoteTest.docx");

        using (var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Nội dung có ghi chú.")))));

            // Footnote
            var fnPart = mainPart.AddNewPart<FootnotesPart>();
            fnPart.Footnotes = new Footnotes(
                new Footnote(new Paragraph(new Run(new Text("Ghi chú chân trang số 1")))) { Id = 1 }
            );

            // Endnote
            var enPart = mainPart.AddNewPart<EndnotesPart>();
            enPart.Endnotes = new Endnotes(
                new Endnote(new Paragraph(new Run(new Text("Ghi chú cuối tài liệu A")))) { Id = 1 }
            );

            mainPart.Document.Save();
        }

        var scanner = new WordDocumentScanner();
        var segments = await scanner.ScanDocumentAsync(filePath);

        segments.Should().HaveCount(3);
        segments.Should().Contain(s => s.StoryType == StoryType.MainDocument);
        segments.Should().Contain(s => s.StoryType == StoryType.Footnote && s.LogicalText == "Ghi chú chân trang số 1");
        segments.Should().Contain(s => s.StoryType == StoryType.Endnote && s.LogicalText == "Ghi chú cuối tài liệu A");
    }

    [Fact]
    public void WordOpenXmlValidator_ShouldValidateValidDocumentSuccessfully()
    {
        string filePath = Path.Combine(_tempDirectory, "ValidTest.docx");

        using (var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Hợp đồng nguyên vẹn.")))));
            mainPart.Document.Save();
        }

        var validator = new WordOpenXmlValidator();
        var (isValid, errors) = validator.ValidateDocument(filePath);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }
}

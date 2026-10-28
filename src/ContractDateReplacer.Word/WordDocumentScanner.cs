using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ContractDateReplacer.Word;

/// <summary>
/// Triển khai dịch vụ quét chuyên sâu toàn bộ 13 phân vùng tài liệu Word DOCX sử dụng Open XML SDK.
/// Tự động trích xuất chuỗi logic (LogicalText) và lập bảng ánh xạ từng ký tự tới Run XML (TextRunMapping).
/// Hoàn toàn thread-safe và hỗ trợ cả DrawingML, VML Shape lẫn AlternateContent TextBoxes.
/// </summary>
public class WordDocumentScanner : IDocumentScanner
{
    public Task<bool> CanReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath)) return Task.FromResult(false);

        if (WordFormatHelper.IsLegacyWord97Doc(filePath))
        {
            return Task.FromResult(true);
        }

        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            return Task.FromResult(doc.MainDocumentPart?.Document?.Body != null);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<List<DocumentTextSegment>> ScanDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var segments = new List<DocumentTextSegment>();
            if (!File.Exists(filePath)) return segments;

            string pathToOpen = filePath;
            string? convertedTempPath = null;

            if (WordFormatHelper.IsLegacyWord97Doc(filePath))
            {
                if (WordFormatHelper.TryAutoConvertLegacyDocToDocx(filePath, out var converted, out var err))
                {
                    pathToOpen = converted!;
                    convertedTempPath = converted;
                }
                else
                {
                    throw new InvalidOperationException(err ?? "Tệp tin này có định dạng Word 97-2003 (.doc) bị đặt nhầm đuôi .docx. Vui lòng mở bằng Microsoft Word và Lưu lại (Save As) dạng Word Document (*.docx).");
                }
            }
            else if (!WordFormatHelper.IsValidZipPackage(filePath))
            {
                throw new InvalidOperationException("Tệp tin này không phải là tài liệu Word OpenXML (.docx) hợp lệ (không chứa cấu trúc gói nén chuẩn). Vui lòng kiểm tra lại tập tin.");
            }

            try
            {
                using var doc = WordprocessingDocument.Open(pathToOpen, false);
                var mainPart = doc.MainDocumentPart;
                if (mainPart?.Document?.Body == null) return segments;

                var scannedTextBoxes = new HashSet<OpenXmlElement>();

                // 1. Quét Main Document Body (Đoạn văn, Bảng, Bảng lồng, Content Control, TextBoxes)
                ScanContainerElements(mainPart.Document.Body, segments, StoryType.MainDocument, "Nội dung chính", scannedTextBoxes, cancellationToken);

                // 2. Quét Headers (Tiêu đề trang)
                ScanHeaders(mainPart, segments, scannedTextBoxes, cancellationToken);

                // 3. Quét Footers (Chân trang)
                ScanFooters(mainPart, segments, scannedTextBoxes, cancellationToken);

            // 4. Quét Footnotes (Chú thích chân trang)
            ScanFootnotes(mainPart, segments, scannedTextBoxes, cancellationToken);

                // 5. Quét Endnotes (Chú thích cuối tài liệu)
                ScanEndnotes(mainPart, segments, scannedTextBoxes, cancellationToken);

                return segments;
            }
            finally
            {
                if (!string.IsNullOrEmpty(convertedTempPath) && File.Exists(convertedTempPath))
                {
                    try { File.Delete(convertedTempPath); } catch { }
                }
            }
        }, cancellationToken);
    }

    #region Container & Element Scanners

    private void ScanContainerElements(
        OpenXmlElement container,
        List<DocumentTextSegment> segments,
        StoryType defaultStoryType,
        string locationPrefix,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        int paragraphIndex = 0;
        int tableIndex = 0;
        int sdtIndex = 0;

        foreach (var element in container.Elements())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is Paragraph paragraph)
            {
                paragraphIndex++;
                var segment = ExtractSegmentFromParagraph(
                    paragraph,
                    defaultStoryType,
                    paragraphIndex,
                    $"{locationPrefix}, Đoạn {paragraphIndex}");

                if (segment != null) segments.Add(segment);

                // Quét TextBoxes nằm bên trong Paragraph này
                ScanTextBoxesInElement(paragraph, segments, $"{locationPrefix}, Đoạn {paragraphIndex}", scannedTextBoxes, cancellationToken);
            }
            else if (element is Table table)
            {
                tableIndex++;
                ScanTable(table, segments, $"{locationPrefix} > Bảng {tableIndex}", tableIndex, scannedTextBoxes, cancellationToken);
            }
            else if (element is SdtBlock sdtBlock)
            {
                sdtIndex++;
                string sdtTitle = sdtBlock.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value 
                               ?? sdtBlock.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value 
                               ?? $"Khối {sdtIndex}";

                var content = sdtBlock.SdtContentBlock;
                if (content != null)
                {
                    ScanContainerElements(
                        content,
                        segments,
                        StoryType.ContentControl,
                        $"{locationPrefix} > Thẻ nội dung ({sdtTitle})",
                        scannedTextBoxes,
                        cancellationToken);
                }
            }
        }

        // Quét toàn bộ TextBoxes dạng Floating hoặc Anchor trong Container nếu chưa quét qua Paragraph
        ScanFloatingTextBoxes(container, segments, locationPrefix, scannedTextBoxes, cancellationToken);
    }

    private void ScanTable(
        Table table,
        List<DocumentTextSegment> segments,
        string tableLocationPrefix,
        int tableIndex,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        int rowIndex = 0;
        foreach (var row in table.Elements<TableRow>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowIndex++;
            int cellIndex = 0;

            foreach (var cell in row.Elements<TableCell>())
            {
                cellIndex++;
                string cellPrefix = $"{tableLocationPrefix}, Dòng {rowIndex}, Ô {cellIndex}";
                int cellParagraphIndex = 0;
                int nestedTableIndex = 0;

                foreach (var cellChild in cell.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (cellChild is Paragraph p)
                    {
                        cellParagraphIndex++;
                        var segment = ExtractSegmentFromParagraph(
                            p,
                            StoryType.Table,
                            cellParagraphIndex,
                            $"{cellPrefix}, Đoạn {cellParagraphIndex}",
                            tableIndex,
                            rowIndex,
                            cellIndex);

                        if (segment != null) segments.Add(segment);

                        // Quét TextBoxes trong đoạn văn của ô
                        ScanTextBoxesInElement(p, segments, cellPrefix, scannedTextBoxes, cancellationToken);
                    }
                    else if (cellChild is Table nestedTable)
                    {
                        nestedTableIndex++;
                        ScanTable(
                            nestedTable,
                            segments,
                            $"{cellPrefix} > Bảng lồng {nestedTableIndex}",
                            nestedTableIndex,
                            scannedTextBoxes,
                            cancellationToken);
                    }
                    else if (cellChild is SdtBlock sdtCellBlock)
                    {
                        var sdtContent = sdtCellBlock.SdtContentBlock;
                        if (sdtContent != null)
                        {
                            ScanContainerElements(
                                sdtContent,
                                segments,
                                StoryType.ContentControl,
                                $"{cellPrefix} > Thẻ nội dung",
                                scannedTextBoxes,
                                cancellationToken);
                        }
                    }
                }
            }
        }
    }

    #endregion

    #region Header & Footer Scanners

    private void ScanHeaders(
        MainDocumentPart mainPart,
        List<DocumentTextSegment> segments,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        var headerRefs = mainPart.Document.Descendants<HeaderReference>().ToList();

        foreach (var headerPart in mainPart.HeaderParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string partId = mainPart.GetIdOfPart(headerPart);
            var matchingRef = headerRefs.FirstOrDefault(r => r.Id?.Value == partId);

            string headerTypeDesc = "Mặc định";
            if (matchingRef?.Type?.Value == HeaderFooterValues.First) headerTypeDesc = "Trang đầu";
            else if (matchingRef?.Type?.Value == HeaderFooterValues.Even) headerTypeDesc = "Trang chẵn";

            if (headerPart.Header != null)
            {
                var headerSegments = new List<DocumentTextSegment>();
                ScanContainerElements(
                    headerPart.Header,
                    headerSegments,
                    StoryType.Header,
                    $"Tiêu đề trang (Header - {headerTypeDesc})",
                    scannedTextBoxes,
                    cancellationToken);

                foreach (var seg in headerSegments)
                {
                    seg.StoryType = StoryType.Header;
                    seg.HeaderType = headerTypeDesc;
                    segments.Add(seg);
                }
            }
        }
    }

    private void ScanFooters(
        MainDocumentPart mainPart,
        List<DocumentTextSegment> segments,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        var footerRefs = mainPart.Document.Descendants<FooterReference>().ToList();

        foreach (var footerPart in mainPart.FooterParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string partId = mainPart.GetIdOfPart(footerPart);
            var matchingRef = footerRefs.FirstOrDefault(r => r.Id?.Value == partId);

            string footerTypeDesc = "Mặc định";
            if (matchingRef?.Type?.Value == HeaderFooterValues.First) footerTypeDesc = "Trang đầu";
            else if (matchingRef?.Type?.Value == HeaderFooterValues.Even) footerTypeDesc = "Trang chẵn";

            if (footerPart.Footer != null)
            {
                var footerSegments = new List<DocumentTextSegment>();
                ScanContainerElements(
                    footerPart.Footer,
                    footerSegments,
                    StoryType.Footer,
                    $"Chân trang (Footer - {footerTypeDesc})",
                    scannedTextBoxes,
                    cancellationToken);

                foreach (var seg in footerSegments)
                {
                    seg.StoryType = StoryType.Footer;
                    seg.FooterType = footerTypeDesc;
                    segments.Add(seg);
                }
            }
        }
    }

    #endregion

    #region Footnotes & Endnotes Scanners

    private void ScanFootnotes(
        MainDocumentPart mainPart,
        List<DocumentTextSegment> segments,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        if (mainPart.FootnotesPart?.Footnotes == null) return;

        int footnoteIndex = 0;
        foreach (var footnote in mainPart.FootnotesPart.Footnotes.Elements<Footnote>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (footnote.Type?.Value == FootnoteEndnoteValues.Separator ||
                footnote.Type?.Value == FootnoteEndnoteValues.ContinuationSeparator)
            {
                continue;
            }

            footnoteIndex++;
            var fnSegments = new List<DocumentTextSegment>();
            ScanContainerElements(
                footnote,
                fnSegments,
                StoryType.Footnote,
                $"Chú thích chân trang {footnoteIndex}",
                scannedTextBoxes,
                cancellationToken);

            foreach (var seg in fnSegments)
            {
                seg.StoryType = StoryType.Footnote;
                segments.Add(seg);
            }
        }
    }

    private void ScanEndnotes(
        MainDocumentPart mainPart,
        List<DocumentTextSegment> segments,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        if (mainPart.EndnotesPart?.Endnotes == null) return;

        int endnoteIndex = 0;
        foreach (var endnote in mainPart.EndnotesPart.Endnotes.Elements<Endnote>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (endnote.Type?.Value == FootnoteEndnoteValues.Separator ||
                endnote.Type?.Value == FootnoteEndnoteValues.ContinuationSeparator)
            {
                continue;
            }

            endnoteIndex++;
            var enSegments = new List<DocumentTextSegment>();
            ScanContainerElements(
                endnote,
                enSegments,
                StoryType.Endnote,
                $"Chú thích cuối tài liệu {endnoteIndex}",
                scannedTextBoxes,
                cancellationToken);

            foreach (var seg in enSegments)
            {
                seg.StoryType = StoryType.Endnote;
                segments.Add(seg);
            }
        }
    }

    #endregion

    #region TextBox Scanners (DrawingML & VML)

    private List<OpenXmlElement> FindTextBoxElements(OpenXmlElement container, HashSet<OpenXmlElement> scannedTextBoxes)
    {
        var result = new List<OpenXmlElement>();

        foreach (var desc in container.Descendants())
        {
            if (desc is TextBoxContent || desc.LocalName == "txbxContent")
            {
                if (!scannedTextBoxes.Contains(desc))
                {
                    scannedTextBoxes.Add(desc);
                    result.Add(desc);
                }
            }
        }

        return result;
    }

    private void ScanTextBoxesInElement(
        OpenXmlElement element,
        List<DocumentTextSegment> segments,
        string parentLocation,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        var txbxList = FindTextBoxElements(element, scannedTextBoxes);
        int tbNum = 0;

        foreach (var txbx in txbxList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tbNum++;
            ScanTextBoxContent(txbx, segments, $"{parentLocation} > Khung văn bản (TextBox {tbNum})", cancellationToken);
        }
    }

    private void ScanFloatingTextBoxes(
        OpenXmlElement container,
        List<DocumentTextSegment> segments,
        string locationPrefix,
        HashSet<OpenXmlElement> scannedTextBoxes,
        CancellationToken cancellationToken)
    {
        var txbxList = FindTextBoxElements(container, scannedTextBoxes);
        int tbNum = 0;

        foreach (var txbx in txbxList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tbNum++;
            ScanTextBoxContent(txbx, segments, $"{locationPrefix} > Khung văn bản nổi {tbNum}", cancellationToken);
        }
    }

    private void ScanTextBoxContent(
        OpenXmlElement txbx,
        List<DocumentTextSegment> segments,
        string location,
        CancellationToken cancellationToken)
    {
        int pIndex = 0;
        foreach (var p in txbx.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pIndex++;

            var seg = ExtractSegmentFromParagraph(
                p,
                StoryType.TextBox,
                pIndex,
                $"{location}, Đoạn {pIndex}");

            if (seg != null)
            {
                segments.Add(seg);
            }
        }
    }

    #endregion

    #region Paragraph & Run Extraction Core Logic

    /// <summary>
    /// Trích xuất toàn bộ các Run hợp lệ trong Paragraph, ghép nối thành LogicalText và lập bảng TextRunMapping.
    /// Hỗ trợ cả các Run nằm bên trong Hyperlink, SdtRun, SimpleField, SmartTag, InsertedRun.
    /// Bỏ qua các Run nằm trong TextBoxContent con (được quét riêng).
    /// </summary>
    private DocumentTextSegment? ExtractSegmentFromParagraph(
        Paragraph paragraph,
        StoryType storyType,
        int paragraphIndex,
        string location,
        int tableIndex = -1,
        int rowIndex = -1,
        int cellIndex = -1,
        string? headerType = null,
        string? footerType = null)
    {
        var runs = GetParagraphRuns(paragraph).ToList();
        if (runs.Count == 0) return null;

        var segment = new DocumentTextSegment
        {
            Id = Guid.NewGuid().ToString("N"),
            StoryType = storyType,
            ParagraphIndex = paragraphIndex,
            TableIndex = tableIndex,
            RowIndex = rowIndex,
            CellIndex = cellIndex,
            HeaderType = headerType,
            FooterType = footerType,
            SourceLocation = location,
            IsSupportedForReplacement = true
        };

        var logicalSb = new StringBuilder();
        int runIndex = 0;

        foreach (var run in runs)
        {
            var textElements = run.Elements<Text>();
            var runTextSb = new StringBuilder();

            foreach (var t in textElements)
            {
                runTextSb.Append(t.Text);
            }

            string runText = runTextSb.ToString();
            if (string.IsNullOrEmpty(runText)) continue;

            int logicalStart = logicalSb.Length;
            logicalSb.Append(runText);

            var rPr = run.RunProperties;
            bool isBold = rPr?.Bold != null && (rPr.Bold.Val == null || rPr.Bold.Val.Value);
            bool isItalic = rPr?.Italic != null && (rPr.Italic.Val == null || rPr.Italic.Val.Value);
            bool isUnderline = rPr?.Underline != null;
            string? color = rPr?.Color?.Val?.Value;
            string? fontName = rPr?.RunFonts?.Ascii?.Value ?? rPr?.RunFonts?.HighAnsi?.Value;
            string? fontSize = rPr?.FontSize?.Val?.Value;

            segment.RunMappings.Add(new TextRunMapping
            {
                LogicalStart = logicalStart,
                LogicalLength = runText.Length,
                RunIndex = runIndex,
                LocalStart = 0,
                LocalLength = runText.Length,
                RunId = $"run_{runIndex}",
                OriginalText = runText,
                IsBold = isBold,
                IsItalic = isItalic,
                IsUnderline = isUnderline,
                Color = color,
                FontName = fontName,
                FontSize = fontSize
            });

            runIndex++;
        }

        string logicalText = logicalSb.ToString();
        if (string.IsNullOrWhiteSpace(logicalText)) return null;

        segment.LogicalText = logicalText;

        // Nhận diện Field Code (w:fldSimple)
        var simpleField = paragraph.Descendants<SimpleField>().FirstOrDefault()
                       ?? paragraph.Ancestors<SimpleField>().FirstOrDefault();
        if (simpleField != null)
        {
            segment.IsFieldCode = true;
            segment.FieldInstruction = simpleField.Instruction?.Value;
        }

        // Nhận diện Thẻ nội dung Content Control (w:sdt)
        var sdt = paragraph.Ancestors().FirstOrDefault(a => a is SdtBlock || a is SdtRun || a is SdtCell || a is SdtRow)
               ?? paragraph.Descendants<SdtRun>().FirstOrDefault();
        if (sdt != null)
        {
            segment.IsContentControl = true;
            var sdtPr = sdt.GetFirstChild<SdtProperties>();
            segment.ContentControlTitle = sdtPr?.GetFirstChild<Tag>()?.Val?.Value ?? sdtPr?.GetFirstChild<SdtAlias>()?.Val?.Value;
            segment.IsDatePickerContentControl = sdtPr?.GetFirstChild<SdtContentDate>() != null;
        }

        return segment;
    }

    /// <summary>
    /// Thu thập tất cả các Run trực thuộc hoặc lồng trong Paragraph (kể cả Hyperlink, SdtRun, SmartTag, InsertedRun, Field),
    /// nhưng loại bỏ các Run thuộc về TextBox con (vì TextBox được xử lý thành Segment riêng biệt).
    /// </summary>
    private IEnumerable<Run> GetParagraphRuns(Paragraph paragraph)
    {
        // Thu thập các Textbox con nếu có
        var childTextBoxes = paragraph.Descendants()
            .Where(e => e is TextBoxContent || e.LocalName == "txbxContent")
            .ToHashSet();

        foreach (var desc in paragraph.Descendants<Run>())
        {
            // Nếu Run này nằm bên trong một TextBoxContent con của Paragraph thì bỏ qua
            if (childTextBoxes.Count > 0)
            {
                var parentTextBox = desc.Ancestors()
                    .FirstOrDefault(a => a is TextBoxContent || a.LocalName == "txbxContent");
                if (parentTextBox != null && childTextBoxes.Contains(parentTextBox))
                {
                    continue;
                }
            }

            yield return desc;
        }
    }

    #endregion
}

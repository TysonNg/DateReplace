using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.Word;

/// <summary>
/// Dịch vụ thay thế ngày tháng vào tài liệu Word OpenXML (Core Hardcore).
/// Đảm bảo tuyệt đối:
/// 1. Không phá vỡ cấu trúc XML Word (OpenXmlValidator pass 0 errors).
/// 2. Giữ nguyên định dạng RunProperties (Font, Size, Color, Bold, Italic, Underline).
/// 3. Giữ nguyên cấu trúc Table, Header, Footer, TextBox, Footnotes, Endnotes.
/// 4. Xử lý triệt để ngày bị chia cắt thành nhiều Runs (Run fragmentation).
/// 5. Xử lý nhiều ngày trong cùng một Paragraph từ phải qua trái (descending StartIndex).
/// 6. Bảo tồn thuộc tính xml:space="preserve".
/// </summary>
public class WordDocumentReplacementService : IDocumentReplacementService
{
    private readonly WordOpenXmlValidator _validator;
    private readonly ILogger<WordDocumentReplacementService> _logger;

    public WordDocumentReplacementService(
        WordOpenXmlValidator validator,
        ILogger<WordDocumentReplacementService> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public async Task<bool> ReplaceDatesAsync(
        string sourceFilePath,
        string destinationFilePath,
        IEnumerable<ReplacementOccurrence> selectedOccurrences,
        bool preserveOriginalDateFormat = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Không tìm thấy file nguồn: {sourceFilePath}");
        }

        var occurrencesList = selectedOccurrences.Where(o => o.IsSelected).ToList();
        if (occurrencesList.Count == 0)
        {
            _logger.LogInformation("Không có vị trí ngày nào được chọn thay thế cho {File}.", Path.GetFileName(sourceFilePath));
            return false;
        }

        string destDir = Path.GetDirectoryName(Path.GetFullPath(destinationFilePath))!;
        Directory.CreateDirectory(destDir);

        string? convertedTempPath = null;
        if (WordFormatHelper.IsLegacyWord97Doc(sourceFilePath))
        {
            if (WordFormatHelper.TryAutoConvertLegacyDocToDocx(sourceFilePath, out var converted, out var err))
            {
                convertedTempPath = converted;
                File.Copy(converted!, destinationFilePath, overwrite: true);
            }
            else
            {
                throw new InvalidOperationException(err ?? "Không thể chuyển đổi tài liệu Word 97-2003 cũ sang .docx.");
            }
        }
        else
        {
            // Copy file nguồn sang file đích trước khi can thiệp (bảo vệ file gốc 100%)
            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        }

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (var wordDoc = WordprocessingDocument.Open(destinationFilePath, true))
            {
                // Nhóm các điểm thay thế theo Segment/Vị trí
                var groupedBySegment = occurrencesList
                    .GroupBy(o => o.Segment.Id)
                    .ToList();

                int totalReplaced = 0;

                foreach (var group in groupedBySegment)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var firstOccurrence = group.First();
                    var segment = firstOccurrence.Segment;

                    // Sắp xếp các điểm thay thế trong cùng một đoạn theo StartIndex GIẢM DẦN
                    // (thay thế từ phải sang trái để không làm lệch offset của các ký tự đứng trước)
                    var sortedOccurrences = group
                        .OrderByDescending(o => o.Candidate.StartIndex)
                        .ToList();

                    var paragraph = FindParagraphInDocument(wordDoc, segment);
                    if (paragraph == null)
                    {
                        _logger.LogWarning("Không thể định vị Paragraph cho Segment {Id} ({Location})", segment.Id, segment.SourceLocation);
                        continue;
                    }

                    foreach (var occurrence in sortedOccurrences)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        bool replaced = ReplaceDateInParagraph(
                            paragraph,
                            occurrence.Candidate.StartIndex,
                            occurrence.Candidate.Length,
                            occurrence.NewDateText);

                        if (replaced)
                        {
                            occurrence.Status = ReplacementStatus.Success;
                            totalReplaced++;
                        }
                        else
                        {
                            occurrence.Status = ReplacementStatus.Failed;
                            occurrence.SafetyNote = "Không tìm thấy chuỗi ngày tại vị trí Run tương ứng";
                        }
                    }
                }

                // Lưu các thay đổi vào tài liệu OpenXML
                wordDoc.MainDocumentPart?.Document.Save();
                foreach (var headerPart in wordDoc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
                {
                    headerPart.Header?.Save();
                }
                foreach (var footerPart in wordDoc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
                {
                    footerPart.Footer?.Save();
                }
                if (wordDoc.MainDocumentPart?.FootnotesPart != null)
                {
                    wordDoc.MainDocumentPart.FootnotesPart.Footnotes?.Save();
                }
                if (wordDoc.MainDocumentPart?.EndnotesPart != null)
                {
                    wordDoc.MainDocumentPart.EndnotesPart.Endnotes?.Save();
                }
            }

            // Kiểm tra tính toàn vẹn OpenXML sau khi thay thế
            var (isValid, errors) = _validator.ValidateDocument(destinationFilePath);
            if (!isValid)
            {
                _logger.LogError("Lỗi xác thực OpenXML sau khi thay thế {File}: {Errors}",
                    destinationFilePath, string.Join("; ", errors));
                throw new InvalidOperationException($"Tài liệu Word sau khi thay thế có lỗi OpenXML: {errors.FirstOrDefault()}");
            }

            _logger.LogInformation("Thay thế thành công {Count} vị trí ngày trong {File}. OpenXML hợp lệ.", occurrencesList.Count, destinationFilePath);
            return true;
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

    /// <summary>
    /// Thay thế một chuỗi ngày trong Paragraph OpenXML tại khoảng offset [startIndex, startIndex + length).
    /// Hỗ trợ cả trường hợp nằm trọn trong 1 Run lẫn trường hợp ngày bị chia cắt qua nhiều Runs.
    /// </summary>
    public bool ReplaceDateInParagraph(Paragraph paragraph, int startIndex, int length, string newDateText)
    {
        var runs = GetParagraphRuns(paragraph).ToList();
        if (runs.Count == 0) return false;

        int currentOffset = 0;
        int targetEnd = startIndex + length;

        Run? startRun = null;
        int startRunOffset = 0;
        string startRunText = string.Empty;

        Run? endRun = null;
        int endRunOffset = 0;
        string endRunText = string.Empty;

        var middleRuns = new List<Run>();

        foreach (var run in runs)
        {
            string runText = GetRunText(run);
            int runStart = currentOffset;
            int runEnd = currentOffset + runText.Length;
            currentOffset = runEnd;

            // Run nằm hoàn toàn trước ngày cần thay
            if (runEnd <= startIndex)
            {
                continue;
            }

            // Run nằm hoàn toàn sau ngày cần thay
            if (runStart >= targetEnd)
            {
                continue;
            }

            // Run đầu tiên giao với khoảng ngày
            if (startRun == null)
            {
                startRun = run;
                startRunOffset = runStart;
                startRunText = runText;
            }

            // Ghi nhận run cuối cùng giao với khoảng ngày
            if (runEnd >= targetEnd || runStart < targetEnd)
            {
                endRun = run;
                endRunOffset = runStart;
                endRunText = runText;
            }

            // Các run nằm ở giữa (không phải run đầu và không phải run cuối)
            if (run != startRun && runEnd < targetEnd)
            {
                middleRuns.Add(run);
            }
        }

        if (startRun == null || endRun == null)
        {
            return false;
        }

        // TH 1: Ngày nằm trọn trong 1 Run duy nhất
        if (startRun == endRun)
        {
            int relStart = startIndex - startRunOffset;
            string before = startRunText.Substring(0, relStart);
            string after = startRunText.Substring(relStart + length);
            string replacedRunText = before + newDateText + after;

            SetRunText(startRun, replacedRunText);

            // Cập nhật thẻ SDT DatePicker (w:date / w:fullDate) nếu Paragraph hoặc Run nằm trong DatePicker
            UpdateSdtDatePickerIfPresent(paragraph, startRun, newDateText);

            // Chuyển đổi (unlink) Field Code nếu Run nằm trong w:fldSimple
            UnlinkFieldCodeIfPresent(startRun);

            return true;
        }

        // Trường hợp ngày bị phân tách qua nhiều Run liên tiếp:
        // 1. Run đầu tiên: Giữ phần text trước ngày + gắn toàn bộ chuỗi ngày mới
        int startRel = startIndex - startRunOffset;
        string startBefore = startRunText.Substring(0, startRel);
        SetRunText(startRun, startBefore + newDateText);

        // 2. Các Run ở giữa nằm hoàn toàn trong phạm vi ngày: Xóa trắng text
        foreach (var midRun in middleRuns)
        {
            SetRunText(midRun, string.Empty);
        }

        // 3. Run cuối cùng: Giữ phần text sau ngày
        int endRel = targetEnd - endRunOffset;
        string endAfter = endRel < endRunText.Length ? endRunText.Substring(endRel) : string.Empty;
        if (endRun != startRun)
        {
            SetRunText(endRun, endAfter);
        }

        // Cập nhật thẻ SDT DatePicker (w:date / w:fullDate) nếu Paragraph hoặc Run nằm trong DatePicker
        UpdateSdtDatePickerIfPresent(paragraph, startRun, newDateText);

        // Chuyển đổi (unlink) Field Code nếu Run nằm trong w:fldSimple
        UnlinkFieldCodeIfPresent(startRun);

        return true;
    }

    private void UpdateSdtDatePickerIfPresent(Paragraph paragraph, Run run, string newDateText)
    {
        var sdtElement = run.Ancestors().FirstOrDefault(a => a is SdtBlock || a is SdtRun || a is SdtCell || a is SdtRow)
                      ?? paragraph.Ancestors().FirstOrDefault(a => a is SdtBlock || a is SdtRun || a is SdtCell || a is SdtRow);

        if (sdtElement != null)
        {
            var sdtPr = sdtElement.GetFirstChild<SdtProperties>();
            var sdtDate = sdtPr?.GetFirstChild<SdtContentDate>();
            if (sdtDate != null)
            {
                if (DateTime.TryParseExact(newDateText, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy/MM/dd" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dt) ||
                    DateTime.TryParse(newDateText, System.Globalization.CultureInfo.InvariantCulture, out dt) ||
                    DateTime.TryParse(newDateText, out dt))
                {
                    sdtDate.FullDate = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc);
                    _logger.LogInformation("Đã cập nhật w:fullDate của SDT DatePicker thành {Date:yyyy-MM-ddTHH:mm:ssZ}", dt);
                }
            }
        }
    }

    private void UnlinkFieldCodeIfPresent(Run startRun)
    {
        var simpleField = startRun.Ancestors<SimpleField>().FirstOrDefault();
        if (simpleField != null)
        {
            var parent = simpleField.Parent;
            if (parent != null)
            {
                var elementsToMove = simpleField.ChildElements.ToList();
                foreach (var child in elementsToMove)
                {
                    child.Remove();
                    parent.InsertBefore(child, simpleField);
                }
                simpleField.Remove();
                _logger.LogInformation("Đã chuyển đổi (unlink) SimpleField thành văn bản tĩnh để tránh Word tự động ghi đè ngày.");
            }
        }
    }

    /// <summary>
    /// Thu thập text tổng hợp từ tất cả các thẻ Text bên trong Run.
    /// </summary>
    private static string GetRunText(Run run)
    {
        var sb = new StringBuilder();
        foreach (var t in run.Elements<Text>())
        {
            sb.Append(t.Text);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Cập nhật nội dung text cho Run, gộp thành 1 Text node duy nhất và bảo toàn xml:space="preserve".
    /// </summary>
    private static void SetRunText(Run run, string text)
    {
        var textElements = run.Elements<Text>().ToList();
        if (textElements.Count > 0)
        {
            textElements[0].Text = text;
            if (text.StartsWith(" ") || text.EndsWith(" ") || text.Contains("  "))
            {
                textElements[0].Space = SpaceProcessingModeValues.Preserve;
            }
            else
            {
                textElements[0].Space = null;
            }

            for (int i = 1; i < textElements.Count; i++)
            {
                textElements[i].Remove();
            }
        }
        else
        {
            var textNode = new Text(text);
            if (text.StartsWith(" ") || text.EndsWith(" ") || text.Contains("  "))
            {
                textNode.Space = SpaceProcessingModeValues.Preserve;
            }
            run.AppendChild(textNode);
        }
    }

    /// <summary>
    /// Tìm Paragraph trong tài liệu OpenXML tương ứng với Segment.
    /// Ưu tiên tìm theo tọa độ chính xác (StoryType, Table, Header, Footer...),
    /// và kiểm tra đối chiếu bằng LogicalText để đảm bảo độ chính xác 100%.
    /// </summary>
    private Paragraph? FindParagraphInDocument(WordprocessingDocument doc, DocumentTextSegment segment)
    {
        switch (segment.StoryType)
        {
            case StoryType.MainDocument:
            {
                var body = doc.MainDocumentPart?.Document.Body;
                if (body == null) return null;

                int pIndex = 0;
                foreach (var elem in body.Elements())
                {
                    if (elem is Paragraph p)
                    {
                        pIndex++;
                        if (pIndex == segment.ParagraphIndex)
                        {
                            if (GetParagraphLogicalText(p) == segment.LogicalText)
                            {
                                return p;
                            }
                            break;
                        }
                    }
                }

                // Fallback: Tìm Paragraph theo LogicalText
                return body.Descendants<Paragraph>()
                    .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
            }

            case StoryType.Table:
            {
                var body = doc.MainDocumentPart?.Document.Body;
                if (body == null) return null;

                var tables = body.Descendants<Table>().ToList();
                if (segment.TableIndex > 0 && segment.TableIndex <= tables.Count)
                {
                    var table = tables[segment.TableIndex - 1];
                    var rows = table.Elements<TableRow>().ToList();
                    if (segment.RowIndex > 0 && segment.RowIndex <= rows.Count)
                    {
                        var row = rows[segment.RowIndex - 1];
                        var cells = row.Elements<TableCell>().ToList();
                        if (segment.CellIndex > 0 && segment.CellIndex <= cells.Count)
                        {
                            var cell = cells[segment.CellIndex - 1];
                            var paragraphs = cell.Descendants<Paragraph>().ToList();
                            if (segment.ParagraphIndex > 0 && segment.ParagraphIndex <= paragraphs.Count)
                            {
                                var candidateP = paragraphs[segment.ParagraphIndex - 1];
                                if (GetParagraphLogicalText(candidateP) == segment.LogicalText)
                                {
                                    return candidateP;
                                }
                            }
                        }
                    }
                }

                // Fallback: Tìm trong tất cả các bảng theo LogicalText
                return body.Descendants<Table>()
                    .SelectMany(t => t.Descendants<Paragraph>())
                    .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
            }

            case StoryType.Header:
            {
                if (doc.MainDocumentPart == null) return null;
                foreach (var headerPart in doc.MainDocumentPart.HeaderParts)
                {
                    if (headerPart.Header == null) continue;
                    var match = headerPart.Header.Descendants<Paragraph>()
                        .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                    if (match != null) return match;
                }
                return null;
            }

            case StoryType.Footer:
            {
                if (doc.MainDocumentPart == null) return null;
                foreach (var footerPart in doc.MainDocumentPart.FooterParts)
                {
                    if (footerPart.Footer == null) continue;
                    var match = footerPart.Footer.Descendants<Paragraph>()
                        .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                    if (match != null) return match;
                }
                return null;
            }

            case StoryType.Footnote:
            {
                if (doc.MainDocumentPart?.FootnotesPart?.Footnotes != null)
                {
                    var match = doc.MainDocumentPart.FootnotesPart.Footnotes.Descendants<Paragraph>()
                        .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                    if (match != null) return match;
                }
                break;
            }

            case StoryType.Endnote:
            {
                if (doc.MainDocumentPart?.EndnotesPart?.Endnotes != null)
                {
                    var match = doc.MainDocumentPart.EndnotesPart.Endnotes.Descendants<Paragraph>()
                        .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                    if (match != null) return match;
                }
                break;
            }

            case StoryType.TextBox:
            {
                if (doc.MainDocumentPart == null) return null;
                var allTxbx = doc.MainDocumentPart.Document.Descendants()
                    .Where(d => d is TextBoxContent || d.LocalName == "txbxContent");

                foreach (var txbx in allTxbx)
                {
                    var match = txbx.Descendants<Paragraph>()
                        .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                    if (match != null) return match;
                }
                return null;
            }

            default:
            {
                if (doc.MainDocumentPart != null)
                {
                    var match = doc.MainDocumentPart.Document.Descendants<Paragraph>()
                        .FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                    if (match != null) return match;

                    foreach (var hp in doc.MainDocumentPart.HeaderParts)
                    {
                        var m = hp.Header?.Descendants<Paragraph>().FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                        if (m != null) return m;
                    }

                    foreach (var fp in doc.MainDocumentPart.FooterParts)
                    {
                        var m = fp.Footer?.Descendants<Paragraph>().FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                        if (m != null) return m;
                    }

                    if (doc.MainDocumentPart.FootnotesPart?.Footnotes != null)
                    {
                        var m = doc.MainDocumentPart.FootnotesPart.Footnotes.Descendants<Paragraph>().FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                        if (m != null) return m;
                    }

                    if (doc.MainDocumentPart.EndnotesPart?.Endnotes != null)
                    {
                        var m = doc.MainDocumentPart.EndnotesPart.Endnotes.Descendants<Paragraph>().FirstOrDefault(p => GetParagraphLogicalText(p) == segment.LogicalText);
                        if (m != null) return m;
                    }
                }
                return null;
            }
        }

        return null;
    }

    private string GetParagraphLogicalText(Paragraph paragraph)
    {
        var sb = new StringBuilder();
        foreach (var run in GetParagraphRuns(paragraph))
        {
            sb.Append(GetRunText(run));
        }
        return sb.ToString();
    }

    private IEnumerable<Run> GetParagraphRuns(Paragraph paragraph)
    {
        var childTextBoxes = paragraph.Descendants()
            .Where(e => e is TextBoxContent || e.LocalName == "txbxContent")
            .ToHashSet();

        foreach (var desc in paragraph.Descendants<Run>())
        {
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
}

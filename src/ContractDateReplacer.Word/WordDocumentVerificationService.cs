using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.Word;

/// <summary>
/// Dịch vụ kiểm tra và xác minh độc lập (Post-Replacement Verification Engine) sau khi thay thế ngày.
/// Thực hiện quét lại toàn bộ 13 phân vùng Word, dò tìm lại ngày tháng, so sánh ngày cũ/ngày mới,
/// kiểm tra nghiêm ngặt bảo vệ dữ liệu nhân thân (ngày sinh/CCCD) và thẩm định tính toàn vẹn OpenXML.
/// </summary>
public class WordDocumentVerificationService : IDocumentVerificationService
{
    private readonly IDocumentScanner _scanner;
    private readonly IDateDetector _detector;
    private readonly WordOpenXmlValidator _validator;
    private readonly ILogger<WordDocumentVerificationService> _logger;

    public WordDocumentVerificationService(
        IDocumentScanner scanner,
        IDateDetector detector,
        WordOpenXmlValidator validator,
        ILogger<WordDocumentVerificationService> logger)
    {
        _scanner = scanner;
        _detector = detector;
        _validator = validator;
        _logger = logger;
    }

    public async Task<VerificationResult> VerifyOutputAsync(
        string sourceFilePath,
        string outputFilePath,
        IReadOnlyList<ReplacementOccurrence> originalOccurrences,
        CancellationToken cancellationToken = default)
    {
        var result = new VerificationResult
        {
            SourceFilePath = sourceFilePath,
            OutputFilePath = outputFilePath,
            ExpectedReplacements = originalOccurrences.Count(o => o.IsSelected)
        };

        // 1. Kiểm tra tồn tại và khả năng mở file output
        if (!File.Exists(outputFilePath))
        {
            result.CanOpenDocument = false;
            result.IsValidXmlStructure = false;
            result.IntegrityStatus = "Invalid";
            result.FinalStatus = FileScanStatus.Error;
            result.SafetyScore = 0;
            result.Issues.Add($"File đầu ra không tồn tại: {outputFilePath}");
            return result;
        }

        bool canRead = await _scanner.CanReadAsync(outputFilePath, cancellationToken);
        if (!canRead)
        {
            result.CanOpenDocument = false;
            result.IsValidXmlStructure = false;
            result.IntegrityStatus = "Invalid";
            result.FinalStatus = FileScanStatus.Error;
            result.SafetyScore = 0;
            result.Issues.Add($"Không thể mở tài liệu đầu ra bằng Word OpenXML: {outputFilePath}");
            return result;
        }

        // 2. Thẩm định tính toàn vẹn cấu trúc OpenXML qua OpenXmlValidator
        var (isValidXml, xmlErrors) = _validator.ValidateDocument(outputFilePath);
        result.IsValidXmlStructure = isValidXml;
        if (!isValidXml)
        {
            result.IntegrityStatus = "Invalid";
            result.FinalStatus = FileScanStatus.Error;
            result.SafetyScore = 0;
            foreach (var err in xmlErrors)
            {
                result.Issues.Add($"[Lỗi OpenXML] {err}");
            }
        }

        // 3. Quét lại toàn bộ các phân vùng tài liệu trong file output (Re-scan 13 Story Types)
        var outputSegments = await _scanner.ScanDocumentAsync(outputFilePath, cancellationToken);

        // 4. Dò tìm lại toàn bộ ngày tháng trong file output (Re-detect)
        var allOutputCandidates = new List<(DocumentTextSegment Segment, DateCandidate Candidate)>();
        foreach (var seg in outputSegments)
        {
            var detected = _detector.DetectDates(seg);
            foreach (var cand in detected)
            {
                allOutputCandidates.Add((seg, cand));
            }
        }
        result.NewDatesFoundCount = allOutputCandidates.Count;

        // 5. Đối soát từng vị trí ngày được yêu cầu thay thế (IsSelected == true)
        var selectedToReplace = originalOccurrences.Where(o => o.IsSelected).ToList();

        foreach (var occ in selectedToReplace)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Tìm phân đoạn tương ứng trong output
            var matchingOutputSeg = FindMatchingSegment(outputSegments, occ.Segment);

            if (matchingOutputSeg == null)
            {
                result.Issues.Add($"Không tìm thấy phân đoạn {occ.Segment.SourceLocation} trong file đầu ra.");
                result.MissingReplacements.Add(occ);
                continue;
            }

            // A. Kiểm tra ngày mới có xuất hiện trong phân đoạn không
            bool newDateFound = MatchesOccurrence(matchingOutputSeg, occ, originalOccurrences, occ.NewDateText);
            if (newDateFound)
            {
                result.ActualReplacementsDone++;
            }
            else
            {
                result.MissingReplacements.Add(occ);
                result.Issues.Add($"Không tìm thấy ngày mới '{occ.NewDateText}' tại vị trí {occ.Segment.SourceLocation}.");
            }

            // B. Kiểm tra ngày cũ còn sót lại không
            bool oldDateStillPresent = !newDateFound && matchingOutputSeg.LogicalText.Contains(occ.Candidate.RawText, StringComparison.OrdinalIgnoreCase);
            if (oldDateStillPresent && !string.Equals(occ.Candidate.RawText, occ.NewDateText, StringComparison.OrdinalIgnoreCase))
            {
                result.UnexpectedOldDatesRemaining++;
                result.Issues.Add($"[THAY THẾ CHƯA TRIỆT ĐỂ] Ngày cũ '{occ.Candidate.RawText}' vẫn còn sót lại tại {occ.Segment.SourceLocation}!");
            }
        }

        // 6. Đối soát các ngày KHÔNG ĐƯỢC THAY (Bảo vệ dữ liệu nhân thân & ngày bỏ chọn)
        var intentionallyPreserved = originalOccurrences.Where(o => !o.IsSelected).ToList();

        foreach (var preserved in intentionallyPreserved)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matchingOutputSeg = FindMatchingSegment(outputSegments, preserved.Segment);

            if (matchingOutputSeg == null)
            {
                string missing = $"Không tìm thấy phân đoạn chứa ngày giữ nguyên tại {preserved.Segment.SourceLocation}.";
                result.UnexpectedModifications.Add(missing);
                result.Issues.Add(missing);
                continue;
            }
            // Kiểm tra ngày không được thay có còn nguyên vẹn trong output không
            bool preservedIntact = MatchesOccurrence(matchingOutputSeg, preserved, originalOccurrences, preserved.Candidate.RawText);

            if (preservedIntact)
            {
                result.IntentionallySkippedDatesRemaining++;
            }
            else
            {
                // Nếu đây là dữ liệu nhân thân (ngày sinh hoặc CCCD) mà bị biến mất hoặc thay đổi:
                bool isSensitive = preserved.Candidate.Classification.Type is DateClassificationType.BirthDate
                                                                            or DateClassificationType.IdentityIssueDate
                                                                            or DateClassificationType.IdentityExpiryDate;

                string warnMsg = isSensitive
                    ? $"[NGUY HIỂM - VI PHẠM DỮ LIỆU NHÂN THÂN] Ngày nhạy cảm '{preserved.Candidate.RawText}' ({preserved.Candidate.Classification.Type}) tại {preserved.Segment.SourceLocation} đã bị sửa đổi trái phép!"
                    : $"Ngày giữ nguyên '{preserved.Candidate.RawText}' không còn xuất hiện nguyên vẹn tại {preserved.Segment.SourceLocation}.";

                result.UnexpectedModifications.Add(warnMsg);
                result.Issues.Add(warnMsg);
            }
        }

        // 7. Đánh giá SafetyScore và FinalStatus
        CalculateSafetyScore(result);

        _logger.LogInformation("Hoàn tất xác minh {File}. Trạng thái: {Status}, SafetyScore: {Score}%, Lỗi sót: {Missing}, Sửa đổi trái phép: {Unexpected}",
            Path.GetFileName(outputFilePath), result.FinalStatus, result.SafetyScore, result.MissingReplacements.Count, result.UnexpectedModifications.Count);

        return result;
    }

    private static bool SameLocation(DocumentTextSegment a, DocumentTextSegment b)
    {
        return a.StoryType == b.StoryType &&
            a.ParagraphIndex == b.ParagraphIndex && a.TableIndex == b.TableIndex &&
            a.RowIndex == b.RowIndex && a.CellIndex == b.CellIndex &&
            a.HeaderType == b.HeaderType && a.FooterType == b.FooterType &&
            (string.IsNullOrEmpty(a.SourceLocation) || string.IsNullOrEmpty(b.SourceLocation) ||
                string.Equals(a.SourceLocation, b.SourceLocation, StringComparison.Ordinal));
    }

    private static bool MatchesOccurrence(DocumentTextSegment output, ReplacementOccurrence occurrence,
        IReadOnlyList<ReplacementOccurrence> allOccurrences, string expectedText)
    {
        int position = occurrence.Candidate.StartIndex;
        foreach (var earlier in allOccurrences.Where(o => o.IsSelected &&
            SameLocation(o.Segment, occurrence.Segment) &&
            o.Candidate.StartIndex < occurrence.Candidate.StartIndex))
        {
            position += earlier.NewDateText.Length - earlier.Candidate.RawText.Length;
        }
        return position >= 0 && position <= output.LogicalText.Length - expectedText.Length &&
            string.Compare(output.LogicalText, position, expectedText, 0, expectedText.Length,
                StringComparison.Ordinal) == 0;
    }

    private static DocumentTextSegment? FindMatchingSegment(List<DocumentTextSegment> outputSegments, DocumentTextSegment originalSegment)
    {
        var matches = outputSegments.Where(s => SameLocation(s, originalSegment)).ToList();
        // Ambiguous locations must be reviewed instead of silently matching the first paragraph.
        return matches.Count == 1 ? matches[0] : null;
    }
    private static void CalculateSafetyScore(VerificationResult result)
    {
        if (!result.CanOpenDocument || !result.IsValidXmlStructure)
        {
            result.SafetyScore = 0;
            result.FinalStatus = FileScanStatus.Error;
            return;
        }

        if (result.UnexpectedModifications.Any(m => m.Contains("NGUY HIỂM")))
        {
            result.SafetyScore = 0;
            result.FinalStatus = FileScanStatus.Error;
            return;
        }

        int score = 100;

        // Trừ 30 điểm cho mỗi ngày cũ bị sót lại
        if (result.MissingReplacements.Count > 0)
        {
            score -= (result.MissingReplacements.Count * 30);
        }

        // Trừ 15 điểm cho mỗi cảnh báo sửa đổi ngoài ý muốn
        if (result.UnexpectedModifications.Count > 0)
        {
            score -= (result.UnexpectedModifications.Count * 15);
        }

        // Trừ 20 điểm nếu số lượng thay thế thực tế không khớp số lượng dự kiến
        if (result.ActualReplacementsDone < result.ExpectedReplacements)
        {
            score -= ((result.ExpectedReplacements - result.ActualReplacementsDone) * 20);
        }

        result.SafetyScore = Math.Max(0, Math.Min(score, 100));

        if (result.Issues.Count == 0 && result.SafetyScore >= 90)
        {
            result.FinalStatus = FileScanStatus.Pass;
        }
        else if (result.MissingReplacements.Count > 0 || result.UnexpectedModifications.Count > 0)
        {
            result.FinalStatus = FileScanStatus.Error;
        }
        else
        {
            result.FinalStatus = FileScanStatus.ReviewRequired;
        }
    }
}




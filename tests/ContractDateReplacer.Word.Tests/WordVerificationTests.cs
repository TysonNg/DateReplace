using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using ContractDateReplacer.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContractDateReplacer.Word.Tests;

public class WordVerificationTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly WordOpenXmlValidator _validator;
    private readonly WordDocumentScanner _scanner;
    private readonly DateDetector _detector;
    private readonly WordDocumentReplacementService _replacementService;
    private readonly WordDocumentVerificationService _verificationService;

    public WordVerificationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "WordVerificationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _validator = new WordOpenXmlValidator();
        _scanner = new WordDocumentScanner();
        _detector = new DateDetector();
        _replacementService = new WordDocumentReplacementService(_validator, NullLogger<WordDocumentReplacementService>.Instance);
        _verificationService = new WordDocumentVerificationService(_scanner, _detector, _validator, NullLogger<WordDocumentVerificationService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Theory]
    [InlineData(false, "05/10/2026")]
    [InlineData(true, "05/10/2026")]
    [InlineData(false, "5/10/2026")]
    [InlineData(true, "5/10/2026")]
    public async Task VerifyOutputAsync_RepeatedDate_ChecksExactOccurrence(bool replaceWrongOccurrence, string replacement)
    {
        string source = Path.Combine(_tempDirectory, "repeat_source.docx");
        string output = Path.Combine(_tempDirectory, "repeat_output.docx");
        const string original = "Ký ngày 02/09/2026; sinh ngày 02/09/2026.";
        using (var doc = WordprocessingDocument.Create(source, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text(original)))));
        }
        var segment = (await _scanner.ScanDocumentAsync(source))[0];
        var dates = new[]
        {
            new DateCandidate { RawText = "02/09/2026", Length = 10, StartIndex = original.IndexOf("02/09/2026") },
            new DateCandidate { RawText = "02/09/2026", Length = 10, StartIndex = original.LastIndexOf("02/09/2026") }
        };
        var selected = new ReplacementOccurrence { Segment = segment, Candidate = dates[0], NewDateText = replacement, IsSelected = true };
        dates[1].Classification.Type = DateClassificationType.BirthDate;
        var preserved = new ReplacementOccurrence { Segment = segment, Candidate = dates[1], IsSelected = false };
        string text = replaceWrongOccurrence
            ? $"Ký ngày 02/09/2026; sinh ngày {replacement}."
            : $"Ký ngày {replacement}; sinh ngày 02/09/2026.";
        using (var doc = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
        }
        var result = await _verificationService.VerifyOutputAsync(source, output, new[] { selected, preserved });
        result.IsPassed.Should().Be(!replaceWrongOccurrence);
        if (replaceWrongOccurrence)
            result.UnexpectedModifications.Should().NotBeEmpty();
        else
            result.UnexpectedOldDatesRemaining.Should().Be(0);
    }
    [Fact]
    public async Task Test_ReplaceInUserContract()
    {
        string path = @"C:\Users\immin\Downloads\Biên bản nghiệm thu.docx";
        if (File.Exists(path))
        {
            string outPath = Path.Combine(_tempDirectory, "Biên bản nghiệm thu_out.docx");
            var segments = await _scanner.ScanDocumentAsync(path, default);
            var occurrences = new List<ReplacementOccurrence>();
            foreach (var seg in segments)
            {
                var detected = _detector.DetectDates(seg);
                foreach (var cand in detected)
                {
                    if (cand.Classification.IsRecommendedForReplacement)
                    {
                        occurrences.Add(new ReplacementOccurrence
                        {
                            Candidate = cand,
                            Segment = seg,
                            IsSelected = true,
                            NewDateText = "05/09/2026",
                            NewDateTime = new DateTime(2026, 9, 5)
                        });
                        break;
                    }
                }
                if (occurrences.Count > 0) break;
            }

            if (occurrences.Count > 0)
            {
                bool success = await _replacementService.ReplaceDatesAsync(path, outPath, occurrences, true, default);
                success.Should().BeTrue();
            }
        }
    }

    [Fact]
    public async Task VerifyOutputAsync_WhenAllDatesReplacedAndSensitivePreserved_ReturnsPassWith100Score()
    {
        string srcPath = Path.Combine(_tempDirectory, "ValidContract_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "ValidContract_Dest.docx");

        using (var doc = WordprocessingDocument.Create(srcPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(
                    new Run(new Text("Hợp đồng lập ngày 02/09/2026 giữa Công ty A và Ông Nguyễn Văn B, sinh ngày: 01/01/1990."))
                )
            ));
            mainPart.Document.Save();
        }

        var segments = await _scanner.ScanDocumentAsync(srcPath);
        var seg = segments[0];

        int signDateStart = seg.LogicalText.IndexOf("02/09/2026");
        int birthDateStart = seg.LogicalText.IndexOf("01/01/1990");

        var occToReplace = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "02/09/2026",
                StartIndex = signDateStart,
                Length = 10,
                Classification = new ClassificationResult { Type = DateClassificationType.ContractSignDate, IsRecommendedForReplacement = true }
            },
            NewDateText = "05/10/2026",
            IsSelected = true
        };

        var occPreservedBirthDate = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "01/01/1990",
                StartIndex = birthDateStart,
                Length = 10,
                Classification = new ClassificationResult { Type = DateClassificationType.BirthDate, IsRecommendedForReplacement = false }
            },
            NewDateText = "01/01/1990",
            IsSelected = false
        };

        // Thực hiện thay thế
        bool replaceResult = await _replacementService.ReplaceDatesAsync(srcPath, destPath, new[] { occToReplace });
        replaceResult.Should().BeTrue();

        // Thực hiện Verify độc lập
        var verifyResult = await _verificationService.VerifyOutputAsync(srcPath, destPath, new[] { occToReplace, occPreservedBirthDate });

        verifyResult.IsPassed.Should().BeTrue();
        verifyResult.FinalStatus.Should().Be(FileScanStatus.Pass);
        verifyResult.SafetyScore.Should().Be(100);
        verifyResult.ExpectedReplacements.Should().Be(1);
        verifyResult.ActualReplacementsDone.Should().Be(1);
        verifyResult.UnexpectedOldDatesRemaining.Should().Be(0);
        verifyResult.IntentionallySkippedDatesRemaining.Should().Be(1);
        verifyResult.MissingReplacements.Should().BeEmpty();
        verifyResult.UnexpectedModifications.Should().BeEmpty();
        verifyResult.IsValidXmlStructure.Should().BeTrue();
        verifyResult.IntegrityStatus.Should().Be("Valid");
    }

    [Fact]
    public async Task VerifyOutputAsync_WhenOldDateStillPresent_FlagsErrorWithMissingReplacement()
    {
        string srcPath = Path.Combine(_tempDirectory, "OldDateRemaining_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "OldDateRemaining_Dest.docx");

        // Tạo file output GIẢ ĐỊNH chưa thay thế xong (vẫn còn ngày cũ 02/09/2026)
        using (var doc = WordprocessingDocument.Create(destPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Hợp đồng lập ngày 02/09/2026 tại Hà Nội.")))
            ));
            mainPart.Document.Save();
        }

        var seg = new DocumentTextSegment
        {
            StoryType = StoryType.MainDocument,
            ParagraphIndex = 1,
            SourceLocation = "Nội dung chính, Đoạn 1"
        };

        var occExpected = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "02/09/2026",
                StartIndex = 18,
                Length = 10,
                Classification = new ClassificationResult { Type = DateClassificationType.ContractSignDate }
            },
            NewDateText = "05/10/2026",
            IsSelected = true
        };

        var verifyResult = await _verificationService.VerifyOutputAsync(srcPath, destPath, new[] { occExpected });

        verifyResult.IsPassed.Should().BeFalse();
        verifyResult.FinalStatus.Should().Be(FileScanStatus.Error);
        verifyResult.UnexpectedOldDatesRemaining.Should().Be(1);
        verifyResult.MissingReplacements.Should().HaveCount(1);
        verifyResult.Issues.Should().Contain(i => i.Contains("THAY THẾ CHƯA TRIỆT ĐỂ"));
    }

    [Fact]
    public async Task VerifyOutputAsync_WhenSensitiveBirthDateModified_FlagsCriticalError()
    {
        string srcPath = Path.Combine(_tempDirectory, "SensitiveModified_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "SensitiveModified_Dest.docx");

        // Giả lập file đầu ra vô tình làm mất ngày sinh 01/01/1990 và bị đổi thành 01/01/2000
        using (var doc = WordprocessingDocument.Create(destPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Người đại diện sinh ngày: 01/01/2000 tại Hà Nội.")))
            ));
            mainPart.Document.Save();
        }

        var seg = new DocumentTextSegment
        {
            StoryType = StoryType.MainDocument,
            ParagraphIndex = 1,
            SourceLocation = "Nội dung chính, Đoạn 1"
        };

        var birthDateOcc = new ReplacementOccurrence
        {
            Segment = seg,
            Candidate = new DateCandidate
            {
                RawText = "01/01/1990",
                StartIndex = 26,
                Length = 10,
                Classification = new ClassificationResult { Type = DateClassificationType.BirthDate, IsRecommendedForReplacement = false }
            },
            NewDateText = "01/01/1990",
            IsSelected = false // Người dùng KHÔNG chọn thay thế
        };

        var verifyResult = await _verificationService.VerifyOutputAsync(srcPath, destPath, new[] { birthDateOcc });

        verifyResult.IsPassed.Should().BeFalse();
        verifyResult.FinalStatus.Should().Be(FileScanStatus.Error);
        verifyResult.SafetyScore.Should().Be(0);
        verifyResult.UnexpectedModifications.Should().Contain(m => m.Contains("NGUY HIỂM - VI PHẠM DỮ LIỆU NHÂN THÂN"));
    }

    [Fact]
    public async Task VerifyOutputAsync_WhenOutputXmlIsCorrupt_FlagsInvalidXmlStructureAndZeroScore()
    {
        string srcPath = Path.Combine(_tempDirectory, "Corrupt_Src.docx");
        string destPath = Path.Combine(_tempDirectory, "Corrupt_Dest.docx");

        // Tạo file đầu ra hỏng hoàn toàn (text rác không phải file zip DOCX)
        await File.WriteAllTextAsync(destPath, "THIS IS CORRUPT NOT A ZIP DOCX ARCHIVE");

        var verifyResult = await _verificationService.VerifyOutputAsync(srcPath, destPath, Array.Empty<ReplacementOccurrence>());

        verifyResult.IsPassed.Should().BeFalse();
        verifyResult.CanOpenDocument.Should().BeFalse();
        verifyResult.IsValidXmlStructure.Should().BeFalse();
        verifyResult.IntegrityStatus.Should().Be("Invalid");
        verifyResult.SafetyScore.Should().Be(0);
        verifyResult.FinalStatus.Should().Be(FileScanStatus.Error);
        verifyResult.Issues.Should().NotBeEmpty();
    }
    [Fact]
    public async Task Partial_dates_replace_across_runs_and_verify_preserved_full_date()
    {
        var source = Path.Combine(_tempDirectory, "partial.docx");
        var output = Path.Combine(_tempDirectory, "partial-out.docx");
        using (var doc = WordprocessingDocument.Create(source, WordprocessingDocumentType.Document))
            doc.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(
                new Run(new Text("From 1/")), new Run(new Text("02 through 29/2; full 01/02/2026.")))));
        var segment = (await _scanner.ScanDocumentAsync(source))[0];
        var dates = _detector.DetectDates(segment);
        Assert.Equal(3, dates.Count);
        var occurrences = new List<ReplacementOccurrence>();
        foreach (var date in dates)
            occurrences.Add(new ReplacementOccurrence {
                Segment = segment, Candidate = date, IsSelected = date.Year == null,
                NewDateTime = new DateTime(2028, 4, 3),
                NewDateText = ContractDateReplacer.Core.Helpers.DateFormattingHelper.FormatReplacementDate(date, new DateTime(2028, 4, 3))
            });
        Assert.True(await _replacementService.ReplaceDatesAsync(source, output, occurrences, true, default));
        Assert.Equal("From 3/04 through 03/4; full 01/02/2026.", (await _scanner.ScanDocumentAsync(output))[0].LogicalText);
        var result = await _verificationService.VerifyOutputAsync(source, output, occurrences);
        Assert.True(result.IsPassed);
        Assert.Equal(2, result.ActualReplacementsDone);
    }}




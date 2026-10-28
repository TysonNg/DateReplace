using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
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

public class SmartOcrScannedContractTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly IOcrService _ocrService;
    private readonly IWordImageExtractionService _imageExtractionService;

    public SmartOcrScannedContractTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "SmartOcrTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _ocrService = new SmartOcrService();
        _imageExtractionService = new WordImageExtractionService(_ocrService, NullLogger<WordImageExtractionService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task ExtractImagesAsync_WhenDocumentContainsImageParts_ExtractsAllImagesSuccessfully()
    {
        string docPath = Path.Combine(_tempDirectory, "EmbeddedImagesTest.docx");

        using (var doc = WordprocessingDocument.Create(docPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Tài liệu kèm phụ lục ảnh scan")))
            ));

            // Thêm một ImagePart dạng PNG vào MainDocumentPart
            var imagePart = mainPart.AddImagePart(ImagePartType.Png);
            byte[] dummyPng = new byte[128];
            dummyPng[0] = 0x89; dummyPng[1] = 0x50; dummyPng[2] = 0x4E; dummyPng[3] = 0x47; // PNG signature
            using (var stream = imagePart.GetStream())
            {
                await stream.WriteAsync(dummyPng);
            }

            mainPart.Document.Save();
        }

        var images = await _imageExtractionService.ExtractImagesAsync(docPath);

        images.Should().HaveCount(1);
        images[0].ContentType.Should().Be("image/png");
        images[0].SizeBytes.Should().Be(128);
        images[0].LocationDescription.Should().Contain("Nội dung chính");
    }

    [Fact]
    public void RecognizeText_WithConfidenceAndBoundingBoxes_ExtractsAccurateMetrics()
    {
        string ocrRaw = "CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM\r\n" +
                        "Độc lập - Tự do - Hạnh phúc\r\n" +
                        "Hà Nội, ngày 15 tháng 09 năm 2024\r\n" +
                        "BÊN A: CÔNG TY TNHH ĐẦU TƯ ABC";

        var result = _ocrService.ParseOcrText(ocrRaw, defaultConfidence: 0.96);

        result.Should().NotBeNull();
        result.Lines.Should().HaveCount(4);
        result.AverageConfidence.Should().Be(0.96);

        // Kiểm tra nhận dạng ngày tháng từ OCR
        result.DetectedDates.Should().HaveCount(1);
        var dateCandidate = result.DetectedDates[0];

        dateCandidate.RawText.Should().Be("ngày 15 tháng 09 năm 2024");
        dateCandidate.TargetDate.Should().Be(new DateTime(2024, 9, 15));
        dateCandidate.Confidence.Should().BeGreaterOrEqualTo(0.95);
        dateCandidate.LineIndex.Should().Be(2);

        // Kiểm tra bounding box có tọa độ hợp lệ
        dateCandidate.BoundingBox.Should().NotBeNull();
        dateCandidate.BoundingBox!.Width.Should().BeGreaterThan(0);
        dateCandidate.BoundingBox.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void VietnameseDateFormats_RecognizesStandardAndSlashFormatsAccurately()
    {
        string mixedOcrText = "Hợp đồng kinh tế ký ngày 02/09/2024.\r\n" +
                              "Thời hạn nghiệm thu đến ngày 18 tháng 04 năm 2025.\r\n" +
                              "Mã số hồ sơ tham chiếu theo văn bản lập ngày 2026-12-25.";

        var result = _ocrService.ParseOcrText(mixedOcrText);

        result.DetectedDates.Should().HaveCount(3);

        var d1 = result.DetectedDates.FirstOrDefault(d => d.RawText.Contains("02/09/2024"));
        d1.Should().NotBeNull();
        d1!.TargetDate.Should().Be(new DateTime(2024, 9, 2));

        var d2 = result.DetectedDates.FirstOrDefault(d => d.RawText.Contains("ngày 18 tháng 04 năm 2025"));
        d2.Should().NotBeNull();
        d2!.TargetDate.Should().Be(new DateTime(2025, 4, 18));

        var d3 = result.DetectedDates.FirstOrDefault(d => d.RawText.Contains("2026-12-25"));
        d3.Should().NotBeNull();
        d3!.TargetDate.Should().Be(new DateTime(2026, 12, 25));
    }

    [Fact]
    public async Task AnalyzeScannedContractAsync_WhenDocumentIsImageHeavyAndTextLight_ClassifiesAsScannedDocument()
    {
        string docPath = Path.Combine(_tempDirectory, "ScannedContractTest.docx");

        using (var doc = WordprocessingDocument.Create(docPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            // Đoạn text rất ngắn (< 50 ký tự) giả lập hợp đồng scan chỉ có tiêu đề
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Bản scan Hợp đồng.")))
            ));

            var imagePart = mainPart.AddImagePart(ImagePartType.Jpeg);
            byte[] dummyJpg = new byte[256];
            using (var stream = imagePart.GetStream())
            {
                await stream.WriteAsync(dummyJpg);
            }

            mainPart.Document.Save();
        }

        var analysis = await _imageExtractionService.AnalyzeScannedContractAsync(docPath);

        analysis.HasEmbeddedImages.Should().BeTrue();
        analysis.ImageCount.Should().Be(1);
        analysis.IsLikelyScannedDocument.Should().BeTrue();
        analysis.SafetyWarning.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnalyzeScannedContractAsync_WhenOcrFindsDatesInImage_GeneratesSafetyWarning()
    {
        string docPath = Path.Combine(_tempDirectory, "OcrDateWarningTest.docx");

        // Tạo file docx kèm ảnh chứa text ngày dạng byte nhúng
        using (var doc = WordprocessingDocument.Create(docPath, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Hợp đồng nguyên tắc.")))
            ));

            var imagePart = mainPart.AddImagePart(ImagePartType.Jpeg);
            // Giả lập ảnh chứa chuỗi metadata ngày tháng
            string embeddedText = "SCAN: HOP DONG THI CONG - ngay 12 thang 05 nam 2024 - DA KY";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(embeddedText);
            using (var stream = imagePart.GetStream())
            {
                await stream.WriteAsync(bytes);
            }

            mainPart.Document.Save();
        }

        var analysis = await _imageExtractionService.AnalyzeScannedContractAsync(docPath);

        analysis.AllDetectedDates.Should().NotBeEmpty();
        analysis.AllDetectedDates[0].TargetDate.Should().Be(new DateTime(2024, 5, 12));
        analysis.SafetyWarning.Should().Contain("ảnh quét/bitmap không thể chỉnh sửa trực tiếp dạng văn bản Word");
    }
}

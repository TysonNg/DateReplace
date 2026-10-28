using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContractDateReplacer.Word;

/// <summary>
/// Dịch vụ trích xuất toàn bộ hình ảnh trong tài liệu Word và kiểm tra OCR phát hiện hợp đồng scan.
/// </summary>
public class WordImageExtractionService : IWordImageExtractionService
{
    private readonly IOcrService _ocrService;
    private readonly ILogger<WordImageExtractionService> _logger;

    public WordImageExtractionService(
        IOcrService ocrService,
        ILogger<WordImageExtractionService>? logger = null)
    {
        _ocrService = ocrService;
        _logger = logger ?? NullLogger<WordImageExtractionService>.Instance;
    }

    public async Task<List<DocumentImageItem>> ExtractImagesAsync(string docxPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(docxPath))
        {
            throw new FileNotFoundException($"Không tìm thấy file Word: {docxPath}");
        }

        var imageItems = new List<DocumentImageItem>();

        await Task.Run(async () =>
        {
            using var doc = WordprocessingDocument.Open(docxPath, false);
            var mainPart = doc.MainDocumentPart;
            if (mainPart == null) return;

            int imageIndex = 0;

            // 1. Trích xuất ảnh trong Main Document Body
            foreach (var imgPart in mainPart.ImageParts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                imageIndex++;

                var item = await ProcessImagePartAsync(imgPart, $"Ảnh {imageIndex} (Nội dung chính)", cancellationToken);
                imageItems.Add(item);
            }

            // 2. Trích xuất ảnh trong Headers
            int headerImgIdx = 0;
            foreach (var headerPart in mainPart.HeaderParts)
            {
                foreach (var imgPart in headerPart.ImageParts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    headerImgIdx++;
                    var item = await ProcessImagePartAsync(imgPart, $"Ảnh Header {headerImgIdx}", cancellationToken);
                    imageItems.Add(item);
                }
            }

            // 3. Trích xuất ảnh trong Footers
            int footerImgIdx = 0;
            foreach (var footerPart in mainPart.FooterParts)
            {
                foreach (var imgPart in footerPart.ImageParts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    footerImgIdx++;
                    var item = await ProcessImagePartAsync(imgPart, $"Ảnh Footer {footerImgIdx}", cancellationToken);
                    imageItems.Add(item);
                }
            }
        }, cancellationToken);

        _logger.LogInformation("Đã trích xuất {Count} hình ảnh từ {File}", imageItems.Count, Path.GetFileName(docxPath));
        return imageItems;
    }

    public async Task<ScannedDocumentAnalysisResult> AnalyzeScannedContractAsync(string docxPath, CancellationToken cancellationToken = default)
    {
        var images = await ExtractImagesAsync(docxPath, cancellationToken);
        var result = new ScannedDocumentAnalysisResult
        {
            FilePath = docxPath,
            HasEmbeddedImages = images.Count > 0,
            ImageCount = images.Count,
            Images = images
        };

        // Thu thập tất cả các ngày tháng phát hiện trong tất cả các ảnh qua OCR
        foreach (var img in images)
        {
            if (img.OcrResult?.DetectedDates != null && img.OcrResult.DetectedDates.Count > 0)
            {
                result.AllDetectedDates.AddRange(img.OcrResult.DetectedDates);
            }
        }

        // Đánh giá xem tài liệu có khả năng là hợp đồng scan (chỉ toàn ảnh chụp hoặc rất ít chữ Word)
        await Task.Run(() =>
        {
            using var doc = WordprocessingDocument.Open(docxPath, false);
            string bodyText = doc.MainDocumentPart?.Document.InnerText ?? string.Empty;
            int textLength = bodyText.Trim().Length;

            // Nếu tài liệu có ảnh lớn và phần văn bản text rất ngắn (< 200 ký tự) thì khả năng cao là tài liệu scan
            if (images.Count > 0 && textLength < 200)
            {
                result.IsLikelyScannedDocument = true;
            }
        }, cancellationToken);

        // Tạo cảnh báo an toàn
        if (result.AllDetectedDates.Count > 0)
        {
            result.SafetyWarning = $"Tài liệu chứa {result.ImageCount} hình ảnh/bản scan hợp đồng với {result.AllDetectedDates.Count} ngày tháng được phát hiện qua OCR. Chú ý: Ngày trong ảnh quét/bitmap không thể chỉnh sửa trực tiếp dạng văn bản Word.";
        }
        else if (result.IsLikelyScannedDocument)
        {
            result.SafetyWarning = $"Tài liệu có đặc điểm của bản scan hợp đồng (nhiều hình ảnh nhưng rất ít văn bản chữ).";
        }
        else if (result.HasEmbeddedImages)
        {
            result.SafetyWarning = $"Tài liệu chứa {result.ImageCount} hình ảnh đính kèm.";
        }

        return result;
    }

    private async Task<DocumentImageItem> ProcessImagePartAsync(
        ImagePart imgPart,
        string location,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        using (var stream = imgPart.GetStream())
        using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms, cancellationToken);
            bytes = ms.ToArray();
        }

        var item = new DocumentImageItem
        {
            ImageId = Path.GetFileName(imgPart.Uri.ToString()),
            ContentType = imgPart.ContentType,
            PartUri = imgPart.Uri.ToString(),
            SizeBytes = bytes.Length,
            LocationDescription = location,
            ImageData = bytes
        };

        // Nhận diện OCR văn bản và ngày tháng trong ảnh
        try
        {
            var ocrResult = await _ocrService.RecognizeTextAsync(bytes, "vie", cancellationToken);
            item.OcrResult = ocrResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể xử lý OCR cho ảnh {Uri}", imgPart.Uri);
        }

        return item;
    }
}

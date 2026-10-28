using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trích xuất toàn bộ hình ảnh nhúng trong tài liệu Word và kiểm tra phân tích OCR hợp đồng scan.
/// </summary>
public interface IWordImageExtractionService
{
    /// <summary>
    /// Trích xuất danh sách tất cả các ảnh nhúng (ImagePart) từ tài liệu Word.
    /// </summary>
    Task<List<DocumentImageItem>> ExtractImagesAsync(string docxPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phân tích chuyên sâu tài liệu Word để kiểm tra xem có phải là bản scan và có chứa ngày tháng trong ảnh không.
    /// </summary>
    Task<ScannedDocumentAnalysisResult> AnalyzeScannedContractAsync(string docxPath, CancellationToken cancellationToken = default);
}

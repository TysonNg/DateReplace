using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ nhận dạng ký tự quang học (OCR) thông minh cho hình ảnh và bản scan hợp đồng.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Cho biết engine OCR có sẵn sàng hoạt động hay không.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Nhận dạng văn bản và tự động bóc tách ngày tháng từ luồng dữ liệu hình ảnh (Stream).
    /// </summary>
    Task<OcrResult> RecognizeTextAsync(Stream imageStream, string language = "vie", CancellationToken cancellationToken = default);

    /// <summary>
    /// Nhận dạng văn bản và tự động bóc tách ngày tháng từ mảng byte ảnh.
    /// </summary>
    Task<OcrResult> RecognizeTextAsync(byte[] imageBytes, string language = "vie", CancellationToken cancellationToken = default);

    /// <summary>
    /// Phân tích văn bản thô từ OCR/ảnh để trích xuất các vị trí ngày tháng kèm độ tin cậy và tọa độ nếu có.
    /// </summary>
    OcrResult ParseOcrText(string extractedRawText, double defaultConfidence = 0.95);
}

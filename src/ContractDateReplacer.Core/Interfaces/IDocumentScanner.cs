using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ đọc và trích xuất cấu trúc văn bản từ tài liệu Word.
/// Cho phép thay thế OpenXML SDK bằng Aspose.Words hoặc thư viện khác mà không làm đổi Business Logic.
/// </summary>
public interface IDocumentScanner
{
    /// <summary>
    /// Quét toàn bộ nội dung của file docx và trích xuất thành danh sách các DocumentTextSegment kèm mapping Run.
    /// </summary>
    Task<List<DocumentTextSegment>> ScanDocumentAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra định dạng và khả năng mở của file Word mà không làm hỏng tài liệu.
    /// </summary>
    Task<bool> CanReadAsync(string filePath, CancellationToken cancellationToken = default);
}

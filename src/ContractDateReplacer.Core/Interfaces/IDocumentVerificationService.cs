using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ mở và quét kiểm tra lại (Verify) tài liệu đầu ra sau khi thay thế.
/// Đảm bảo không bỏ sót và tài liệu không bị hỏng cấu trúc.
/// </summary>
public interface IDocumentVerificationService
{
    /// <summary>
    /// Mở và quét kiểm tra tài liệu sau khi thay thế, so sánh với các vị trí dự kiến thay.
    /// </summary>
    Task<VerificationResult> VerifyOutputAsync(
        string sourceFilePath,
        string outputFilePath,
        IReadOnlyList<ReplacementOccurrence> originalOccurrences,
        CancellationToken cancellationToken = default);
}

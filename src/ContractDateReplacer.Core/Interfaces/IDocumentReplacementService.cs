using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ thay thế ngày an toàn vào tài liệu Word, đảm bảo bảo toàn formatting.
/// </summary>
public interface IDocumentReplacementService
{
    /// <summary>
    /// Thực hiện thay thế các vị trí ngày đã chọn từ file nguồn và lưu ra file đích.
    /// File nguồn tuyệt đối không bị ghi đè.
    /// </summary>
    Task<bool> ReplaceDatesAsync(
        string sourceFilePath,
        string destinationFilePath,
        IEnumerable<ReplacementOccurrence> selectedOccurrences,
        bool preserveOriginalDateFormat = true,
        CancellationToken cancellationToken = default);
}

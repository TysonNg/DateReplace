using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Dịch vụ xuất báo cáo nghiệm thu và phân tích đợt thay thế ngày sang các định dạng chuẩn (Excel XLSX, CSV, JSON, PDF/HTML).
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Xuất báo cáo đợt xử lý theo định dạng mong muốn và lưu vào đường dẫn outputPath.
    /// </summary>
    Task<string> ExportReportAsync(ReplacementBatchReport report, ExportFormat format, string outputPath, CancellationToken cancellationToken = default);
}

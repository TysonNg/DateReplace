using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ phân loại ngữ cảnh ngày tháng dựa trên bộ quy tắc (Rule-based) hoặc mô hình.
/// </summary>
public interface IDateClassifier
{
    /// <summary>
    /// Phân loại ngữ cảnh của một ứng viên ngày dựa trên ngữ cảnh xung quanh và bộ quy tắc hiện hành.
    /// </summary>
    Task<ClassificationResult> ClassifyAsync(DateCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phân loại hàng loạt danh sách ứng viên ngày.
    /// </summary>
    Task ClassifyBatchAsync(IEnumerable<DateCandidate> candidates, CancellationToken cancellationToken = default, DocumentTextSegment? segment = null);
}


using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ xử lý song song đa luồng hiệu năng cao bằng Channels và tối ưu hóa bộ nhớ RAM.
/// </summary>
public interface IBatchProcessingService
{
    /// <summary>
    /// Xử lý song song một danh sách item bằng Channel/Worker Pool với điều tiết tải CPU và kiểm soát rò rỉ RAM.
    /// </summary>
    Task<BatchExecutionSummary<TResult>> ProcessBatchAsync<TItem, TResult>(
        IEnumerable<TItem> items,
        Func<TItem, CancellationToken, Task<TResult>> workerFunc,
        Func<TItem, string>? itemKeySelector = null,
        int maxDegreeOfParallelism = -1,
        IProgress<BatchProgressUpdate>? progress = null,
        bool autoManageMemory = true,
        CancellationToken cancellationToken = default);
}

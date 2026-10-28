using System;
using System.Collections.Generic;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Báo cáo tiến độ streaming theo thời gian thực của tác vụ xử lý hàng loạt đa luồng.
/// </summary>
public class BatchProgressUpdate
{
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SuccessItems { get; set; }
    public int FailedItems { get; set; }
    public double PercentCompleted => TotalItems > 0 ? (double)ProcessedItems / TotalItems * 100.0 : 0.0;
    public string CurrentFileName { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public double MemoryUsageMb { get; set; }
}

/// <summary>
/// Kết quả tổng kết hiệu năng sau khi hoàn tất xử lý hàng loạt.
/// </summary>
public class BatchExecutionSummary<TResult>
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<TResult> Results { get; set; } = new();
    public List<(string ItemKey, string ErrorMessage)> Errors { get; set; } = new();
    public double PeakMemoryUsageMb { get; set; }
    public double ItemsPerSecond => TotalDuration.TotalSeconds > 0 ? TotalCount / TotalDuration.TotalSeconds : 0;
}

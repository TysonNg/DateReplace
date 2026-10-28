using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContractDateReplacer.Infrastructure.Services;

/// <summary>
/// Bộ xử lý hàng loạt song song đa luồng hiệu năng cao ứng dụng kiến trúc Channel và tối ưu hóa bộ nhớ RAM.
/// </summary>
public class HighPerformanceBatchProcessor : IBatchProcessingService
{
    private readonly ISettingsService? _settingsService;
    private readonly ILogger<HighPerformanceBatchProcessor> _logger;

    public HighPerformanceBatchProcessor(
        ISettingsService? settingsService = null,
        ILogger<HighPerformanceBatchProcessor>? logger = null)
    {
        _settingsService = settingsService;
        _logger = logger ?? NullLogger<HighPerformanceBatchProcessor>.Instance;
    }

    public async Task<BatchExecutionSummary<TResult>> ProcessBatchAsync<TItem, TResult>(
        IEnumerable<TItem> items,
        Func<TItem, CancellationToken, Task<TResult>> workerFunc,
        Func<TItem, string>? itemKeySelector = null,
        int maxDegreeOfParallelism = -1,
        IProgress<BatchProgressUpdate>? progress = null,
        bool autoManageMemory = true,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        int totalItems = itemList.Count;

        if (maxDegreeOfParallelism <= 0)
        {
            maxDegreeOfParallelism = _settingsService?.CurrentSettings.MaxDegreeOfParallelism
                                     ?? Math.Clamp(Environment.ProcessorCount, 2, 16);
        }

        maxDegreeOfParallelism = Math.Max(1, Math.Min(maxDegreeOfParallelism, totalItems > 0 ? totalItems : 1));

        var summary = new BatchExecutionSummary<TResult>
        {
            TotalCount = totalItems
        };

        if (totalItems == 0)
        {
            return summary;
        }

        var results = new ConcurrentBag<TResult>();
        var errors = new ConcurrentBag<(string, string)>();

        int processedCounter = 0;
        int successCounter = 0;
        int failedCounter = 0;
        double peakMemoryMb = 0;

        var stopwatch = Stopwatch.StartNew();

        // Khởi tạo Channel có giới hạn đệm (Bounded Channel) để điều tiết áp lực hàng đợi
        int channelCapacity = Math.Max(maxDegreeOfParallelism * 2, 16);
        var channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

        // 1. Task Sản sinh (Producer): Đẩy các item vào channel
        var producerTask = Task.Run(async () =>
        {
            try
            {
                foreach (var item in itemList)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await channel.Writer.WriteAsync(item, cancellationToken);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // 2. Pool các Task Tiêu thụ (Consumers): Xử lý song song theo số luồng maxDegreeOfParallelism
        var consumerTasks = Enumerable.Range(0, maxDegreeOfParallelism).Select(async workerId =>
        {
            await Task.Yield();

            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string itemKey = itemKeySelector?.Invoke(item) ?? $"Item_{processedCounter}";

                    try
                    {
                        var res = await workerFunc(item, cancellationToken);
                        results.Add(res);
                        Interlocked.Increment(ref successCounter);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref failedCounter);
                        errors.Add((itemKey, ex.Message));
                        _logger.LogWarning(ex, "Lỗi xử lý item [{Key}] trong worker {WorkerId}", itemKey, workerId);
                    }
                    finally
                    {
                        int currentProcessed = Interlocked.Increment(ref processedCounter);

                        // Đo lường bộ nhớ RAM
                        double currentMem = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                        lock (summary)
                        {
                            if (currentMem > peakMemoryMb) peakMemoryMb = currentMem;
                        }

                        // Kiểm soát dọn dẹp bộ nhớ định kỳ để tránh tràn RAM
                        if (autoManageMemory && currentProcessed % 25 == 0)
                        {
                            TriggerGentleMemoryCompaction();
                        }

                        // Báo cáo tiến độ streaming
                        progress?.Report(new BatchProgressUpdate
                        {
                            TotalItems = totalItems,
                            ProcessedItems = currentProcessed,
                            SuccessItems = successCounter,
                            FailedItems = failedCounter,
                            CurrentFileName = itemKey,
                            StatusMessage = $"Đang xử lý {currentProcessed}/{totalItems} (Worker {workerId})...",
                            MemoryUsageMb = currentMem
                        });
                    }
                }
            }
        }).ToArray();

        // Chờ producer và tất cả consumers hoàn thành
        await Task.WhenAll(producerTask);
        await Task.WhenAll(consumerTasks);

        stopwatch.Stop();

        // Thu gom rác lần cuối giải phóng bộ nhớ heap sau khi hoàn tất toàn bộ batch
        if (autoManageMemory)
        {
            TriggerGentleMemoryCompaction();
        }

        summary.SuccessCount = successCounter;
        summary.FailureCount = failedCounter;
        summary.TotalDuration = stopwatch.Elapsed;
        summary.Results = results.ToList();
        summary.Errors = errors.ToList();
        summary.PeakMemoryUsageMb = Math.Round(peakMemoryMb, 1);

        _logger.LogInformation("Batch hoàn tất trong {Duration:F2}s. Thành công: {Success}/{Total}. Tốc độ: {Rate:F1} items/s. RAM đỉnh: {RAM} MB",
            summary.TotalDuration.TotalSeconds, summary.SuccessCount, summary.TotalCount, summary.ItemsPerSecond, summary.PeakMemoryUsageMb);

        return summary;
    }

    private static void TriggerGentleMemoryCompaction()
    {
        // Tối ưu hóa Large Object Heap và giải phóng Gen 2 không block UI dài
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(1, GCCollectionMode.Optimized, false);
    }
}

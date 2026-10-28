using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class BatchProcessingPerformanceTests
{
    private readonly IBatchProcessingService _batchProcessor;

    public BatchProcessingPerformanceTests()
    {
        _batchProcessor = new HighPerformanceBatchProcessor(null, NullLogger<HighPerformanceBatchProcessor>.Instance);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenGivenMultipleItems_ExecutesAllItemsConcurrentlyViaChannel()
    {
        var items = Enumerable.Range(1, 50).Select(i => $"Contract_{i}.docx").ToList();

        var summary = await _batchProcessor.ProcessBatchAsync<string, int>(
            items,
            async (item, ct) =>
            {
                await Task.Delay(5, ct); // Giả lập quét file
                return item.Length;
            },
            itemKeySelector: i => i,
            maxDegreeOfParallelism: 4);

        summary.TotalCount.Should().Be(50);
        summary.SuccessCount.Should().Be(50);
        summary.FailureCount.Should().Be(0);
        summary.Results.Should().HaveCount(50);
        summary.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ProcessBatchAsync_RespectsMaxDegreeOfParallelism()
    {
        const int maxConcurrency = 3;
        int currentActive = 0;
        int peakActive = 0;
        object lockObj = new();

        var items = Enumerable.Range(1, 30).ToList();

        var summary = await _batchProcessor.ProcessBatchAsync<int, int>(
            items,
            async (item, ct) =>
            {
                int active = Interlocked.Increment(ref currentActive);
                lock (lockObj)
                {
                    if (active > peakActive) peakActive = active;
                }

                await Task.Delay(20, ct);

                Interlocked.Decrement(ref currentActive);
                return item * 2;
            },
            maxDegreeOfParallelism: maxConcurrency);

        summary.SuccessCount.Should().Be(30);
        peakActive.Should().BeLessOrEqualTo(maxConcurrency);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenItemFails_IsolatesErrorAndCompletesRemainingItems()
    {
        var items = Enumerable.Range(1, 20).Select(i => $"Doc_{i}").ToList();

        var summary = await _batchProcessor.ProcessBatchAsync<string, string>(
            items,
            async (item, ct) =>
            {
                await Task.Delay(2, ct);
                if (item == "Doc_5" || item == "Doc_12")
                {
                    throw new InvalidOperationException($"Lỗi hỏng cấu trúc tại {item}");
                }
                return $"{item}_Processed";
            },
            itemKeySelector: i => i,
            maxDegreeOfParallelism: 4);

        summary.TotalCount.Should().Be(20);
        summary.SuccessCount.Should().Be(18);
        summary.FailureCount.Should().Be(2);

        summary.Errors.Should().HaveCount(2);
        summary.Errors.Should().Contain(e => e.ItemKey == "Doc_5" && e.ErrorMessage.Contains("Lỗi hỏng cấu trúc tại Doc_5"));
        summary.Errors.Should().Contain(e => e.ItemKey == "Doc_12" && e.ErrorMessage.Contains("Lỗi hỏng cấu trúc tại Doc_12"));
    }

    [Fact]
    public async Task ProcessBatchAsync_ReportsStreamingProgressAccurately()
    {
        var items = Enumerable.Range(1, 25).Select(i => $"File_{i}.docx").ToList();
        var progressUpdates = new List<BatchProgressUpdate>();

        var progress = new Progress<BatchProgressUpdate>(update =>
        {
            lock (progressUpdates)
            {
                progressUpdates.Add(new BatchProgressUpdate
                {
                    TotalItems = update.TotalItems,
                    ProcessedItems = update.ProcessedItems,
                    SuccessItems = update.SuccessItems,
                    MemoryUsageMb = update.MemoryUsageMb
                });
            }
        });

        var summary = await _batchProcessor.ProcessBatchAsync<string, bool>(
            items,
            async (item, ct) =>
            {
                await Task.Delay(5, ct);
                return true;
            },
            itemKeySelector: i => i,
            maxDegreeOfParallelism: 4,
            progress: progress);

        summary.SuccessCount.Should().Be(25);

        // Đợi Progress event bus hoàn tất dispatch
        await Task.Delay(50);

        lock (progressUpdates)
        {
            progressUpdates.Should().NotBeEmpty();
            var lastUpdate = progressUpdates.OrderBy(p => p.ProcessedItems).Last();
            lastUpdate.ProcessedItems.Should().Be(25);
            lastUpdate.TotalItems.Should().Be(25);
        }
    }

    [Fact]
    public async Task ProcessBatchAsync_TracksMemoryAndMetrics()
    {
        var items = Enumerable.Range(1, 15).Select(i => new byte[1000]).ToList();

        var summary = await _batchProcessor.ProcessBatchAsync<byte[], int>(
            items,
            async (bytes, ct) =>
            {
                await Task.Delay(2, ct);
                return bytes.Length;
            },
            maxDegreeOfParallelism: 2,
            autoManageMemory: true);

        summary.SuccessCount.Should().Be(15);
        summary.PeakMemoryUsageMb.Should().BeGreaterThan(0);
        summary.ItemsPerSecond.Should().BeGreaterThan(0);
    }
}

using System;
using System.Collections.Generic;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Các định dạng xuất báo cáo được hỗ trợ.
/// </summary>
public enum ExportFormat
{
    ExcelXlsx,
    Csv,
    Json,
    PdfHtml
}

/// <summary>
/// Chi tiết từng điểm ngày trong báo cáo xuất tổng hợp.
/// </summary>
public class BatchReportDateItem
{
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceLocation { get; set; } = string.Empty;
    public string OldDateText { get; set; } = string.Empty;
    public string NewDateText { get; set; } = string.Empty;
    public string ClassificationTypeName { get; set; } = string.Empty;
    public string Status { get; set; } = "OK";
    public int SafetyScore { get; set; } = 100;
}

/// <summary>
/// Chi tiết từng file trong đợt xử lý xuất báo cáo.
/// </summary>
public class BatchReportFileItem
{
    public string FileName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int SafetyScore { get; set; } = 100;
    public int ReplacedDatesCount { get; set; }
    public int PreservedDatesCount { get; set; }
    public string Status { get; set; } = "Success";
    public List<string> Issues { get; set; } = new();
    public List<BatchReportDateItem> Dates { get; set; } = new();
}

/// <summary>
/// Dữ liệu tổng hợp toàn diện một đợt thay thế ngày (Batch Report) sẵn sàng cho xuất đa định dạng.
/// </summary>
public class ReplacementBatchReport
{
    public string BatchId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedTime { get; set; } = DateTime.Now;
    public int TotalFiles { get; set; }
    public int SuccessFiles { get; set; }
    public int WarningFiles { get; set; }
    public int FailedFiles { get; set; }
    public int TotalDatesReplaced { get; set; }
    public int TotalDatesPreserved { get; set; }
    public int AverageSafetyScore { get; set; } = 100;
    public string IntegrityHash { get; set; } = string.Empty;
    public List<BatchReportFileItem> Files { get; set; } = new();
    public List<BatchReportDateItem> AllDates { get; set; } = new();
}

/// <summary>
/// Dữ liệu Payload gửi qua Webhook HTTP POST cho các hệ thống ERP / DMS / Quản lý hợp đồng.
/// </summary>
public class WebhookPayload
{
    public string BatchId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = "contract.date_replace.batch_completed";
    public int TotalFiles { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalDatesReplaced { get; set; }
    public string IntegrityHash { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
}

/// <summary>
/// Kết quả chuyển giao Webhook.
/// </summary>
public class WebhookDeliveryResult
{
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Signature { get; set; }
}

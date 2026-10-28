using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Infrastructure.Services;
using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class ExternalIntegrationExportTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly IExportService _exportService;
    private readonly IWebhookNotificationService _webhookService;

    public ExternalIntegrationExportTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "ExportIntegrationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _exportService = new ExportService(NullLogger<ExportService>.Instance);
        _webhookService = new WebhookNotificationService(null, NullLogger<WebhookNotificationService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    private ReplacementBatchReport CreateSampleBatchReport()
    {
        var report = new ReplacementBatchReport
        {
            BatchId = "BATCH_20260902_TEST",
            CreatedTime = new DateTime(2026, 9, 2, 15, 30, 0),
            TotalFiles = 2,
            SuccessFiles = 2,
            WarningFiles = 0,
            FailedFiles = 0,
            TotalDatesReplaced = 3,
            TotalDatesPreserved = 1,
            AverageSafetyScore = 98,
            IntegrityHash = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08"
        };

        var date1 = new BatchReportDateItem
        {
            SourceFileName = "HopDongKinhTe_01.docx",
            SourceLocation = "Nội dung chính, Đoạn 1",
            OldDateText = "15/04/2024",
            NewDateText = "15/04/2026",
            ClassificationTypeName = "ContractSignDate",
            Status = "[OK]",
            SafetyScore = 100
        };

        var date2 = new BatchReportDateItem
        {
            SourceFileName = "HopDongKinhTe_01.docx",
            SourceLocation = "Bảng 1, Dòng 2, Ô 1",
            OldDateText = "30/04/2024",
            NewDateText = "30/04/2026",
            ClassificationTypeName = "PaymentDate",
            Status = "[OK]",
            SafetyScore = 95
        };

        var date3 = new BatchReportDateItem
        {
            SourceFileName = "HopDongLaoDong_02.docx",
            SourceLocation = "Nội dung chính, Đoạn 5",
            OldDateText = "01/05/2024",
            NewDateText = "01/05/2026",
            ClassificationTypeName = "ContractEffectiveDate",
            Status = "[OK]",
            SafetyScore = 100
        };

        var date4 = new BatchReportDateItem
        {
            SourceFileName = "HopDongLaoDong_02.docx",
            SourceLocation = "Nội dung chính, Đoạn 8",
            OldDateText = "19/08/1990",
            NewDateText = "19/08/1990",
            ClassificationTypeName = "BirthDate",
            Status = "[GIỮ NGUYÊN]",
            SafetyScore = 100
        };

        report.AllDates.AddRange(new[] { date1, date2, date3, date4 });

        report.Files.Add(new BatchReportFileItem
        {
            FileName = "HopDongKinhTe_01.docx",
            OriginalPath = "D:\\Data\\HopDongKinhTe_01.docx",
            OutputPath = "D:\\Output\\HopDongKinhTe_01.docx",
            SafetyScore = 98,
            ReplacedDatesCount = 2,
            PreservedDatesCount = 0,
            Status = "Success",
            Dates = new List<BatchReportDateItem> { date1, date2 }
        });

        report.Files.Add(new BatchReportFileItem
        {
            FileName = "HopDongLaoDong_02.docx",
            OriginalPath = "D:\\Data\\HopDongLaoDong_02.docx",
            OutputPath = "D:\\Output\\HopDongLaoDong_02.docx",
            SafetyScore = 100,
            ReplacedDatesCount = 1,
            PreservedDatesCount = 1,
            Status = "Success",
            Dates = new List<BatchReportDateItem> { date3, date4 }
        });

        return report;
    }

    [Fact]
    public async Task ExportReportAsync_ExcelXlsx_GeneratesValidSpreadsheetWithSheetsAndContent()
    {
        var report = CreateSampleBatchReport();
        string xlsxPath = Path.Combine(_tempDirectory, "BaoCaoTest.xlsx");

        string resultPath = await _exportService.ExportReportAsync(report, ExportFormat.ExcelXlsx, xlsxPath);

        resultPath.Should().Be(xlsxPath);
        File.Exists(xlsxPath).Should().BeTrue();
        new FileInfo(xlsxPath).Length.Should().BeGreaterThan(1000);

        // Đọc lại file Excel để kiểm chứng cấu trúc chuẩn SpreadsheetML
        using var spreadsheet = SpreadsheetDocument.Open(xlsxPath, false);
        var workbookPart = spreadsheet.WorkbookPart;
        workbookPart.Should().NotBeNull();

        var sheets = workbookPart!.Workbook.Sheets?.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ToList();
        sheets.Should().NotBeNull();
        sheets!.Should().HaveCount(3);
        sheets![0].Name!.Value.Should().Be("Tổng Quan Đợt Thay Thế");
        sheets![1].Name!.Value.Should().Be("Chi Tiết Vị Trí Ngày");
        sheets![2].Name!.Value.Should().Be("Danh Sách File Hợp Đồng");
    }

    [Fact]
    public async Task ExportReportAsync_JsonAndCsv_OutputsCompleteAuditData()
    {
        var report = CreateSampleBatchReport();
        string jsonPath = Path.Combine(_tempDirectory, "BaoCaoTest.json");
        string csvPath = Path.Combine(_tempDirectory, "BaoCaoTest.csv");

        // 1. Kiểm thử xuất JSON
        await _exportService.ExportReportAsync(report, ExportFormat.Json, jsonPath);
        File.Exists(jsonPath).Should().BeTrue();
        string jsonContent = await File.ReadAllTextAsync(jsonPath);
        jsonContent.Should().Contain(report.BatchId);
        jsonContent.Should().Contain(report.IntegrityHash);
        jsonContent.Should().Contain("HopDongKinhTe_01.docx");

        // Khôi phục lại từ JSON để xác nhận tính toàn vẹn
        var deserialized = JsonSerializer.Deserialize<ReplacementBatchReport>(jsonContent);
        deserialized.Should().NotBeNull();
        deserialized!.BatchId.Should().Be(report.BatchId);
        deserialized.AllDates.Should().HaveCount(4);

        // 2. Kiểm thử xuất CSV
        await _exportService.ExportReportAsync(report, ExportFormat.Csv, csvPath);
        File.Exists(csvPath).Should().BeTrue();
        string csvContent = await File.ReadAllTextAsync(csvPath);
        csvContent.Should().Contain("STT,Tên File,Vị Trí,Ngày Gốc,Ngày Mới,Phân Loại,Trạng Thái,Điểm An Toàn");
        csvContent.Should().Contain("15/04/2024");
        csvContent.Should().Contain("15/04/2026");
        csvContent.Should().Contain("HopDongLaoDong_02.docx");
    }

    [Fact]
    public void GenerateHmacSignature_ProducesStandardSha256HexHash()
    {
        string payloadJson = "{\"batchId\":\"B123\",\"totalFiles\":5,\"success\":true}";
        string secretKey = "MyCorporateSuperSecretKey_2026";

        string signature1 = _webhookService.GenerateHmacSignature(payloadJson, secretKey);
        string signature2 = _webhookService.GenerateHmacSignature(payloadJson, secretKey);

        signature1.Should().NotBeNullOrEmpty();
        signature1.Length.Should().Be(64); // 256 bits = 64 hex characters
        signature1.Should().Be(signature2); // Hash xác định (deterministic)

        // Đổi key sẽ tạo hash khác
        string differentKeySig = _webhookService.GenerateHmacSignature(payloadJson, "DifferentSecretKey");
        differentKeySig.Should().NotBe(signature1);
    }

    [Fact]
    public async Task SendBatchCompletedNotificationAsync_WithMockHttp_DeliversSuccessfullyAndHandlesRetry()
    {
        // Giả lập HttpMessageHandler: lần đầu ném exception mạng, lần 2 trả về 200 OK thành công
        var mockHandler = new MockHttpMessageHandler(HttpStatusCode.OK, failFirstThenSucceed: true);
        var httpClient = new HttpClient(mockHandler);
        var webhookService = new WebhookNotificationService(httpClient, NullLogger<WebhookNotificationService>.Instance);

        var payload = new WebhookPayload
        {
            BatchId = "BATCH_HOOK_001",
            TotalFiles = 10,
            SuccessCount = 10,
            FailedCount = 0,
            TotalDatesReplaced = 25,
            IntegrityHash = "HASH_ABC_XYZ",
            Summary = "Đã hoàn thành thay thế ngày cho 10 file hợp đồng."
        };

        var result = await webhookService.SendBatchCompletedNotificationAsync(
            payload,
            "https://erp.enterprise.com/api/v1/contract-date-webhook",
            "SecretKey_12345");

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.RetryCount.Should().Be(1); // Thử lại 1 lần thành công
        result.Signature.Should().NotBeNullOrEmpty();
        mockHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ExportReportAsync_PdfHtml_GeneratesExecutiveReportWithSealAndStyling()
    {
        var report = CreateSampleBatchReport();
        string htmlPath = Path.Combine(_tempDirectory, "BienBanNghiemThu.html");

        string resultPath = await _exportService.ExportReportAsync(report, ExportFormat.PdfHtml, htmlPath);

        File.Exists(htmlPath).Should().BeTrue();
        string content = await File.ReadAllTextAsync(htmlPath);

        content.Should().Contain("BIÊN BẢN NGHIỆM THU THAY THẾ NGÀY HỢP ĐỒNG");
        content.Should().Contain(report.BatchId);
        content.Should().Contain(report.IntegrityHash);
        content.Should().Contain("CHỨNG THỰC TOÀN VẸN TỰ ĐỘNG");
        content.Should().Contain("HopDongKinhTe_01.docx");
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        private readonly HttpStatusCode _statusCode;
        private readonly bool _failFirstThenSucceed;

        public MockHttpMessageHandler(HttpStatusCode statusCode, bool failFirstThenSucceed = false)
        {
            _statusCode = statusCode;
            _failFirstThenSucceed = failFirstThenSucceed;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_failFirstThenSucceed && CallCount == 1)
            {
                throw new HttpRequestException("Lỗi kết nối mạng tạm thời (mô phỏng)");
            }

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{\"received\":true}", Encoding.UTF8, "application/json")
            });
        }
    }
}

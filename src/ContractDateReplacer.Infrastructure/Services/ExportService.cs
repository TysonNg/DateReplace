using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContractDateReplacer.Infrastructure.Services;

/// <summary>
/// Dịch vụ xuất báo cáo nghiệm thu và phân tích đợt thay thế ngày sang các định dạng chuẩn doanh nghiệp.
/// </summary>
public class ExportService : IExportService
{
    private readonly ILogger<ExportService> _logger;

    public ExportService(ILogger<ExportService>? logger = null)
    {
        _logger = logger ?? NullLogger<ExportService>.Instance;
    }

    public async Task<string> ExportReportAsync(
        ReplacementBatchReport report,
        ExportFormat format,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(dir);

        switch (format)
        {
            case ExportFormat.ExcelXlsx:
                await Task.Run(() => ExportToExcel(report, outputPath), cancellationToken);
                break;

            case ExportFormat.Csv:
                await ExportToCsvAsync(report, outputPath, cancellationToken);
                break;

            case ExportFormat.Json:
                await ExportToJsonAsync(report, outputPath, cancellationToken);
                break;

            case ExportFormat.PdfHtml:
                await ExportToHtmlReportAsync(report, outputPath, cancellationToken);
                break;

            default:
                throw new NotSupportedException($"Định dạng xuất {format} chưa được hỗ trợ.");
        }

        _logger.LogInformation("Xuất thành công báo cáo đợt {BatchId} định dạng {Format} ra {Path}",
            report.BatchId, format, outputPath);

        return outputPath;
    }

    private void ExportToExcel(ReplacementBatchReport report, string filePath)
    {
        if (File.Exists(filePath)) File.Delete(filePath);

        using var spreadsheet = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
        var workbookPart = spreadsheet.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var sheets = spreadsheet.WorkbookPart!.Workbook.AppendChild(new Sheets());

        // 1. Sheet "Tổng Quan"
        var summaryWsPart = workbookPart.AddNewPart<WorksheetPart>();
        var summaryData = new SheetData();
        summaryWsPart.Worksheet = new Worksheet(summaryData);
        sheets.Append(new Sheet
        {
            Id = spreadsheet.WorkbookPart.GetIdOfPart(summaryWsPart),
            SheetId = 1,
            Name = "Tổng Quan Đợt Thay Thế"
        });

        AddRow(summaryData, "BÁO CÁO NGHIỆM THU THAY THẾ NGÀY HỢP ĐỒNG (ANTIGRAVITY DATE REPLACE)");
        AddRow(summaryData, "Mã Đợt (Batch ID):", report.BatchId);
        AddRow(summaryData, "Thời Gian Khởi Tạo:", report.CreatedTime.ToString("dd/MM/yyyy HH:mm:ss"));
        AddRow(summaryData, "Mã Băm Toàn Vẹn (Integrity Hash):", report.IntegrityHash);
        AddRow(summaryData, string.Empty);
        AddRow(summaryData, "Tổng Số File Xử Lý:", report.TotalFiles.ToString());
        AddRow(summaryData, "File Thành Công Tuyệt Đối:", report.SuccessFiles.ToString());
        AddRow(summaryData, "File Cần Kiểm Tra (Warning):", report.WarningFiles.ToString());
        AddRow(summaryData, "File Thất Bại (Error):", report.FailedFiles.ToString());
        AddRow(summaryData, "Tổng Số Ngày Đã Thay:", report.TotalDatesReplaced.ToString());
        AddRow(summaryData, "Tổng Số Ngày Giữ Nguyên:", report.TotalDatesPreserved.ToString());
        AddRow(summaryData, "Điểm An Toàn Trung Bình:", $"{report.AverageSafetyScore}%");

        // 2. Sheet "Chi Tiết Ngày"
        var detailsWsPart = workbookPart.AddNewPart<WorksheetPart>();
        var detailsData = new SheetData();
        detailsWsPart.Worksheet = new Worksheet(detailsData);
        sheets.Append(new Sheet
        {
            Id = spreadsheet.WorkbookPart.GetIdOfPart(detailsWsPart),
            SheetId = 2,
            Name = "Chi Tiết Vị Trí Ngày"
        });

        AddRow(detailsData, "STT", "Tên File", "Vị Trí Trong Tài Liệu", "Ngày Gốc (Cũ)", "Ngày Mới Thay", "Phân Loại Điều Khoản", "Trạng Thái", "Điểm An Toàn");

        int stt = 0;
        foreach (var d in report.AllDates)
        {
            stt++;
            AddRow(detailsData,
                stt.ToString(),
                d.SourceFileName,
                d.SourceLocation,
                d.OldDateText,
                d.NewDateText,
                d.ClassificationTypeName,
                d.Status,
                $"{d.SafetyScore}%");
        }

        // 3. Sheet "Danh Sách File"
        var filesWsPart = workbookPart.AddNewPart<WorksheetPart>();
        var filesData = new SheetData();
        filesWsPart.Worksheet = new Worksheet(filesData);
        sheets.Append(new Sheet
        {
            Id = spreadsheet.WorkbookPart.GetIdOfPart(filesWsPart),
            SheetId = 3,
            Name = "Danh Sách File Hợp Đồng"
        });

        AddRow(filesData, "STT", "Tên File", "Đường Dẫn Đích", "Điểm An Toàn", "Số Ngày Thay", "Số Ngày Giữ Nguyên", "Trạng Thái", "Ghi Chú");

        int fStt = 0;
        foreach (var f in report.Files)
        {
            fStt++;
            string issues = string.Join("; ", f.Issues);
            AddRow(filesData,
                fStt.ToString(),
                f.FileName,
                f.OutputPath,
                $"{f.SafetyScore}%",
                f.ReplacedDatesCount.ToString(),
                f.PreservedDatesCount.ToString(),
                f.Status,
                issues);
        }

        workbookPart.Workbook.Save();
    }

    private static void AddRow(SheetData sheetData, params string[] values)
    {
        var row = new Row();
        foreach (var val in values)
        {
            row.Append(new Cell
            {
                DataType = CellValues.String,
                CellValue = new CellValue(val ?? string.Empty)
            });
        }
        sheetData.Append(row);
    }

    private async Task ExportToCsvAsync(ReplacementBatchReport report, string filePath, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        // UTF-8 BOM
        sb.AppendLine("STT,Tên File,Vị Trí,Ngày Gốc,Ngày Mới,Phân Loại,Trạng Thái,Điểm An Toàn");

        int stt = 0;
        foreach (var d in report.AllDates)
        {
            stt++;
            sb.AppendLine($"{stt},\"{EscapeCsv(d.SourceFileName)}\",\"{EscapeCsv(d.SourceLocation)}\",\"{EscapeCsv(d.OldDateText)}\",\"{EscapeCsv(d.NewDateText)}\",\"{EscapeCsv(d.ClassificationTypeName)}\",\"{EscapeCsv(d.Status)}\",{d.SafetyScore}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    private async Task ExportToJsonAsync(ReplacementBatchReport report, string filePath, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string json = JsonSerializer.Serialize(report, options);
        await File.WriteAllTextAsync(filePath, json, new UTF8Encoding(true), cancellationToken);
    }

    private async Task ExportToHtmlReportAsync(ReplacementBatchReport report, string filePath, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"vi\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine($"<title>Báo Cáo Thay Thế Ngày - Đợt {report.BatchId}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 30px; color: #1e293b; background: #f8fafc; }");
        sb.AppendLine(".card { background: #ffffff; border-radius: 8px; padding: 24px; box-shadow: 0 1px 3px rgba(0,0,0,0.1); margin-bottom: 24px; }");
        sb.AppendLine(".header { border-bottom: 2px solid #2563eb; padding-bottom: 12px; margin-bottom: 20px; }");
        sb.AppendLine(".metrics { display: flex; gap: 16px; margin: 20px 0; }");
        sb.AppendLine(".metric-box { flex: 1; padding: 16px; border-radius: 6px; background: #f1f5f9; text-align: center; }");
        sb.AppendLine(".metric-val { font-size: 24px; font-weight: bold; color: #2563eb; }");
        sb.AppendLine(".metric-label { font-size: 13px; color: #64748b; margin-top: 4px; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 16px; font-size: 14px; }");
        sb.AppendLine("th, td { border: 1px solid #e2e8f0; padding: 10px 14px; text-align: left; }");
        sb.AppendLine("th { background: #f8fafc; font-weight: 600; }");
        sb.AppendLine(".badge-ok { color: #16a34a; font-weight: bold; }");
        sb.AppendLine(".badge-preserved { color: #64748b; }");
        sb.AppendLine(".seal-box { border: 2px dashed #94a3b8; padding: 16px; border-radius: 8px; margin-top: 30px; text-align: center; color: #475569; }");
        sb.AppendLine("@media print { body { background: #fff; margin: 0; } .card { box-shadow: none; border: none; padding: 0; } }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine("<div class=\"card\">");
        sb.AppendLine("  <div class=\"header\">");
        sb.AppendLine("    <h1>BIÊN BẢN NGHIỆM THU THAY THẾ NGÀY HỢP ĐỒNG</h1>");
        sb.AppendLine($"    <p>Hệ thống: <strong>Antigravity ContractDateReplacer</strong> | Mã Đợt: <code>{report.BatchId}</code></p>");
        sb.AppendLine($"    <p>Thời gian: <strong>{report.CreatedTime:dd/MM/yyyy HH:mm:ss}</strong> | Hash: <code>{report.IntegrityHash}</code></p>");
        sb.AppendLine("  </div>");

        sb.AppendLine("  <div class=\"metrics\">");
        sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-val\">{report.TotalFiles}</div><div class=\"metric-label\">TỔNG FILE</div></div>");
        sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-val\" style=\"color:#16a34a;\">{report.SuccessFiles}</div><div class=\"metric-label\">THÀNH CÔNG</div></div>");
        sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-val\" style=\"color:#ea580c;\">{report.WarningFiles}</div><div class=\"metric-label\">CẢNH BÁO</div></div>");
        sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-val\" style=\"color:#dc2626;\">{report.FailedFiles}</div><div class=\"metric-label\">THẤT BẠI</div></div>");
        sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-val\">{report.TotalDatesReplaced}</div><div class=\"metric-label\">ĐÃ THAY THẾ</div></div>");
        sb.AppendLine($"    <div class=\"metric-box\"><div class=\"metric-val\">{report.AverageSafetyScore}%</div><div class=\"metric-label\">ĐIỂM AN TOÀN</div></div>");
        sb.AppendLine("  </div>");

        sb.AppendLine("  <h2>Chi tiết các vị trí ngày đã can thiệp</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead><tr><th>STT</th><th>Tên File</th><th>Vị Trí</th><th>Ngày Cũ</th><th>Ngày Mới</th><th>Phân Loại</th><th>Kết Quả</th></tr></thead>");
        sb.AppendLine("    <tbody>");

        int idx = 0;
        foreach (var d in report.AllDates)
        {
            idx++;
            string badgeClass = d.Status.Contains("OK") ? "badge-ok" : "badge-preserved";
            sb.AppendLine($"    <tr><td>{idx}</td><td>{d.SourceFileName}</td><td>{d.SourceLocation}</td><td>{d.OldDateText}</td><td><strong>{d.NewDateText}</strong></td><td>{d.ClassificationTypeName}</td><td class=\"{badgeClass}\">{d.Status}</td></tr>");
        }

        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        sb.AppendLine("  <div class=\"seal-box\">");
        sb.AppendLine("    <p><strong>CHỨNG THỰC TOÀN VẸN TỰ ĐỘNG (INTEGRITY CERTIFICATE)</strong></p>");
        sb.AppendLine($"    <p>Toàn bộ {report.TotalDatesReplaced} điểm thay thế đã được kiểm chứng tính toàn vẹn OpenXML và lưu vết kiểm toán.</p>");
        sb.AppendLine($"    <p>Mã băm SHA-256: <code>{report.IntegrityHash}</code></p>");
        sb.AppendLine("  </div>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(true), cancellationToken);
    }

    private static string EscapeCsv(string text)
    {
        return text?.Replace("\"", "\"\"") ?? string.Empty;
    }
}

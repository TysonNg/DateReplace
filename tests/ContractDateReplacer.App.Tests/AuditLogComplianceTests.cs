using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Persistence;
using ContractDateReplacer.Persistence.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class AuditLogComplianceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _dbPath;
    private readonly Mock<IAppDataService> _mockAppData;
    private readonly DatabaseInitializer _initializer;
    private readonly AuditLogService _auditService;

    public AuditLogComplianceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "AuditComplianceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _dbPath = Path.Combine(_tempDirectory, "audit_test.db");

        _mockAppData = new Mock<IAppDataService>();
        _mockAppData.Setup(a => a.RootDirectory).Returns(_tempDirectory);
        _mockAppData.Setup(a => a.DatabasePath).Returns(_dbPath);

        _initializer = new DatabaseInitializer(_mockAppData.Object);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _auditService = new AuditLogService(_mockAppData.Object, NullLogger<AuditLogService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task LogActionAsync_WhenCalled_SavesLogAndGeneratesValidIntegrityHash()
    {
        var entry = await _auditService.LogActionAsync(
            AuditActionType.BatchExecution,
            "{\"filesCount\": 5, \"datesChanged\": 12}",
            AuditSeverity.Info,
            @"C:\Contracts\HD_01.docx",
            "BATCH_20260903_01");

        entry.Should().NotBeNull();
        entry.LogId.Should().NotBeNullOrWhiteSpace();
        entry.IntegrityHash.Should().NotBeNullOrWhiteSpace();
        entry.VerifyIntegrity().Should().BeTrue();

        var queried = await _auditService.QueryLogsAsync();
        queried.Should().HaveCount(1);
        queried[0].LogId.Should().Be(entry.LogId);
        queried[0].IntegrityHash.Should().Be(entry.IntegrityHash);
        queried[0].TargetFile.Should().Be(@"C:\Contracts\HD_01.docx");
    }

    [Fact]
    public async Task VerifyLogIntegrityAsync_WhenLogsAreUntampered_Reports100PercentValid()
    {
        await _auditService.LogActionAsync(AuditActionType.BatchExecution, "Thao tác 1", AuditSeverity.Info);
        await _auditService.LogActionAsync(AuditActionType.RuleModified, "Thao tác 2", AuditSeverity.Warning);
        await _auditService.LogActionAsync(AuditActionType.PresetModified, "Thao tác 3", AuditSeverity.Info);

        var report = await _auditService.VerifyLogIntegrityAsync();

        report.IsTamperFree.Should().BeTrue();
        report.TotalLogsScanned.Should().Be(3);
        report.ValidLogsCount.Should().Be(3);
        report.TamperedLogsCount.Should().Be(0);
        report.TamperedLogIds.Should().BeEmpty();
        report.Message.Should().Contain("100%");
    }

    [Fact]
    public async Task VerifyLogIntegrityAsync_WhenLogIsTampered_DetectsViolationAndReportsTampered()
    {
        var entry = await _auditService.LogActionAsync(
            AuditActionType.FileRollback,
            "{\"status\": \"Original_Data\"}",
            AuditSeverity.Info,
            "TargetFile.docx");

        // Giả lập can thiệp sửa đổi trái phép trực tiếp vào SQLite
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        string tamperSql = "UPDATE ComplianceLogs SET DetailsJson = '{\"status\": \"TAMPERED_HACKED\"}' WHERE LogId = @Id;";
        await using var cmd = new SqliteCommand(tamperSql, conn);
        cmd.Parameters.AddWithValue("@Id", entry.LogId);
        await cmd.ExecuteNonQueryAsync();

        // Chạy kiểm tra tính toàn vẹn
        var report = await _auditService.VerifyLogIntegrityAsync();

        report.IsTamperFree.Should().BeFalse();
        report.TamperedLogsCount.Should().Be(1);
        report.TamperedLogIds.Should().Contain(entry.LogId);
        report.Message.Should().Contain("CẢNH BÁO VI PHẠM");
    }

    [Fact]
    public async Task QueryLogsAsync_WhenFilteredByActionTypeAndDate_ReturnsMatchingLogsOnly()
    {
        await _auditService.LogActionAsync(AuditActionType.BatchExecution, "Đợt chạy", AuditSeverity.Info);
        await _auditService.LogActionAsync(AuditActionType.RuleModified, "Sửa luật", AuditSeverity.Warning);
        await _auditService.LogActionAsync(AuditActionType.ConfigChanged, "Cấu hình", AuditSeverity.Info);

        // Lọc theo ActionType = RuleModified
        var filteredByAction = await _auditService.QueryLogsAsync(new AuditLogFilter
        {
            ActionType = AuditActionType.RuleModified
        });

        filteredByAction.Should().HaveCount(1);
        filteredByAction[0].ActionType.Should().Be(AuditActionType.RuleModified);

        // Lọc theo Severity = Warning
        var filteredBySev = await _auditService.QueryLogsAsync(new AuditLogFilter
        {
            Severity = AuditSeverity.Warning
        });

        filteredBySev.Should().HaveCount(1);
        filteredBySev[0].Severity.Should().Be(AuditSeverity.Warning);
    }

    [Fact]
    public async Task ExportLogsCsvAsync_ExportsUtf8WithBomAndValidFormat()
    {
        await _auditService.LogActionAsync(
            AuditActionType.BatchExecution,
            "Chi tiết: Thay đổi ngày hợp đồng sang 02/09/2026 thành công",
            AuditSeverity.Info,
            @"C:\Hợp Đồng\HopDongDichVu.docx",
            "BATCH_EXPORT_01");

        string exportFile = Path.Combine(_tempDirectory, "AuditLogs_Test.csv");

        await _auditService.ExportLogsCsvAsync(exportFile);

        File.Exists(exportFile).Should().BeTrue();

        byte[] bytes = await File.ReadAllBytesAsync(exportFile);
        // Kiểm tra UTF-8 BOM (0xEF, 0xBB, 0xBF)
        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        string content = await File.ReadAllTextAsync(exportFile, Encoding.UTF8);
        content.Should().Contain("Mã Log");
        content.Should().Contain("Chi tiết: Thay đổi ngày hợp đồng sang 02/09/2026 thành công");
        content.Should().Contain("HopDongDichVu.docx");
    }
}

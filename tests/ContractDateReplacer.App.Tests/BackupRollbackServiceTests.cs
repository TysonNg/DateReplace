using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Persistence;
using ContractDateReplacer.Persistence.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class BackupRollbackServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _dbPath;
    private readonly Mock<IAppDataService> _mockAppData;
    private readonly DatabaseInitializer _initializer;
    private readonly BackupRollbackService _service;

    public BackupRollbackServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BackupRollbackTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _dbPath = Path.Combine(_tempDirectory, "test.db");

        _mockAppData = new Mock<IAppDataService>();
        _mockAppData.Setup(a => a.RootDirectory).Returns(_tempDirectory);
        _mockAppData.Setup(a => a.DatabasePath).Returns(_dbPath);

        _initializer = new DatabaseInitializer(_mockAppData.Object);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _service = new BackupRollbackService(_mockAppData.Object, NullLogger<BackupRollbackService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task CreateBackupAsync_WhenCalled_CreatesBackupFileAndMatchesChecksum()
    {
        string origPath = Path.Combine(_tempDirectory, "HopDong_Goc.docx");
        await File.WriteAllTextAsync(origPath, "Nội dung hợp đồng gốc nguyên bản 100%");

        string originalHash = BackupRollbackService.ComputeSha256(origPath);

        var record = await _service.CreateBackupAsync("BATCH_20260903", origPath, null, 2);

        record.Should().NotBeNull();
        record.ChecksumSHA256.Should().Be(originalHash);
        record.Status.Should().Be("Available");
        File.Exists(record.BackupPath).Should().BeTrue();

        string backupFileHash = BackupRollbackService.ComputeSha256(record.BackupPath);
        backupFileHash.Should().Be(originalHash);

        var batchRecords = await _service.GetBackupsByBatchIdAsync("BATCH_20260903");
        batchRecords.Should().HaveCount(1);
        batchRecords[0].BackupId.Should().Be(record.BackupId);
    }

    [Fact]
    public async Task RestoreFileAsync_WhenCalled_RestoresOriginalContent100Percent()
    {
        string origPath = Path.Combine(_tempDirectory, "HopDong_ChuanBiSua.docx");
        await File.WriteAllTextAsync(origPath, "Văn bản gốc cần bảo vệ");

        var record = await _service.CreateBackupAsync("BATCH_RESTORE", origPath, null, 1);

        // Giả lập file gốc bị sửa đổi hoặc ghi đè
        await File.WriteAllTextAsync(origPath, "VĂN BẢN ĐÃ BỊ SỬA ĐỔI HOÀN TOÀN");
        string modifiedHash = BackupRollbackService.ComputeSha256(origPath);
        modifiedHash.Should().NotBe(record.ChecksumSHA256);

        // Khôi phục lại từ bản sao lưu
        var restoreResult = await _service.RestoreFileAsync(record.BackupId);

        restoreResult.IsSuccess.Should().BeTrue();
        restoreResult.RestoredFilePath.Should().Be(origPath);
        restoreResult.ActualChecksum.Should().Be(record.ChecksumSHA256);

        string restoredContent = await File.ReadAllTextAsync(origPath);
        restoredContent.Should().Be("Văn bản gốc cần bảo vệ");

        var allBackups = await _service.GetAllBackupsAsync();
        allBackups.First(b => b.BackupId == record.BackupId).Status.Should().Be("Restored");
    }

    [Fact]
    public async Task RestoreFileAsync_WhenBackupChecksumMismatches_RefusesRestoreAndReturnsError()
    {
        string origPath = Path.Combine(_tempDirectory, "HopDong_BaoVe.docx");
        await File.WriteAllTextAsync(origPath, "Nội dung cực kỳ quan trọng");

        var record = await _service.CreateBackupAsync("BATCH_TAMPER", origPath, null, 1);

        // Cố tình can thiệp làm hỏng file backup trên đĩa
        await File.WriteAllTextAsync(record.BackupPath, "NỘI DUNG ĐÃ BỊ CAN THIỆP TRÁI PHÉP");

        var restoreResult = await _service.RestoreFileAsync(record.BackupId);

        restoreResult.IsSuccess.Should().BeFalse();
        restoreResult.Message.Should().Contain("Checksum");
        restoreResult.Message.Should().Contain("không khớp");
    }

    [Fact]
    public async Task CleanupExpiredBackupsAsync_WhenBackupsOlderThanRetention_DeletesAndMarksExpired()
    {
        string origPath = Path.Combine(_tempDirectory, "HopDong_HetHan.docx");
        await File.WriteAllTextAsync(origPath, "Nội dung cũ");

        var record = await _service.CreateBackupAsync("BATCH_CLEANUP", origPath, null, 1);
        File.Exists(record.BackupPath).Should().BeTrue();

        // Dọn dẹp với retention 0 ngày (coi như tất cả đều quá hạn)
        int cleaned = await _service.CleanupExpiredBackupsAsync(retentionDays: 0);

        cleaned.Should().Be(1);
        File.Exists(record.BackupPath).Should().BeFalse();

        var allBackups = await _service.GetAllBackupsAsync();
        allBackups.First(b => b.BackupId == record.BackupId).Status.Should().Be("Expired");
    }
}

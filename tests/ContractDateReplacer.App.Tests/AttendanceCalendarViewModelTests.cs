using System.IO;
using ContractDateReplacer.App.Services;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace ContractDateReplacer.App.Tests;
public class AttendanceCalendarViewModelTests
{
    private readonly Mock<IAttendanceCalendarService> service = new();
    private readonly Mock<IBackupRollbackService> backup = new();
    private readonly Mock<IAttendanceFileDialogService> dialog = new();
    private readonly AttendanceTable table = new("one", "Cong", "rId1", 13, 4, 15, 18, 35, "A11", 2026, 10);
    private AttendanceCalendarViewModel Create(bool errors = false)
    {
        service.Setup(s => s.Analyze("source.xlsx")).Returns(new AttendanceAnalysis(new[] { table }, Array.Empty<string>()));
        service.Setup(s => s.Preview("source.xlsx", table.Id, It.IsAny<int>(), It.IsAny<int>())).Returns((string p, string id, int y, int m) =>
            new AttendancePreview(table, y, m, "Thời gian", new[] { new AttendanceDay(1, "CN", true) }, errors ? new[] { "Cong!AH17 có công" } : Array.Empty<string>(), "CHECKSUM"));
        dialog.Setup(d => d.SaveWorkbook(It.IsAny<string>())).Returns("output.xlsx");
        backup.Setup(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default))
            .ReturnsAsync(new BackupRecord { BackupId = "backup-id" });
        return new AttendanceCalendarViewModel(service.Object, backup.Object, dialog.Object, NullLogger<AttendanceCalendarViewModel>.Instance);
    }
    [Fact]
    public async Task ChangingMonth_InvalidatesPreviewAndExport()
    {
        var vm = Create(); await vm.LoadFileAsync("source.xlsx"); await vm.BuildPreviewAsync();
        Assert.True(vm.CanExport); vm.SelectedMonth = 11;
        Assert.False(vm.CanExport); Assert.Empty(vm.PreviewDays);
    }
    [Fact]
    public async Task OccupiedExtraDay_PreventsBackupAndExport()
    {
        var vm = Create(true); await vm.LoadFileAsync("source.xlsx"); await vm.BuildPreviewAsync(); await vm.ExportAsync();
        Assert.False(vm.CanExport); Assert.Contains("AH17", vm.StatusMessage);
        backup.Verify(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default), Times.Never);
    }
    [Fact]
    public async Task BackupFailure_PreventsExport()
    {
        var vm = Create(); backup.Setup(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), default)).ThrowsAsync(new IOException("Backup failed"));
        await vm.LoadFileAsync("source.xlsx"); await vm.BuildPreviewAsync(); await vm.ExportAsync();
        service.Verify(s => s.Export(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        Assert.Contains("Backup failed", vm.StatusMessage);
    }
    [Fact]
    public async Task SuccessfulExport_CreatesHistoryAndPassesPreviewChecksum()
    {
        var vm = Create(); await vm.LoadFileAsync("source.xlsx"); await vm.BuildPreviewAsync(); await vm.ExportAsync();
        backup.Verify(b => b.CreateBackupAsync(It.IsAny<string>(), "source.xlsx", "output.xlsx", It.IsAny<int>(), default), Times.Once);
        service.Verify(s => s.Export("source.xlsx", "output.xlsx", table.Id, 2026, 10, "CHECKSUM"), Times.Once);
        Assert.Equal("output.xlsx", vm.OutputPath);
    }
    [Fact]
    public async Task FailedExport_RemovesFailedHistoryRecord()
    {
        var vm = Create(); service.Setup(s => s.Export(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>())).Throws(new IOException("Export failed"));
        await vm.LoadFileAsync("source.xlsx"); await vm.BuildPreviewAsync(); await vm.ExportAsync();
        backup.Verify(b => b.DeleteBackupAsync("backup-id", default), Times.Once);
        Assert.Empty(vm.OutputPath); Assert.Contains("Export failed", vm.StatusMessage);
    }
}

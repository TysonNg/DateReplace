using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel hiển thị từng dòng sự kiện kiểm toán trên giao diện người dùng.
/// </summary>
public partial class AuditLogItemViewModel : ObservableObject
{
    public AuditLogEntry Entry { get; }

    public string LogId => Entry.LogId;
    public string TimestampText => Entry.Timestamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
    public AuditActionType ActionType => Entry.ActionType;
    public AuditSeverity Severity => Entry.Severity;
    public string UserName => Entry.UserName;
    public string MachineName => Entry.MachineName;
    public string? BatchId => Entry.BatchId;
    public string TargetFile => Entry.TargetFile ?? string.Empty;
    public string TargetFileName => string.IsNullOrEmpty(Entry.TargetFile) ? "--" : Path.GetFileName(Entry.TargetFile);
    public string DetailsJson => Entry.DetailsJson;
    public string IntegrityHash => Entry.IntegrityHash;
    public string IntegrityHashShort => Entry.IntegrityHash.Length > 16 ? Entry.IntegrityHash[..16] + "..." : Entry.IntegrityHash;

    public string ActionTypeDisplayName => Entry.ActionType switch
    {
        AuditActionType.BatchExecution => "Chạy Đợt Thay Thế",
        AuditActionType.FileRollback => "Khôi Phục Tệp",
        AuditActionType.BatchRollback => "Khôi Phục Cả Đợt",
        AuditActionType.RuleModified => "Thay Đổi Quy Tắc",
        AuditActionType.PresetModified => "Thay Đổi Mẫu Preset",
        AuditActionType.ConfigChanged => "Đổi Cấu Hình Hệ Thống",
        AuditActionType.SecurityAlert => "Cảnh Báo Bảo Mật",
        _ => Entry.ActionType.ToString()
    };

    public string SeverityBadgeAppearance => Entry.Severity switch
    {
        AuditSeverity.Info => "Success",
        AuditSeverity.Warning => "Warning",
        AuditSeverity.Error => "Danger",
        AuditSeverity.Critical => "Danger",
        _ => "Secondary"
    };

    public AuditLogItemViewModel(AuditLogEntry entry)
    {
        Entry = entry;
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// Đại diện cho một đợt thay thế (Batch) trong màn hình Lịch sử & Khôi phục.
/// </summary>
public partial class BatchHistoryGroupViewModel : ObservableObject
{
    public string BatchId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayTitle => $"Đợt thay thế {CreatedAt:dd/MM/yyyy HH:mm:ss}";
    public string DisplaySubtitle => $"{Backups.Count} file • Tổng {TotalReplacements} ngày thay";

    public int TotalReplacements => Backups.Sum(b => b.ReplacementCount);

    public ObservableCollection<BackupRecord> Backups { get; } = new();

    public string StatusSummary
    {
        get
        {
            if (Backups.All(b => b.Status == "Restored")) return "Đã khôi phục toàn bộ";
            if (Backups.Any(b => b.Status == "Restored")) return "Đã khôi phục một phần";
            return "Có thể khôi phục";
        }
    }

    public string StatusAppearance
    {
        get
        {
            if (Backups.All(b => b.Status == "Restored")) return "Info";
            if (Backups.Any(b => b.Status == "Restored")) return "Caution";
            return "Success";
        }
    }
}

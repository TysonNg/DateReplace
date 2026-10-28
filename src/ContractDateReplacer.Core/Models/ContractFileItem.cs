using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Đại diện cho một file hợp đồng trong danh sách xử lý hàng loạt.
/// Hỗ trợ INotifyPropertyChanged để UI tự động cập nhật trạng thái quét.
/// </summary>
public class ContractFileItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private FileScanStatus _status = FileScanStatus.NotScanned;
    private string? _statusMessage;
    private int _classifiedCount;
    private int _unknownCount;
    private int _replaceableDatesCount;
    private int _safetyScore = 100;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string DocumentType => Path.GetExtension(FilePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ? "Excel" : "Word";
    public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
    public long FileSizeBytes { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public FileScanStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeAppearance));
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetField(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    private int _totalDatesDetected;
    public int TotalDatesDetected
    {
        get => DetectedCandidates.Count > 0 ? DetectedCandidates.Count : _totalDatesDetected;
        set
        {
            if (SetField(ref _totalDatesDetected, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public int ClassifiedCount
    {
        get => _classifiedCount;
        set
        {
            if (SetField(ref _classifiedCount, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public int UnknownCount
    {
        get => _unknownCount;
        set
        {
            if (SetField(ref _unknownCount, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public int ReplaceableDatesCount
    {
        get => _replaceableDatesCount;
        set
        {
            if (SetField(ref _replaceableDatesCount, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public int SafetyScore
    {
        get => _safetyScore;
        set => SetField(ref _safetyScore, value);
    }

    public bool HasError => Status == FileScanStatus.Error;

    public List<DateCandidate> DetectedCandidates { get; set; } = new();
    public List<DocumentTextSegment> Segments { get; set; } = new();

    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    public string StatusBadgeText => Status switch
    {
        FileScanStatus.NotScanned => "CHƯA QUÉT",
        FileScanStatus.Scanning => "ĐANG QUÉT",
        FileScanStatus.Pass => "✓ AN TOÀN",
        FileScanStatus.ReviewRequired => "⚠ CẦN KIỂM TRA",
        FileScanStatus.Error => "✕ LỖI",
        FileScanStatus.Completed => "✓ HOÀN TẤT",
        _ => "KHÔNG RÕ"
    };

    public string StatusBadgeAppearance => Status switch
    {
        FileScanStatus.NotScanned => "Secondary",
        FileScanStatus.Scanning => "Info",
        FileScanStatus.Pass => "Success",
        FileScanStatus.ReviewRequired => "Caution",
        FileScanStatus.Error => "Danger",
        FileScanStatus.Completed => "Success",
        _ => "Secondary"
    };

    public string SummaryText
    {
        get
        {
            if (!string.IsNullOrEmpty(StatusMessage) && (Status == FileScanStatus.Error || Status == FileScanStatus.ReviewRequired))
            {
                return StatusMessage;
            }

            return Status switch
            {
                FileScanStatus.NotScanned => "Chưa quét nội dung tài liệu",
                FileScanStatus.Scanning => "Đang quét nội dung tài liệu...",
                FileScanStatus.Pass => TotalDatesDetected > 0 
                    ? $"{TotalDatesDetected} ngày phát hiện • {ReplaceableDatesCount} ngày có thể thay" 
                    : $"{Segments.Count} phân đoạn văn bản • Chưa phát hiện ngày",
                FileScanStatus.ReviewRequired => $"{TotalDatesDetected} ngày phát hiện • {UnknownCount} ngày cần kiểm tra",
                FileScanStatus.Error => StatusMessage ?? "Không thể đọc tài liệu",
                FileScanStatus.Completed => "Đã thay thế ngày và xác minh thành công",
                _ => "Chưa có thông tin"
            };
        }
    }

    public void RefreshTotals()
    {
        OnPropertyChanged(nameof(TotalDatesDetected));
        OnPropertyChanged(nameof(SummaryText));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}



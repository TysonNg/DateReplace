using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Helpers;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel đại diện cho một mục ngày tháng phát hiện được hiển thị trên giao diện Review & Selection.
/// Hỗ trợ chọn riêng lẻ, chỉ định ngày mới độc lập, và cảnh báo an toàn khi chọn ngày sinh / CCCD.
/// </summary>
public partial class DateItemViewModel : ObservableObject
{
    public DateCandidate Candidate { get; }
    public ContractFileItem FileItem { get; }
    public DocumentTextSegment? Segment { get; }
    private readonly IDateIntelligenceService? _dateIntelligence;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private DateTime? _targetNewDate;

    [ObservableProperty]
    private bool _hasNonWorkingDayWarning;

    [ObservableProperty]
    private string _nonWorkingDayReason = string.Empty;

    [ObservableProperty]
    private DateTime? _suggestedBusinessDay;

    public DateItemViewModel(
        DateCandidate candidate, 
        ContractFileItem fileItem, 
        DocumentTextSegment? segment = null,
        IDateIntelligenceService? dateIntelligence = null)
    {
        Candidate = candidate;
        FileItem = fileItem;
        Segment = segment;
        _dateIntelligence = dateIntelligence;

        // Mặc định chọn thay thế theo khuyến nghị của bộ phân loại
        _isSelected = CanReplace && candidate.Classification.IsRecommendedForReplacement;

        // Khởi tạo ngày mới mặc định — người dùng tự chọn ngày tùy ý
        _targetNewDate = null;

        CheckDateIntelligence();
    }

    public bool CanReplace => Segment?.IsSupportedForReplacement != false;
    public string StorageTypeLabel => !CanReplace ? (Segment?.IsSpreadsheetFormula == true ? "Công thức / Chỉ đọc" : "Ô được bảo vệ / Chỉ đọc") :
        System.IO.Path.GetExtension(FilePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? (Segment?.IsSpreadsheetNumericDate == true ? "Ngày Excel (số)" : "Văn bản") : "Văn bản Word";
    public string ClassificationExplanation => Candidate.Classification.Explanation;

partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanReplace) { IsSelected = false; return; }
        OnPropertyChanged(nameof(HasSensitiveWarning));
        OnPropertyChanged(nameof(StatusBadgeAppearance));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(HasEffectiveChange));
        CheckDateIntelligence();
    }

    partial void OnTargetNewDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(FormattedNewDate));
        OnPropertyChanged(nameof(FormattedReplacementText));
        OnPropertyChanged(nameof(HasEffectiveChange));
        CheckDateIntelligence();
    }

    public void ApplySuggestedBusinessDay()
    {
        if (SuggestedBusinessDay.HasValue)
        {
            TargetNewDate = SuggestedBusinessDay.Value;
        }
    }

    private void CheckDateIntelligence()
    {
        if (_dateIntelligence != null && TargetNewDate.HasValue && IsSelected)
        {
            var (isNonWorking, reason, suggested) = _dateIntelligence.AnalyzeDateCompliance(TargetNewDate.Value);
            HasNonWorkingDayWarning = isNonWorking;
            NonWorkingDayReason = reason;
            SuggestedBusinessDay = suggested;
        }
        else
        {
            HasNonWorkingDayWarning = false;
            NonWorkingDayReason = string.Empty;
            SuggestedBusinessDay = null;
        }
    }

    public string DateCompletenessText => Candidate.PatternType switch
    {
        DatePatternType.DayMonth => "Thiếu năm",
        DatePatternType.MonthYearDelimited or DatePatternType.VietnameseMonthYear => "Thiếu ngày",
        DatePatternType.VietnameseYearOnly => "Chỉ có năm",
        _ => string.Empty
    };

    public string RawText => Candidate.RawText;
    public string FileName => FileItem.FileName;
    public string FilePath => FileItem.FilePath;
    public DateClassificationType ClassificationType => Candidate.Classification.Type;

    public string ClassificationTypeName => Candidate.Classification.Type switch
    {
        DateClassificationType.ContractSignDate => "Ngày ký hợp đồng",
        DateClassificationType.ContractEffectiveDate => "Ngày hiệu lực",
        DateClassificationType.ContractStartDate => "Ngày bắt đầu thực hiện",
        DateClassificationType.ContractEndDate => "Ngày hết hạn hợp đồng",
        DateClassificationType.BirthDate => "Ngày sinh cá nhân",
        DateClassificationType.IdentityIssueDate => "Ngày cấp CCCD/CMND",
        DateClassificationType.IdentityExpiryDate => "Ngày hết hạn CCCD",
        DateClassificationType.PaymentDate => "Hạn thanh toán",
        DateClassificationType.HandoverDate => "Ngày bàn giao / nghiệm thu",
        DateClassificationType.AppendixDate => "Ngày phụ lục hợp đồng",
        DateClassificationType.DisputeDate => "Ngày tranh chấp / tố tụng",
        DateClassificationType.DocumentDate => "Ngày ban hành văn bản",
        _ => "Chưa xác định ngữ cảnh"
    };

    public string ConfidenceText => $"Độ tin cậy: {Math.Round(Candidate.Classification.Confidence * 100)}%";

    public string SourceLocation => Segment?.SourceLocation 
        ?? (Segment?.ParagraphIndex > 0 ? $"Đoạn {Segment.ParagraphIndex}" : "Văn bản");

    public string ContextBefore => Candidate.ContextBefore;
    public string ContextAfter => Candidate.ContextAfter;

    public string FormattedContextDisplay => 
        $"{(string.IsNullOrWhiteSpace(ContextBefore) ? "" : "..." + ContextBefore)}[{RawText}]{(string.IsNullOrWhiteSpace(ContextAfter) ? "" : ContextAfter + "...")}";

    public bool IsRecommendedForReplacement => CanReplace && Candidate.Classification.IsRecommendedForReplacement;

    public string RecommendationText => IsRecommendedForReplacement 
        ? "Đề xuất thay: CÓ" 
        : "Đề xuất thay: KHÔNG (Giữ nguyên)";

    public bool IsSensitivePersonalData =>
        Candidate.Classification.Type is DateClassificationType.BirthDate
                                       or DateClassificationType.IdentityIssueDate
                                       or DateClassificationType.IdentityExpiryDate;

    public bool HasSensitiveWarning => IsSelected && IsSensitivePersonalData;

    public string SensitiveWarningMessage => 
        "⚠ CẢNH BÁO: Bạn đang chọn thay thế Ngày sinh / Ngày cấp giấy tờ tùy thân cá nhân! Vui lòng kiểm tra kỹ trước khi áp dụng.";

    public bool IsReviewRequired => Candidate.Classification.IsReviewRequired;

    public string FormattedNewDate => TargetNewDate?.ToString("dd/MM/yyyy") ?? "Chưa chọn";

    public string FormattedReplacementText => TargetNewDate.HasValue
        ? DateFormattingHelper.FormatReplacementDate(Candidate, TargetNewDate.Value)
        : RawText;

    public bool HasEffectiveChange => CanReplace && IsSelected &&
                                      TargetNewDate.HasValue &&
                                      !string.Equals(FormattedReplacementText, RawText, StringComparison.Ordinal);

    public string StatusBadgeText => !CanReplace ? "GIỮ CÔNG THỨC" : IsSelected ? "SẼ THAY THẾ" : "GIỮ NGUYÊN";

    public string StatusBadgeAppearance => IsSelected 
        ? (IsSensitivePersonalData ? "Caution" : "Success") 
        : "Secondary";
}




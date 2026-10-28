using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý từng dòng quy tắc phân loại ngữ cảnh ngày.
/// </summary>
public partial class RuleItemViewModel : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public DateClassificationType TargetType { get; set; }
    public int Priority { get; set; } = 80;
    public double DefaultConfidence { get; set; } = 0.95;
    public bool IsBuiltIn { get; set; }

    [ObservableProperty]
    private bool _isEnabled = true;

    public bool IsCustom => !IsBuiltIn;
    public bool CanEdit => !IsBuiltIn;
    public bool CanDelete => !IsBuiltIn;

    public string KeywordsDisplayText => string.Join(", ", Keywords);

    public string TypeDisplayName => TargetType switch
    {
        DateClassificationType.BirthDate => "Ngày sinh (Nhân thân)",
        DateClassificationType.IdentityIssueDate => "Ngày cấp CCCD (Nhân thân)",
        DateClassificationType.IdentityExpiryDate => "Ngày hết hạn CCCD",
        DateClassificationType.ContractSignDate => "Ngày ký hợp đồng",
        DateClassificationType.ContractEffectiveDate => "Ngày hiệu lực",
        DateClassificationType.ContractStartDate => "Ngày bắt đầu",
        DateClassificationType.ContractEndDate => "Ngày kết thúc",
        DateClassificationType.PaymentDate => "Ngày thanh toán",
        DateClassificationType.HandoverDate => "Ngày bàn giao",
        DateClassificationType.DocumentDate => "Ngày văn bản",
        DateClassificationType.AppendixDate => "Ngày phụ lục",
        DateClassificationType.DisputeDate => "Ngày tranh chấp",
        _ => "Khác / Không rõ"
    };

    public string BadgeAppearance => TargetType switch
    {
        DateClassificationType.BirthDate or DateClassificationType.IdentityIssueDate => "Danger",
        DateClassificationType.ContractSignDate or DateClassificationType.ContractEffectiveDate => "Success",
        DateClassificationType.PaymentDate or DateClassificationType.HandoverDate => "Info",
        _ => "Secondary"
    };

    public ClassificationRule ToModel()
    {
        return new ClassificationRule
        {
            Id = Id,
            Name = Name,
            Keywords = new List<string>(Keywords),
            TargetType = TargetType,
            Priority = Priority,
            DefaultConfidence = DefaultConfidence,
            IsEnabled = IsEnabled,
            IsBuiltIn = IsBuiltIn
        };
    }

    public static RuleItemViewModel FromModel(ClassificationRule model)
    {
        return new RuleItemViewModel
        {
            Id = model.Id,
            Name = model.Name,
            Keywords = new List<string>(model.Keywords),
            TargetType = model.TargetType,
            Priority = model.Priority,
            DefaultConfidence = model.DefaultConfidence,
            IsEnabled = model.IsEnabled,
            IsBuiltIn = model.IsBuiltIn
        };
    }
}

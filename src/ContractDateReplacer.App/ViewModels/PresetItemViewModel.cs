using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel đại diện cho từng mẫu thiết lập (Preset) hiển thị trên giao diện.
/// </summary>
public partial class PresetItemViewModel : ObservableObject
{
    public string PresetId { get; set; } = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    public DateTime CreatedAt { get; set; }

    [ObservableProperty]
    private bool _isDefault;

    public bool IsBuiltIn { get; set; }

    public bool IsCustom => !IsBuiltIn;
    public bool CanDelete => !IsBuiltIn;

    public List<DateOffsetRule> DateMappingRules { get; set; } = new();
    public string OutputMode { get; set; } = "Suffix";
    public string SuffixPattern { get; set; } = "_2026";
    public string CustomFolder { get; set; } = string.Empty;

    public string RulesSummaryText
    {
        get
        {
            if (DateMappingRules == null || DateMappingRules.Count == 0)
                return "Chưa có quy tắc ánh xạ ngày.";

            var items = DateMappingRules.Where(r => r.IsActive).Select(r =>
            {
                string modeDesc = r.Mode switch
                {
                    DateOffsetMode.CurrentDate => "Hôm nay",
                    DateOffsetMode.FixedDate => r.FixedValue?.ToString("dd/MM/yyyy") ?? "Cố định",
                    DateOffsetMode.OffsetFromAnotherDate => $"{r.BaseDateType} +{r.OffsetYears}n +{r.OffsetMonths}t +{r.OffsetDays}d",
                    DateOffsetMode.OffsetYearsFromSelf => $"+{r.OffsetYears} năm",
                    DateOffsetMode.KeepOriginal => "Giữ nguyên",
                    _ => r.Mode.ToString()
                };
                return $"{r.TargetClassificationType} ➔ {modeDesc}";
            });

            return string.Join(" • ", items);
        }
    }

    public string BadgeText => IsDefault ? "Mặc định" : (IsBuiltIn ? "Hệ thống" : "Tự tạo");

    public string BadgeAppearance => IsDefault ? "Success" : (IsBuiltIn ? "Info" : "Secondary");

    public PresetModel ToModel()
    {
        return new PresetModel
        {
            PresetId = PresetId,
            Name = Name,
            Description = Description,
            CreatedAt = CreatedAt,
            DateMappingRules = new List<DateOffsetRule>(DateMappingRules),
            OutputMode = OutputMode,
            SuffixPattern = SuffixPattern,
            CustomFolder = CustomFolder,
            IsDefault = IsDefault,
            IsBuiltIn = IsBuiltIn
        };
    }

    public static PresetItemViewModel FromModel(PresetModel model)
    {
        return new PresetItemViewModel
        {
            PresetId = model.PresetId,
            Name = model.Name,
            Description = model.Description,
            CreatedAt = model.CreatedAt,
            IsDefault = model.IsDefault,
            IsBuiltIn = model.IsBuiltIn,
            DateMappingRules = new List<DateOffsetRule>(model.DateMappingRules),
            OutputMode = model.OutputMode,
            SuffixPattern = model.SuffixPattern,
            CustomFolder = model.CustomFolder
        };
    }
}

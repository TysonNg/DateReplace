using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Quản Lý Mẫu Thiết Lập (Preset Management View - Phase 14).
/// </summary>
public partial class PresetManagementViewModel : ObservableObject
{
    private readonly IPresetRepository _presetRepository;
    private readonly IPresetApplicationService _presetAppService;
    private readonly ILogger<PresetManagementViewModel> _logger;

    public ObservableCollection<PresetItemViewModel> Presets { get; } = new();

    [ObservableProperty]
    private PresetItemViewModel? _selectedPreset;

    [ObservableProperty]
    private string _feedbackMessage = string.Empty;

    // Form Editor
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editorTitle = "Thêm Mẫu Thiết Lập Mới";

    [ObservableProperty]
    private string _editingPresetId = string.Empty;

    [ObservableProperty]
    private string _editingName = string.Empty;

    [ObservableProperty]
    private string _editingDescription = string.Empty;

    [ObservableProperty]
    private string _editingOutputMode = "Suffix";

    [ObservableProperty]
    private string _editingSuffixPattern = "_2026";

    [ObservableProperty]
    private bool _editingIsDefault;

    // Fast mapping configuration
    [ObservableProperty]
    private DateOffsetMode _signDateMode = DateOffsetMode.CurrentDate;

    [ObservableProperty]
    private DateOffsetMode _startDateMode = DateOffsetMode.CurrentDate;

    [ObservableProperty]
    private DateOffsetMode _endDateMode = DateOffsetMode.OffsetFromAnotherDate;

    [ObservableProperty]
    private int _endDateOffsetYears = 1;

    // Preview calculated results
    [ObservableProperty]
    private string _previewSignDateText = string.Empty;

    [ObservableProperty]
    private string _previewStartDateText = string.Empty;

    [ObservableProperty]
    private string _previewEndDateText = string.Empty;

    public event Action? RequestNavigateBack;

    public PresetManagementViewModel(
        IPresetRepository presetRepository,
        IPresetApplicationService presetAppService,
        ILogger<PresetManagementViewModel> logger)
    {
        _presetRepository = presetRepository;
        _presetAppService = presetAppService;
        _logger = logger;
    }

    [RelayCommand]
    public async Task LoadPresetsAsync()
    {
        Presets.Clear();
        FeedbackMessage = string.Empty;

        try
        {
            await _presetRepository.EnsureSeedPresetsAsync();
            var list = await _presetRepository.GetAllPresetsAsync();

            foreach (var p in list)
            {
                Presets.Add(PresetItemViewModel.FromModel(p));
            }

            if (Presets.Count > 0 && SelectedPreset == null)
            {
                SelectedPreset = Presets.FirstOrDefault(p => p.IsDefault) ?? Presets[0];
            }

            FeedbackMessage = $"Đã tải {Presets.Count} mẫu thiết lập.";
            UpdatePreviewCalculation();
            _logger.LogInformation("Đã tải {Count} Presets từ SQLite.", Presets.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi nạp danh sách Presets");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    partial void OnSelectedPresetChanged(PresetItemViewModel? value)
    {
        UpdatePreviewCalculation();
    }

    private void UpdatePreviewCalculation()
    {
        if (SelectedPreset == null)
        {
            PreviewSignDateText = "--";
            PreviewStartDateText = "--";
            PreviewEndDateText = "--";
            return;
        }

        var sampleOriginal = new Dictionary<DateClassificationType, DateTime>
        {
            [DateClassificationType.ContractSignDate] = new DateTime(2025, 1, 15),
            [DateClassificationType.ContractStartDate] = new DateTime(2025, 2, 1),
            [DateClassificationType.ContractEndDate] = new DateTime(2026, 1, 31)
        };

        var resolved = _presetAppService.ResolveTargetDates(SelectedPreset.ToModel(), sampleOriginal);

        PreviewSignDateText = resolved.TryGetValue(DateClassificationType.ContractSignDate, out var sDate)
            ? sDate.ToString("dd/MM/yyyy") : "(Giữ nguyên)";

        PreviewStartDateText = resolved.TryGetValue(DateClassificationType.ContractStartDate, out var stDate)
            ? stDate.ToString("dd/MM/yyyy") : "(Giữ nguyên)";

        PreviewEndDateText = resolved.TryGetValue(DateClassificationType.ContractEndDate, out var eDate)
            ? eDate.ToString("dd/MM/yyyy") : "(Giữ nguyên)";
    }

    [RelayCommand]
    public void StartAddNewPreset()
    {
        IsEditing = true;
        EditorTitle = "Thêm Mẫu Thiết Lập Mới";
        EditingPresetId = string.Empty;
        EditingName = string.Empty;
        EditingDescription = string.Empty;
        EditingOutputMode = "Suffix";
        EditingSuffixPattern = "_Moi";
        EditingIsDefault = false;

        SignDateMode = DateOffsetMode.CurrentDate;
        StartDateMode = DateOffsetMode.CurrentDate;
        EndDateMode = DateOffsetMode.OffsetFromAnotherDate;
        EndDateOffsetYears = 1;
    }

    [RelayCommand]
    public void StartEditPreset(PresetItemViewModel? preset)
    {
        if (preset == null) return;

        IsEditing = true;
        EditorTitle = $"Chỉnh Sửa Mẫu: {preset.Name}";
        EditingPresetId = preset.PresetId;
        EditingName = preset.Name;
        EditingDescription = preset.Description;
        EditingOutputMode = preset.OutputMode;
        EditingSuffixPattern = preset.SuffixPattern;
        EditingIsDefault = preset.IsDefault;

        // Trích xuất cấu hình quy tắc
        var signRule = preset.DateMappingRules.FirstOrDefault(r => r.TargetClassificationType == DateClassificationType.ContractSignDate);
        SignDateMode = signRule?.Mode ?? DateOffsetMode.CurrentDate;

        var startRule = preset.DateMappingRules.FirstOrDefault(r => r.TargetClassificationType == DateClassificationType.ContractStartDate);
        StartDateMode = startRule?.Mode ?? DateOffsetMode.CurrentDate;

        var endRule = preset.DateMappingRules.FirstOrDefault(r => r.TargetClassificationType == DateClassificationType.ContractEndDate);
        EndDateMode = endRule?.Mode ?? DateOffsetMode.OffsetFromAnotherDate;
        EndDateOffsetYears = endRule?.OffsetYears ?? 1;
    }

    [RelayCommand]
    public async Task SavePresetAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingName))
        {
            FeedbackMessage = "Vui lòng nhập tên mẫu thiết lập!";
            return;
        }

        var rules = new List<DateOffsetRule>
        {
            new()
            {
                TargetClassificationType = DateClassificationType.ContractSignDate,
                Mode = SignDateMode
            },
            new()
            {
                TargetClassificationType = DateClassificationType.ContractEffectiveDate,
                Mode = SignDateMode
            },
            new()
            {
                TargetClassificationType = DateClassificationType.ContractStartDate,
                Mode = StartDateMode
            },
            new()
            {
                TargetClassificationType = DateClassificationType.ContractEndDate,
                Mode = EndDateMode,
                BaseDateType = DateClassificationType.ContractStartDate,
                OffsetYears = EndDateOffsetYears
            }
        };

        var model = new PresetModel
        {
            PresetId = string.IsNullOrWhiteSpace(EditingPresetId) ? Guid.NewGuid().ToString("N") : EditingPresetId,
            Name = EditingName.Trim(),
            Description = EditingDescription?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            DateMappingRules = rules,
            OutputMode = EditingOutputMode,
            SuffixPattern = EditingSuffixPattern,
            IsDefault = EditingIsDefault,
            IsBuiltIn = false
        };

        try
        {
            await _presetRepository.SavePresetAsync(model);
            IsEditing = false;
            FeedbackMessage = $"Đã lưu mẫu '{model.Name}' thành công!";
            _logger.LogInformation("Đã lưu Preset: {Name} (Id: {Id})", model.Name, model.PresetId);

            await LoadPresetsAsync();
            SelectedPreset = Presets.FirstOrDefault(p => p.PresetId == model.PresetId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu Preset");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    public async Task DeletePresetAsync(PresetItemViewModel? preset)
    {
        if (preset == null) return;

        if (preset.IsBuiltIn)
        {
            FeedbackMessage = "Không thể xóa mẫu thiết lập mặc định của hệ thống!";
            return;
        }

        try
        {
            bool deleted = await _presetRepository.DeletePresetAsync(preset.PresetId);
            if (deleted)
            {
                FeedbackMessage = $"Đã xóa mẫu '{preset.Name}'.";
                Presets.Remove(preset);
                if (IsEditing && EditingPresetId == preset.PresetId)
                {
                    IsEditing = false;
                }
                SelectedPreset = Presets.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa Preset");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SetDefaultPresetAsync(PresetItemViewModel? preset)
    {
        if (preset == null) return;

        try
        {
            await _presetRepository.SetDefaultPresetAsync(preset.PresetId);
            foreach (var p in Presets)
            {
                p.IsDefault = (p.PresetId == preset.PresetId);
            }
            FeedbackMessage = $"Đã đặt mẫu '{preset.Name}' làm mặc định khi mở ứng dụng.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi đặt Preset mặc định");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }
}

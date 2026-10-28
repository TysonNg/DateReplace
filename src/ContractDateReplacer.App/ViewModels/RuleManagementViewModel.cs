using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel quản lý màn hình Quản Lý Luật Phân Loại (Rule Management View - Phase 13).
/// </summary>
public partial class RuleManagementViewModel : ObservableObject
{
    private readonly IRuleRepository _ruleRepository;
    private readonly IDateDetector _detector;
    private readonly IDateClassifier _classifier;
    private readonly ILogger<RuleManagementViewModel> _logger;

    public ObservableCollection<RuleItemViewModel> Rules { get; } = new();
    public ICollectionView FilteredRulesView { get; }

    [ObservableProperty]
    private RuleItemViewModel? _selectedRule;

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private string _selectedFilter = "All"; // "All", "BuiltIn", "Custom"

    // Form Editor (Thêm / Sửa Rule)
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private int _editingRuleId;

    [ObservableProperty]
    private string _editingRuleName = string.Empty;

    [ObservableProperty]
    private string _editingKeywordsText = string.Empty;

    [ObservableProperty]
    private DateClassificationType _editingTargetType = DateClassificationType.ContractSignDate;

    [ObservableProperty]
    private int _editingPriority = 80;

    [ObservableProperty]
    private double _editingConfidence = 0.95;

    [ObservableProperty]
    private bool _editingIsEnabled = true;

    [ObservableProperty]
    private string _editorTitle = "Thêm Quy Tắc Mới";

    [ObservableProperty]
    private string _feedbackMessage = string.Empty;

    // Rule Tester
    [ObservableProperty]
    private string _testInputText = "Hôm nay, ngày 02/09/2026 hai bên đại diện ký kết hợp đồng dịch vụ tại trụ sở.";

    [ObservableProperty]
    private string _testResultText = string.Empty;

    [ObservableProperty]
    private string _testResultType = string.Empty;

    [ObservableProperty]
    private string _testResultConfidence = string.Empty;

    [ObservableProperty]
    private bool _hasTestResult;

    public IReadOnlyList<DateClassificationType> AvailableTypes => Enum.GetValues<DateClassificationType>()
        .Where(t => t != DateClassificationType.Unknown && t != DateClassificationType.Other)
        .ToList();

    public event Action? RequestNavigateBack;

    public RuleManagementViewModel(
        IRuleRepository ruleRepository,
        IDateDetector detector,
        IDateClassifier classifier,
        ILogger<RuleManagementViewModel> logger)
    {
        _ruleRepository = ruleRepository;
        _detector = detector;
        _classifier = classifier;
        _logger = logger;

        FilteredRulesView = CollectionViewSource.GetDefaultView(Rules);
        FilteredRulesView.Filter = FilterRuleItem;
    }

    private bool FilterRuleItem(object obj)
    {
        if (obj is not RuleItemViewModel item) return false;

        // 1. Tab filter
        bool matchTab = SelectedFilter switch
        {
            "BuiltIn" => item.IsBuiltIn,
            "Custom" => item.IsCustom,
            _ => true
        };
        if (!matchTab) return false;

        // 2. Search keyword filter
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return true;

        string kw = SearchKeyword.Trim().ToLowerInvariant();
        return item.Name.ToLowerInvariant().Contains(kw) ||
               item.KeywordsDisplayText.ToLowerInvariant().Contains(kw) ||
               item.TypeDisplayName.ToLowerInvariant().Contains(kw);
    }

    partial void OnSearchKeywordChanged(string value) => FilteredRulesView.Refresh();
    partial void OnSelectedFilterChanged(string value) => FilteredRulesView.Refresh();

    [RelayCommand]
    public async Task LoadRulesAsync()
    {
        Rules.Clear();
        FeedbackMessage = string.Empty;

        try
        {
            await _ruleRepository.EnsureSeedRulesAsync();
            var allRules = await _ruleRepository.GetAllRulesAsync();

            foreach (var r in allRules)
            {
                var vm = RuleItemViewModel.FromModel(r);
                Rules.Add(vm);
            }

            FeedbackMessage = $"Đã tải {Rules.Count} quy tắc phân loại.";
            _logger.LogInformation("Đã tải {Count} quy tắc từ SQLite.", Rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi nạp quy tắc");
            FeedbackMessage = $"Lỗi tải quy tắc: {ex.Message}";
        }
    }

    [RelayCommand]
    public void StartAddNewRule()
    {
        IsEditing = true;
        EditorTitle = "Thêm Quy Tắc Tùy Chỉnh Mới";
        EditingRuleId = 0;
        EditingRuleName = string.Empty;
        EditingKeywordsText = string.Empty;
        EditingTargetType = DateClassificationType.ContractSignDate;
        EditingPriority = 80;
        EditingConfidence = 0.95;
        EditingIsEnabled = true;
    }

    [RelayCommand]
    public void StartEditRule(RuleItemViewModel? rule)
    {
        if (rule == null) return;
        if (rule.IsBuiltIn)
        {
            FeedbackMessage = "Quy tắc mặc định của hệ thống được bảo vệ và khóa chỉnh sửa.";
            return;
        }

        IsEditing = true;
        EditorTitle = $"Chỉnh Sửa Quy Tắc: {rule.Name}";
        EditingRuleId = rule.Id;
        EditingRuleName = rule.Name;
        EditingKeywordsText = rule.KeywordsDisplayText;
        EditingTargetType = rule.TargetType;
        EditingPriority = rule.Priority;
        EditingConfidence = rule.DefaultConfidence;
        EditingIsEnabled = rule.IsEnabled;
    }

    [RelayCommand]
    public async Task SaveRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingRuleName))
        {
            FeedbackMessage = "Vui lòng nhập tên quy tắc!";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditingKeywordsText))
        {
            FeedbackMessage = "Vui lòng nhập ít nhất một từ khóa nhận diện!";
            return;
        }

        var keywords = EditingKeywordsText
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim().ToLowerInvariant())
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct()
            .ToList();

        if (keywords.Count == 0)
        {
            FeedbackMessage = "Từ khóa không hợp lệ!";
            return;
        }

        var rule = new ClassificationRule
        {
            Id = EditingRuleId,
            Name = EditingRuleName.Trim(),
            Keywords = keywords,
            TargetType = EditingTargetType,
            Priority = EditingPriority,
            DefaultConfidence = EditingConfidence,
            IsEnabled = EditingIsEnabled,
            IsBuiltIn = false // User rules are always custom
        };

        try
        {
            int savedId = await _ruleRepository.SaveRuleAsync(rule);
            IsEditing = false;
            FeedbackMessage = $"Đã lưu quy tắc '{rule.Name}' thành công!";
            _logger.LogInformation("Đã lưu quy tắc {Id}: {Name}", savedId, rule.Name);

            await LoadRulesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu quy tắc");
            FeedbackMessage = $"Lỗi khi lưu: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    public async Task DeleteRuleAsync(RuleItemViewModel? rule)
    {
        if (rule == null) return;

        if (rule.IsBuiltIn)
        {
            FeedbackMessage = "Không thể xóa quy tắc mặc định (Built-in) của hệ thống!";
            return;
        }

        try
        {
            bool deleted = await _ruleRepository.DeleteRuleAsync(rule.Id);
            if (deleted)
            {
                FeedbackMessage = $"Đã xóa quy tắc '{rule.Name}'.";
                Rules.Remove(rule);
                if (IsEditing && EditingRuleId == rule.Id)
                {
                    IsEditing = false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa quy tắc");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ToggleRuleAsync(RuleItemViewModel? rule)
    {
        if (rule == null) return;

        rule.IsEnabled = !rule.IsEnabled;
        try
        {
            var model = rule.ToModel();
            await _ruleRepository.SaveRuleAsync(model);
            FeedbackMessage = $"Đã {(rule.IsEnabled ? "bật" : "tắt")} quy tắc '{rule.Name}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật trạng thái quy tắc");
            rule.IsEnabled = !rule.IsEnabled; // revert
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ResetDefaultRulesAsync()
    {
        try
        {
            await _ruleRepository.ResetDefaultRulesAsync();
            FeedbackMessage = "Đã khôi phục thành công 12 quy tắc mặc định của hệ thống!";
            _logger.LogInformation("Đã reset toàn bộ quy tắc mặc định về seed ban đầu.");
            await LoadRulesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi khôi phục quy tắc");
            FeedbackMessage = $"Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RunRuleTesterAsync()
    {
        if (string.IsNullOrWhiteSpace(TestInputText))
        {
            TestResultText = "Vui lòng nhập đoạn văn bản mẫu để kiểm tra!";
            HasTestResult = false;
            return;
        }

        var segment = new DocumentTextSegment
        {
            LogicalText = TestInputText,
            SourceLocation = "Kiểm tra thử nghiệm"
        };

        var detectedDates = _detector.DetectDates(segment);
        if (detectedDates.Count == 0)
        {
            TestResultText = "Không tìm thấy chuỗi ngày tháng nào trong văn bản mẫu.";
            TestResultType = "Không có ngày";
            TestResultConfidence = "0%";
            HasTestResult = true;
            return;
        }

        var firstCandidate = detectedDates.First();
        var classification = await _classifier.ClassifyAsync(firstCandidate);

        firstCandidate.Classification = classification;

        TestResultType = classification.Type.ToString();
        TestResultConfidence = $"{classification.Confidence * 100:F0}%";
        TestResultText = $"Tìm thấy ngày '{firstCandidate.RawText}'. Phân loại: {classification.Type} • Đề xuất thay thế: {(classification.IsRecommendedForReplacement ? "CÓ" : "KHÔNG (Bảo lưu)")}";
        HasTestResult = true;
    }

    [RelayCommand]
    public void NavigateBack()
    {
        RequestNavigateBack?.Invoke();
    }
}

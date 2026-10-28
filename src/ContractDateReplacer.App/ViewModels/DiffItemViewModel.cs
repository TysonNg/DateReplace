using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// ViewModel đại diện cho một điểm khác biệt (Diff item) hiển thị trên màn hình Preview Changes.
/// Hỗ trợ hiển thị trực quan ở cả 2 chế độ Side-by-side và Inline Diff.
/// </summary>
public partial class DiffItemViewModel : ObservableObject
{
    public DateItemViewModel SourceItem { get; }

    public string FileName => SourceItem.FileName;
    public string FilePath => SourceItem.FilePath;
    public string SourceLocation => SourceItem.SourceLocation;
    public string ClassificationTypeName => SourceItem.ClassificationTypeName;
    public DateClassificationType ClassificationType => SourceItem.ClassificationType;

    public bool IsChanged => SourceItem.HasEffectiveChange;

    public string OldDateText => SourceItem.RawText;

    public string NewDateText { get; }

    public string ContextBefore => SourceItem.ContextBefore;
    public string ContextAfter => SourceItem.ContextAfter;

    public DiffItemViewModel(DateItemViewModel sourceItem)
    {
        SourceItem = sourceItem;

        NewDateText = sourceItem.FormattedReplacementText;
    }

    // Side-by-Side: Văn bản trước khi thay
    public string SideBySideBeforeDisplay => 
        $"{(string.IsNullOrWhiteSpace(ContextBefore) ? "" : "..." + ContextBefore)}[ {OldDateText} ]{(string.IsNullOrWhiteSpace(ContextAfter) ? "" : ContextAfter + "...")}";

    // Side-by-Side: Văn bản sau khi thay
    public string SideBySideAfterDisplay => 
        $"{(string.IsNullOrWhiteSpace(ContextBefore) ? "" : "..." + ContextBefore)}[ {NewDateText} ]{(string.IsNullOrWhiteSpace(ContextAfter) ? "" : ContextAfter + "...")}";

    // Inline Diff: Tiền tố, đoạn cũ, đoạn mới, hậu tố
    public string InlinePrefix => string.IsNullOrWhiteSpace(ContextBefore) ? "" : "..." + ContextBefore;
    public string InlineOldText => OldDateText;
    public string InlineNewText => NewDateText;
    public string InlineSuffix => string.IsNullOrWhiteSpace(ContextAfter) ? "" : ContextAfter + "...";

    public string DiffStatusText => IsChanged ? "SẼ THAY THẾ" : "GIỮ NGUYÊN";

    public string DiffStatusAppearance => IsChanged ? "Success" : "Secondary";
}

namespace ContractDateReplacer.Core.Enums;

/// <summary>
/// Xác định loại phân vùng/vùng chứa văn bản trong tài liệu Word.
/// </summary>
public enum StoryType
{
    MainDocument,
    Table,
    Header,
    Footer,
    TextBox,
    Footnote,
    Endnote,
    ContentControl,
    Unknown,
    SpreadsheetCell
}

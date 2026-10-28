using System.Collections.Generic;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Đại diện cho một phân đoạn văn bản logic được trích xuất từ Word.
/// Giữ toàn bộ thông tin vị trí XML và mảng RunMappings.
/// </summary>
public class DocumentTextSegment
{
    public string? WorksheetName { get; set; }
    public string? CellAddress { get; set; }
    public bool IsSpreadsheetNumericDate { get; set; }
    public bool IsSpreadsheetFormula { get; set; }
    public bool IsHiddenSheet { get; set; }

    public string Id { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public StoryType StoryType { get; set; } = StoryType.MainDocument;

    public int ParagraphIndex { get; set; } = -1;
    public int TableIndex { get; set; } = -1;
    public int RowIndex { get; set; } = -1;
    public int CellIndex { get; set; } = -1;
    public string? HeaderType { get; set; }
    public string? FooterType { get; set; }

    /// <summary>
    /// Chuỗi văn bản logic thu được sau khi ghép nối các Text Node / Run.
    /// </summary>
    public string LogicalText { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách mapping từng Run đóng góp vào LogicalText.
    /// </summary>
    public List<TextRunMapping> RunMappings { get; set; } = new();

    /// <summary>
    /// Mô tả vị trí thân thiện với người dùng (ví dụ: "Nội dung chính, Đoạn 5", "Bảng 1, Dòng 2, Ô 3").
    /// </summary>
    public string SourceLocation { get; set; } = string.Empty;

    /// <summary>
    /// Đường dẫn định danh XPath hoặc ID của phần tử trong cây XML.
    /// </summary>
    public string XmlPath { get; set; } = string.Empty;

    /// <summary>
    /// Cho biết phân đoạn này có được hỗ trợ can thiệp thay thế an toàn hay không.
    /// </summary>
    public bool IsSupportedForReplacement { get; set; } = true;

    /// <summary>
    /// Cho biết phân đoạn này có nằm trong trường tự động Field Code (w:fldSimple / w:fldChar) hay không.
    /// </summary>
    public bool IsFieldCode { get; set; }

    /// <summary>
    /// Câu lệnh trường tự động (ví dụ: "DATE", "CREATEDATE").
    /// </summary>
    public string? FieldInstruction { get; set; }

    /// <summary>
    /// Cho biết phân đoạn này có nằm trong thẻ nội dung Structured Document Tag (SDT) hay không.
    /// </summary>
    public bool IsContentControl { get; set; }

    /// <summary>
    /// Tiêu đề hoặc Alias của thẻ nội dung SDT.
    /// </summary>
    public string? ContentControlTitle { get; set; }

    /// <summary>
    /// Cho biết thẻ SDT này có phải là bộ chọn ngày DatePicker (w:date) hay không.
    /// </summary>
    public bool IsDatePickerContentControl { get; set; }

    /// <summary>
    /// Danh sách các ngày phát hiện trong phân đoạn này.
    /// </summary>
    public List<DateCandidate> DetectedDates { get; set; } = new();
}

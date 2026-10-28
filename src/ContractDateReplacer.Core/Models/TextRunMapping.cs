namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Ánh xạ vị trí ký tự từ chuỗi logic sang vị trí trong từng Run XML của OpenXML.
/// Giúp công cụ thay thế có thể can thiệp chính xác vào Run mà không phá vỡ formatting.
/// </summary>
public class TextRunMapping
{
    /// <summary>
    /// Vị trí bắt đầu trong chuỗi logic ghép (LogicalText).
    /// </summary>
    public int LogicalStart { get; set; }

    /// <summary>
    /// Chiều dài ký tự đóng góp vào chuỗi logic.
    /// </summary>
    public int LogicalLength { get; set; }

    /// <summary>
    /// Thứ tự của Run trong đoạn văn bản (0, 1, 2...).
    /// </summary>
    public int RunIndex { get; set; }

    /// <summary>
    /// Vị trí bắt đầu bên trong chuỗi text cục bộ của Run XML.
    /// </summary>
    public int LocalStart { get; set; }

    /// <summary>
    /// Số lượng ký tự trong Run cục bộ.
    /// </summary>
    public int LocalLength { get; set; }

    /// <summary>
    /// Định danh nội bộ của Run XML.
    /// </summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>
    /// Text nguyên bản của Run.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Thông tin thuộc tính formatting (để so sánh hoặc khôi phục).
    /// </summary>
    public bool? IsBold { get; set; }
    public bool? IsItalic { get; set; }
    public bool? IsUnderline { get; set; }
    public string? Color { get; set; }
    public string? FontName { get; set; }
    public string? FontSize { get; set; }
}

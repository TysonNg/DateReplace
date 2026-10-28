using System.Collections.Generic;
using ContractDateReplacer.Core.Enums;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Kết quả quét kiểm tra lại (Verification) độc lập sau khi ghi file mới.
/// </summary>
public class VerificationResult
{
    public string FileId { get; set; } = string.Empty;
    public string SourceFilePath { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;

    public bool CanOpenDocument { get; set; } = true;
    public bool IsValidXmlStructure { get; set; } = true;

    public int ExpectedReplacements { get; set; }
    public int ActualReplacementsDone { get; set; }

    /// <summary>
    /// Số lượng ngày cũ lẽ ra phải thay nhưng vẫn còn sót lại trong file.
    /// </summary>
    public int UnexpectedOldDatesRemaining { get; set; }

    /// <summary>
    /// Số lượng ngày cũ được giữ lại do người dùng chủ ý bỏ chọn (bình thường).
    /// </summary>
    public int IntentionallySkippedDatesRemaining { get; set; }

    /// <summary>
    /// Số lượng ngày mới xuất hiện trong file đầu ra.
    /// </summary>
    public int NewDatesFoundCount { get; set; }

    /// <summary>
    /// Điểm an toàn từ 0 đến 100%.
    /// </summary>
    public int SafetyScore { get; set; } = 100;

    public FileScanStatus FinalStatus { get; set; } = FileScanStatus.Pass;

    /// <summary>
    /// Danh sách cảnh báo hoặc lỗi phát hiện trong quá trình Verify.
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// Danh sách các vị trí cần thay thế nhưng vẫn còn sót ngày cũ trong file output.
    /// </summary>
    public List<ReplacementOccurrence> MissingReplacements { get; set; } = new();

    /// <summary>
    /// Danh sách các sửa đổi ngoài ý muốn (ví dụ: ngày sinh, ngày cấp CCCD bị thay đổi trái phép).
    /// </summary>
    public List<string> UnexpectedModifications { get; set; } = new();

    /// <summary>
    /// Trạng thái tính toàn vẹn OpenXML ("Valid" hoặc "Invalid").
    /// </summary>
    public string IntegrityStatus { get; set; } = "Valid";

    public bool IsPassed => FinalStatus == FileScanStatus.Pass;
}

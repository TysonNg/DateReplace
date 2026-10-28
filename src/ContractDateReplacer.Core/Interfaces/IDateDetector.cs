using System.Collections.Generic;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Trừu tượng hóa dịch vụ nhận diện các mẫu ngày tháng xuất hiện trong một chuỗi ký tự logic.
/// Hoàn toàn độc lập với OpenXML hoặc Word.
/// </summary>
public interface IDateDetector
{
    /// <summary>
    /// Phát hiện toàn bộ ứng viên ngày trong một phân đoạn văn bản DocumentTextSegment.
    /// </summary>
    List<DateCandidate> DetectDates(DocumentTextSegment segment);

    /// <summary>
    /// Phát hiện ứng viên ngày trực tiếp từ chuỗi ký tự logic.
    /// </summary>
    List<DateCandidate> DetectDates(string text, string segmentId = "");
}

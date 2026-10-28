using System;
using System.Collections.Generic;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Tọa độ hộp bao bounding box của vùng chữ nhận diện trong ảnh OCR (pixel).
/// </summary>
public class OcrBoundingBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public OcrBoundingBox() { }

    public OcrBoundingBox(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public override string ToString() => $"[X={X}, Y={Y}, W={Width}, H={Height}]";
}

/// <summary>
/// Đại diện cho một từ đơn được nhận dạng qua OCR kèm tọa độ và độ tin cậy.
/// </summary>
public class OcrTextWord
{
    public string Text { get; set; } = string.Empty;
    public OcrBoundingBox BoundingBox { get; set; } = new();
    public double Confidence { get; set; } = 1.0;
}

/// <summary>
/// Đại diện cho một dòng chữ nhận diện được từ OCR.
/// </summary>
public class OcrTextLine
{
    public string Text { get; set; } = string.Empty;
    public OcrBoundingBox BoundingBox { get; set; } = new();
    public List<OcrTextWord> Words { get; set; } = new();
}

/// <summary>
/// Điểm xuất hiện ngày tháng được phát hiện bên trong ảnh qua OCR.
/// </summary>
public class OcrDateCandidate
{
    public string RawText { get; set; } = string.Empty;
    public DateTime? TargetDate { get; set; }
    public OcrBoundingBox? BoundingBox { get; set; }
    public double Confidence { get; set; } = 1.0;
    public int LineIndex { get; set; } = -1;
    public string ContextSnippet { get; set; } = string.Empty;
}

/// <summary>
/// Kết quả nhận dạng ký tự quang học OCR từ một ảnh.
/// </summary>
public class OcrResult
{
    public string Text { get; set; } = string.Empty;
    public double AverageConfidence { get; set; } = 1.0;
    public List<OcrTextLine> Lines { get; set; } = new();
    public List<OcrDateCandidate> DetectedDates { get; set; } = new();
    public bool HasDetectedDates => DetectedDates.Count > 0;
}

/// <summary>
/// Đại diện cho một hình ảnh nhúng được trích xuất từ tài liệu Word.
/// </summary>
public class DocumentImageItem
{
    public string ImageId { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string PartUri { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string LocationDescription { get; set; } = string.Empty;
    public byte[]? ImageData { get; set; }

    public OcrResult? OcrResult { get; set; }
    public bool HasDetectedDates => OcrResult?.HasDetectedDates == true;
}

/// <summary>
/// Báo cáo phân tích tổng thể tài liệu Word chứa ảnh quét/hợp đồng scan.
/// </summary>
public class ScannedDocumentAnalysisResult
{
    public string FilePath { get; set; } = string.Empty;
    public bool HasEmbeddedImages { get; set; }
    public int ImageCount { get; set; }
    public bool IsLikelyScannedDocument { get; set; }
    public List<DocumentImageItem> Images { get; set; } = new();
    public List<OcrDateCandidate> AllDetectedDates { get; set; } = new();
    public string? SafetyWarning { get; set; }
}

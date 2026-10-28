using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace ContractDateReplacer.Word;

/// <summary>
/// Kiểm tra cấu trúc XML của file Word DOCX bằng OpenXmlValidator để phát hiện file hỏng.
/// </summary>
public class WordOpenXmlValidator
{
    public (bool IsValid, List<string> Errors) ValidateDocument(string filePath)
    {
        var errors = new List<string>();

        if (!File.Exists(filePath))
        {
            errors.Add($"File không tồn tại: {filePath}");
            return (false, errors);
        }

        try
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            // Sử dụng phiên bản Microsoft365 để tương thích đầy đủ với các tài liệu Word 2010 - Microsoft 365
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            var validationErrors = validator.Validate(doc);

            foreach (var error in validationErrors)
            {
                // Bỏ qua các cảnh báo schema thứ yếu/tương thích do Microsoft Word tự sinh
                if (IsBenignSchemaError(error))
                {
                    continue;
                }

                if (error.ErrorType == ValidationErrorType.Schema)
                {
                    errors.Add($"[XML Schema Error] {error.Description} tại {error.Path?.XPath}");
                }
            }

            return (errors.Count == 0, errors);
        }
        catch (System.Exception ex)
        {
            errors.Add($"Không thể mở tài liệu bằng OpenXML: {ex.Message}");
            return (false, errors);
        }
    }

    private static bool IsBenignSchemaError(ValidationErrorInfo error)
    {
        if (error.ErrorType != ValidationErrorType.Schema)
        {
            return false;
        }

        string desc = error.Description ?? string.Empty;
        string xpath = error.Path?.XPath ?? string.Empty;

        // 1. Thuộc tính tương thích trên thẻ tblLook (Word 2010+ như firstRow, lastRow, firstColumn, lastColumn, noHBand, noVBand)
        if (xpath.Contains("tblLook") || desc.Contains("tblLook"))
        {
            if (desc.Contains("firstRow") || desc.Contains("lastRow") ||
                desc.Contains("firstColumn") || desc.Contains("lastColumn") ||
                desc.Contains("noHBand") || desc.Contains("noVBand"))
            {
                return true;
            }
        }

        // 2. Thuộc tính tương thích trên stylePaneFormatFilter
        if (xpath.Contains("stylePaneFormatFilter") || desc.Contains("stylePaneFormatFilter"))
        {
            return true;
        }

        // 3. Thuộc tính mc:Ignorable hoặc namespace mở rộng w14, w15, w16
        if (desc.Contains("Ignorable") || desc.Contains("w14:") || desc.Contains("w15:") || desc.Contains("w16"))
        {
            return true;
        }

        return false;
    }
}

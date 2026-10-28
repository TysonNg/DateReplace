using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ContractDateReplacer.Word;

/// <summary>
/// Tiện ích nhận diện định dạng tệp tin Word và tự động chuyển đổi tài liệu Word 97-2003 cũ (.doc) sang .docx chuẩn OpenXML.
/// </summary>
public static class WordFormatHelper
{
    private static readonly byte[] DocMagicBytes = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
    private static readonly byte[] ZipMagicBytes = { 0x50, 0x4B }; // PK zip signature for docx

    /// <summary>
    /// Kiểm tra xem tệp tin có phải là định dạng nhị phân Word 97-2003 (.doc OLE2) nhưng bị đặt nhầm đuôi .docx hay không.
    /// </summary>
    public static bool IsLegacyWord97Doc(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] header = new byte[8];
            int read = fs.Read(header, 0, header.Length);
            if (read < 8) return false;

            for (int i = 0; i < DocMagicBytes.Length; i++)
            {
                if (header[i] != DocMagicBytes[i]) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Kiểm tra xem tệp tin có chữ ký ZIP hợp lệ của OpenXML (.docx) hay không.
    /// </summary>
    public static bool IsValidZipPackage(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] header = new byte[2];
            int read = fs.Read(header, 0, header.Length);
            return read >= 2 && header[0] == ZipMagicBytes[0] && header[1] == ZipMagicBytes[1];
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Tự động chuyển đổi file Word 97-2003 (.doc) sang file Word OpenXML (.docx) chuẩn thông qua Word COM (nếu máy có cài Office).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool TryAutoConvertLegacyDocToDocx(string sourcePath, out string? convertedPath, out string? errorMessage)
    {
        convertedPath = null;
        errorMessage = null;

        if (!File.Exists(sourcePath))
        {
            errorMessage = "Tập tin không tồn tại.";
            return false;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "ContractDateReplacer_Converted");
        Directory.CreateDirectory(tempDir);

        string safeName = Path.GetFileNameWithoutExtension(sourcePath);
        string tempDoc = Path.Combine(tempDir, $"{safeName}_{Guid.NewGuid():N}.doc");
        string destDocx = Path.Combine(tempDir, $"{safeName}_{Guid.NewGuid():N}.docx");

        Type? wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType == null)
        {
            errorMessage = "Tệp tin này có định dạng Word 97-2003 (.doc). Máy tính chưa cài đặt Microsoft Word để tự động chuyển đổi. Vui lòng mở bằng Word và Lưu lại (Save As) dạng Word Document (*.docx) chuẩn.";
            return false;
        }

        dynamic? wordApp = null;
        dynamic? doc = null;

        try
        {
            // Copy sang file có đuôi .doc thực sự để Word COM không chặn lỗi "file format does not match extension"
            File.Copy(sourcePath, tempDoc, true);

            wordApp = Activator.CreateInstance(wordType);
            if (wordApp == null)
            {
                errorMessage = "Không thể khởi tạo tiến trình Microsoft Word để chuyển đổi.";
                return false;
            }

            wordApp.Visible = false;
            wordApp.DisplayAlerts = 0; // wdAlertsNone

            doc = wordApp.Documents.Open(tempDoc);
            // 12 = wdFormatXMLDocument (chuẩn .docx)
            doc.SaveAs2(destDocx, 12);
            doc.Close(false);
            doc = null;

            if (File.Exists(destDocx) && new FileInfo(destDocx).Length > 0)
            {
                convertedPath = destDocx;
                return true;
            }

            errorMessage = "Quá trình chuyển đổi định dạng Word không tạo ra file đích.";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Không thể tự động chuyển đổi Word 97-2003 sang .docx: {ex.Message}";
            return false;
        }
        finally
        {
            if (doc != null)
            {
                try { doc.Close(false); } catch { }
                try { Marshal.ReleaseComObject(doc); } catch { }
            }

            if (wordApp != null)
            {
                try { wordApp.Quit(false); } catch { }
                try { Marshal.ReleaseComObject(wordApp); } catch { }
            }

            if (File.Exists(tempDoc))
            {
                try { File.Delete(tempDoc); } catch { }
            }
        }
    }
}

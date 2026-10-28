using System.IO;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Infrastructure.Services;
using ContractDateReplacer.Word;

namespace ContractDateReplacer.App.Services;

/// <summary>Routes each operation by the source format, keeping Word and Excel implementations independent.</summary>
public sealed class DocumentFormatRouter : IDocumentScanner, IDocumentReplacementService, IDocumentVerificationService
{
    private readonly WordDocumentScanner _wordScanner;
    private readonly WordDocumentReplacementService _wordReplacement;
    private readonly WordDocumentVerificationService _wordVerification;
    private readonly ExcelDocumentService _excel;

    public DocumentFormatRouter(WordDocumentScanner wordScanner, WordDocumentReplacementService wordReplacement,
        WordDocumentVerificationService wordVerification, ExcelDocumentService excel)
    {
        _wordScanner = wordScanner; _wordReplacement = wordReplacement;
        _wordVerification = wordVerification; _excel = excel;
    }

    private static bool IsExcel(string path) => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
    private static void RequireSupported(string path)
    {
        if (!IsExcel(path) && !Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(path).Equals(".doc", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Chỉ hỗ trợ Word .doc, .docx và Excel .xlsx. Hãy lưu file .xls cũ thành .xlsx trước.");
    }
    public Task<List<DocumentTextSegment>> ScanDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        RequireSupported(filePath);
        return IsExcel(filePath) ? Task.Run(() => _excel.ScanDocumentAsync(filePath, cancellationToken), cancellationToken)
            : _wordScanner.ScanDocumentAsync(filePath, cancellationToken);
    }
    public Task<bool> CanReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        RequireSupported(filePath);
        return (IsExcel(filePath) ? (IDocumentScanner)_excel : _wordScanner).CanReadAsync(filePath, cancellationToken);
    }
    public Task<bool> ReplaceDatesAsync(string sourceFilePath, string destinationFilePath,
        IEnumerable<ReplacementOccurrence> selectedOccurrences, bool preserveOriginalDateFormat = true, CancellationToken cancellationToken = default)
    {
        RequireSupported(sourceFilePath);
        return IsExcel(sourceFilePath)
            ? Task.Run(() => _excel.ReplaceDatesAsync(sourceFilePath, destinationFilePath, selectedOccurrences, preserveOriginalDateFormat, cancellationToken), cancellationToken)
            : _wordReplacement.ReplaceDatesAsync(sourceFilePath, destinationFilePath, selectedOccurrences, preserveOriginalDateFormat, cancellationToken);
    }
    public Task<VerificationResult> VerifyOutputAsync(string sourceFilePath, string outputFilePath,
        IReadOnlyList<ReplacementOccurrence> originalOccurrences, CancellationToken cancellationToken = default)
    {
        RequireSupported(sourceFilePath);
        return IsExcel(sourceFilePath)
            ? Task.Run(() => _excel.VerifyOutputAsync(sourceFilePath, outputFilePath, originalOccurrences, cancellationToken), cancellationToken)
            : _wordVerification.VerifyOutputAsync(sourceFilePath, outputFilePath, originalOccurrences, cancellationToken);
    }
}



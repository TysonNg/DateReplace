using System.Globalization;
using System.Text.RegularExpressions;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace ContractDateReplacer.Infrastructure.Services;

/// <summary>Cell-based XLSX scanning, copy-on-write replacement and independent output verification.</summary>
public sealed class ExcelDocumentService : IDocumentScanner, IDocumentReplacementService, IDocumentVerificationService
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly DateDetector detector = new();

    public Task<List<DocumentTextSegment>> ScanDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(filePath);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
        var result = new List<DocumentTextSegment>();
        foreach (var sheet in workbook.Workbook.Sheets?.Elements<Sheet>() ?? Enumerable.Empty<Sheet>())
        {
            var part = (WorksheetPart)workbook.GetPartById(sheet.Id!.Value!);
            var sheetSegments = new List<DocumentTextSegment>();
            foreach (var cell in part.Worksheet.Descendants<Cell>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = CellText(workbook, cell);
                if (string.IsNullOrEmpty(text) && cell.CellFormula == null) continue;
                var address = cell.CellReference?.Value ?? throw new InvalidDataException("Cell address is missing.");
                var numericDate = TryNumericDate(workbook, cell, out var date);
                var isoDate = cell.DataType?.Value == CellValues.Date && DateTime.TryParse(text, Invariant, DateTimeStyles.RoundtripKind, out date);
                if (numericDate || isoDate) text = date.ToString("dd/MM/yyyy", Invariant);
                var segment = new DocumentTextSegment
                {
                    Id = $"{sheet.Id}!{address}", FileId = Path.GetFullPath(filePath),
                    StoryType = StoryType.SpreadsheetCell, WorksheetName = sheet.Name?.Value,
                    CellAddress = address, SourceLocation = $"{sheet.Name}!{address}",
                    XmlPath = $"{part.Uri}#{address}", LogicalText = text,
                    IsSpreadsheetNumericDate = numericDate, IsSpreadsheetFormula = cell.CellFormula != null,
                    IsSupportedForReplacement = CanReplaceCell(workbook, part.Worksheet, cell),
                    IsHiddenSheet = sheet.State != null && sheet.State.Value != SheetStateValues.Visible,
                    RowIndex = RowNumber(address) - 1, CellIndex = ColumnNumber(address) - 1
                };
                // Numeric IDs, booleans and errors must never be interpreted as textual dates.
                if (numericDate || isoDate || cell.DataType?.Value == CellValues.SharedString ||
                    cell.DataType?.Value == CellValues.InlineString || cell.DataType?.Value == CellValues.String)
                    segment.DetectedDates = detector.DetectDates(segment);
                sheetSegments.Add(segment);
            }
            var cellsByPosition = sheetSegments.ToDictionary(s => (s.RowIndex, s.CellIndex));
            var headersByColumn = new Dictionary<int, DocumentTextSegment>();
            foreach (var segment in sheetSegments.OrderBy(s => s.RowIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segment.DetectedDates.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(segment.LogicalText)) headersByColumn[segment.CellIndex] = segment;
                    continue;
                }
                headersByColumn.TryGetValue(segment.CellIndex, out var header);
                cellsByPosition.TryGetValue((segment.RowIndex, segment.CellIndex - 1), out var left);
                var context = string.Join(" | ", new[] { header?.LogicalText, left?.LogicalText }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (context.Length > 0)
                    foreach (var candidate in segment.DetectedDates) candidate.ContextBefore = context + " | " + candidate.ContextBefore;
            }
            result.AddRange(sheetSegments);
        }
        return Task.FromResult(result);
    }

    public async Task<bool> CanReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try { await ScanDocumentAsync(filePath, cancellationToken); return true; }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public Task<bool> ReplaceDatesAsync(string sourceFilePath, string destinationFilePath,
        IEnumerable<ReplacementOccurrence> selectedOccurrences, bool preserveOriginalDateFormat = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourceFilePath);
        var destination = Path.GetFullPath(destinationFilePath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) || File.Exists(destination))
            throw new IOException("The destination must be a new file, different from the original.");
        var occurrences = selectedOccurrences.Where(o => o.IsSelected).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // CreateNew prevents races with another writer and never overwrites a pre-existing file.
        using (var input = File.OpenRead(source))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
            input.CopyTo(output);
        try
        {
            using var document = SpreadsheetDocument.Open(destination, true);
            var workbook = document.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
            foreach (var group in occurrences.GroupBy(o => Key(o.Segment)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = group.First();
                var cell = FindCell(workbook, first.Segment) ?? throw new InvalidDataException($"Missing cell: {group.Key}");
                if (!CanReplaceCell(workbook, cell.Ancestors<Worksheet>().Single(), cell))
                {
                    foreach (var occurrence in group) { occurrence.Status = ReplacementStatus.Skipped; occurrence.SafetyNote = "Formula or protected cells cannot be replaced."; }
                    continue;
                }
                Apply(workbook, cell, group.ToList());
                foreach (var occurrence in group) occurrence.Status = ReplacementStatus.Success;
            }
            return Task.FromResult(true);
        }
        catch
        {
            File.Delete(destination);
            throw;
        }
    }

    public async Task<VerificationResult> VerifyOutputAsync(string sourceFilePath, string outputFilePath,
        IReadOnlyList<ReplacementOccurrence> originalOccurrences, CancellationToken cancellationToken = default)
    {
        var result = new VerificationResult { SourceFilePath = sourceFilePath, OutputFilePath = outputFilePath };
        try
        {
            var outputSegments = await ScanDocumentAsync(outputFilePath, cancellationToken);
            using var source = SpreadsheetDocument.Open(sourceFilePath, false);
            using var output = SpreadsheetDocument.Open(outputFilePath, false);
            var sourceWb = source.WorkbookPart!;
            var outputWb = output.WorkbookPart!;
            var errors = new OpenXmlValidator(FileFormatVersions.Office2013).Validate(output).Take(30).ToList();
            result.IsValidXmlStructure = errors.Count == 0;
            foreach (var error in errors) result.Issues.Add("OpenXML: " + error.Description);
            // Compare all source cells, including cells without dates, with exactly the permitted edits.
            var seen = new HashSet<string>();
            foreach (var sheet in sourceWb.Workbook.Sheets!.Elements<Sheet>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePart = (WorksheetPart)sourceWb.GetPartById(sheet.Id!.Value!);
                var targetSheet = outputWb.Workbook.Sheets!.Elements<Sheet>().FirstOrDefault(s => s.Name?.Value == sheet.Name?.Value);
                var targetPart = targetSheet == null ? null : outputWb.GetPartById(targetSheet.Id!.Value!) as WorksheetPart;
                var expectedWorksheet = (Worksheet)sourcePart.Worksheet.CloneNode(true);
                foreach (var expectedCell in expectedWorksheet.Descendants<Cell>())
                {
                    var location = $"{sheet.Name}!{expectedCell.CellReference}";
                    seen.Add(location);
                    var occurrences = originalOccurrences.Where(o => Key(o.Segment) == location).ToList();
                    var selected = occurrences.Where(o => o.IsSelected && CanReplaceCell(sourceWb, sourcePart.Worksheet, expectedCell)).ToList();
                    result.ExpectedReplacements += selected.Count;
                    var actualCell = targetPart?.Worksheet.Descendants<Cell>().FirstOrDefault(c => c.CellReference?.Value == expectedCell.CellReference?.Value);
                    if (selected.Count > 0) Apply(sourceWb, expectedCell, selected);
                    var equal = actualCell != null && expectedCell.OuterXml == actualCell.OuterXml;
                    if (equal)
                    {
                        result.ActualReplacementsDone += selected.Count;
                        result.NewDatesFoundCount += selected.Count;
                        result.IntentionallySkippedDatesRemaining += occurrences.Count(o => !o.IsSelected);
                    }
                    else
                    {
                        result.UnexpectedModifications.Add(location);
                        result.Issues.Add($"Cell content, type, style or formula differs at {location}.");
                        result.MissingReplacements.AddRange(selected);
                        foreach (var occurrence in selected)
                            if (actualCell != null && CellText(outputWb, actualCell).Contains(occurrence.Candidate.RawText, StringComparison.Ordinal))
                                result.UnexpectedOldDatesRemaining++;
                    }
                }
                if (targetPart == null || expectedWorksheet.OuterXml != targetPart.Worksheet.OuterXml)
                    result.Issues.Add($"Worksheet structure or cell content changed unexpectedly: {sheet.Name}.");
            }
            if (outputSegments.Any(s => !seen.Contains(Key(s)))) result.Issues.Add("Unexpected cells or worksheets were added.");
            if (sourceWb.Workbook.OuterXml != outputWb.Workbook.OuterXml) result.Issues.Add("Workbook metadata changed unexpectedly.");
            if (sourceWb.WorkbookStylesPart?.Stylesheet.OuterXml != outputWb.WorkbookStylesPart?.Stylesheet.OuterXml)
                result.Issues.Add("Workbook styles changed unexpectedly.");
            if (sourceWb.SharedStringTablePart?.SharedStringTable.OuterXml != outputWb.SharedStringTablePart?.SharedStringTable.OuterXml)
                result.Issues.Add("Shared strings changed unexpectedly.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            result.CanOpenDocument = false;
            result.IsValidXmlStructure = false;
            result.Issues.Add("Cannot verify XLSX package: " + exception.Message);
        }
        var passed = result.Issues.Count == 0 && result.ExpectedReplacements == result.ActualReplacementsDone;
        result.FinalStatus = passed ? FileScanStatus.Pass : FileScanStatus.Error;
        result.SafetyScore = passed ? 100 : 0;
        result.IntegrityStatus = result.IsValidXmlStructure ? "Valid" : "Invalid";
        return result;
    }

    private static string Key(DocumentTextSegment segment) => !string.IsNullOrEmpty(segment.WorksheetName) && !string.IsNullOrEmpty(segment.CellAddress)
        ? $"{segment.WorksheetName}!{segment.CellAddress}" : segment.SourceLocation;

    private static Cell? FindCell(WorkbookPart workbook, DocumentTextSegment segment)
    {
        string key = Key(segment);
        int separator = key.LastIndexOf('!');
        string? name = segment.WorksheetName ?? (separator >= 0 ? key[..separator] : null);
        string? address = segment.CellAddress ?? (separator >= 0 ? key[(separator + 1)..] : null);
        var sheet = workbook.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault(s => s.Name?.Value == name);
        if (sheet == null || address == null) return null;
        return ((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet.Descendants<Cell>()
            .FirstOrDefault(c => c.CellReference?.Value == address);
    }

    private static bool CanReplaceCell(WorkbookPart workbook, Worksheet worksheet, Cell cell)
    {
        if (cell.CellFormula != null) return false;
        if (worksheet.GetFirstChild<SheetProtection>()?.Sheet?.Value != true) return true;
        var styles = workbook.WorkbookStylesPart?.Stylesheet;
        var format = styles?.CellFormats?.Elements<CellFormat>().ElementAtOrDefault((int)(cell.StyleIndex?.Value ?? 0));
        bool? locked = format?.Protection?.Locked?.Value;
        if (!locked.HasValue && format?.FormatId != null)
            locked = styles?.CellStyleFormats?.Elements<CellFormat>().ElementAtOrDefault((int)format.FormatId.Value)?.Protection?.Locked?.Value;
        return locked == false;
    }
    private static void Apply(WorkbookPart workbook, Cell cell, IReadOnlyList<ReplacementOccurrence> occurrences)
    {
        if (cell.CellFormula != null || occurrences.Count == 0) return;
        if (TryNumericDate(workbook, cell, out _) || cell.DataType?.Value == CellValues.Date)
        {
            if (occurrences.Count != 1) throw new InvalidDataException("A native date cell accepts one replacement.");
            var occurrence = occurrences[0];
            var date = NewDate(occurrence).Date;
            if (cell.DataType?.Value == CellValues.Date)
            {
                var oldDate = DateTime.Parse(cell.CellValue!.Text, Invariant, DateTimeStyles.RoundtripKind);
                cell.CellValue = new CellValue(date.Add(oldDate.TimeOfDay));
            }
            else
            {
                var oldSerial = double.Parse(cell.CellValue!.Text, Invariant);
                var newSerial = ToSerial(date, Is1904(workbook)) + (oldSerial - Math.Floor(oldSerial));
                cell.CellValue = new CellValue(newSerial.ToString("R", Invariant));
            }
            return;
        }
        var text = CellText(workbook, cell);
        InlineString? inline = null;
        if (cell.DataType?.Value == CellValues.InlineString)
            inline = (InlineString?)cell.InlineString?.CloneNode(true);
        else if (cell.DataType?.Value == CellValues.SharedString)
        {
            var index = int.Parse(cell.CellValue!.Text, Invariant);
            var shared = workbook.SharedStringTablePart!.SharedStringTable.Elements<SharedStringItem>().ElementAt(index);
            // Clone every run, including its properties; shared aliases remain untouched.
            inline = new InlineString(shared.ChildElements.Select(child => child.CloneNode(true)));
        }
        inline ??= cell.DataType?.Value == CellValues.String ? null : new InlineString(new Text(text));
        var ordered = occurrences.OrderByDescending(o => o.Candidate.StartIndex).ToList();
        var limit = text.Length;
        foreach (var occurrence in ordered)
        {
            var candidate = occurrence.Candidate;
            if (candidate.StartIndex < 0 || candidate.Length <= 0 || candidate.StartIndex + candidate.Length > limit ||
                text.Substring(candidate.StartIndex, candidate.Length) != candidate.RawText)
                throw new InvalidDataException("Date text no longer matches the selected cell or selections overlap.");
            if (inline != null) ReplaceTextNodes(inline, candidate.StartIndex, candidate.Length, occurrence.NewDateText);
            text = text.Remove(candidate.StartIndex, candidate.Length).Insert(candidate.StartIndex, occurrence.NewDateText);
            limit = candidate.StartIndex;
        }
        if (inline == null)
            cell.CellValue = new CellValue(text);
        else
        {
            cell.CellValue = null;
            cell.DataType = CellValues.InlineString;
            cell.InlineString = inline;
        }
    }

    private static IEnumerable<Text> StringTextNodes(OpenXmlCompositeElement value)
    {
        // Phonetic guide text is not part of the displayed cell's logical character offsets.
        foreach (var child in value.ChildElements)
        {
            if (child is Text text) yield return text;
            else if (child is Run run && run.Text != null) yield return run.Text;
        }
    }

    private static void ReplaceTextNodes(InlineString value, int start, int length, string replacement)
    {
        int offset = 0;
        bool inserted = false;
        foreach (var node in StringTextNodes(value))
        {
            var original = node.Text;
            var nodeEnd = offset + original.Length;
            if (offset < start + length && nodeEnd > start)
            {
                var localStart = Math.Max(start - offset, 0);
                var localEnd = Math.Min(start + length - offset, original.Length);
                // Insert into the first intersected run so the new date inherits that run's font.
                node.Text = original[..localStart] + (inserted ? "" : replacement) + original[localEnd..];
                node.Space = SpaceProcessingModeValues.Preserve;
                inserted = true;
            }
            offset = nodeEnd;
        }
        if (!inserted) throw new InvalidDataException("Date range does not map to cell text nodes.");
    }

    private static DateTime NewDate(ReplacementOccurrence occurrence)
    {
        if (occurrence.NewDateTime.HasValue) return occurrence.NewDateTime.Value;
        if (DateTime.TryParseExact(occurrence.NewDateText, new[] { "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd" }, Invariant, DateTimeStyles.None, out var date)) return date;
        throw new InvalidDataException("A native date replacement requires a valid date.");
    }

    private static string CellText(WorkbookPart workbook, Cell cell)
    {
        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString == null ? "" : string.Concat(StringTextNodes(cell.InlineString).Select(t => t.Text));
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (!int.TryParse(cell.CellValue?.Text, out var index) || index < 0)
                throw new InvalidDataException("Invalid shared string index.");
            var shared = workbook.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(index)
                ?? throw new InvalidDataException("Missing shared string.");
            return string.Concat(StringTextNodes(shared).Select(t => t.Text));
        }
        return cell.CellValue?.Text ?? "";
    }

    private static bool Is1904(WorkbookPart workbook) => workbook.Workbook.WorkbookProperties?.Date1904?.Value == true;
    private static double ToSerial(DateTime date, bool date1904) => date1904
        ? (date - new DateTime(1904, 1, 1)).TotalDays
        : (date - new DateTime(1899, 12, 31)).TotalDays + (date >= new DateTime(1900, 3, 1) ? 1 : 0);

    private static bool TryNumericDate(WorkbookPart workbook, Cell cell, out DateTime date)
    {
        date = default;
        if (cell.DataType != null && cell.DataType.Value != CellValues.Number) return false;
        var styles = workbook.WorkbookStylesPart?.Stylesheet;
        var format = styles?.CellFormats?.Elements<CellFormat>().ElementAtOrDefault((int)(cell.StyleIndex?.Value ?? 0));
        uint id = format?.NumberFormatId?.Value ?? 0;
        if (!double.TryParse(cell.CellValue?.Text, NumberStyles.Float, Invariant, out var serial) || !double.IsFinite(serial)) return false;
        if (!IsDateFormat(id, styles, serial)) return false;
        // Excel's fictitious 1900-02-29 cannot be represented as a DateTime.
        if (!Is1904(workbook) && Math.Floor(serial) == 60) return false;
        try
        {
            date = Is1904(workbook) ? new DateTime(1904, 1, 1).AddDays(serial)
                : new DateTime(1899, 12, 31).AddDays(serial < 60 ? serial : serial - 1);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool IsDateFormat(uint id, Stylesheet? styles, double value)
    {
        if (id is >= 14 and <= 17 or 22 or >= 27 and <= 36 or >= 50 and <= 58) return true;
        string? code = styles?.NumberingFormats?.Elements<NumberingFormat>().FirstOrDefault(f => f.NumberFormatId?.Value == id)?.FormatCode?.Value;
        if (string.IsNullOrEmpty(code)) return false;
        var sections = new List<(string Tokens, string? Operator, double Threshold)>();
        var tokens = new System.Text.StringBuilder();
        string? condition = null;
        double threshold = 0;
        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c is '\\' or '_' or '*')
            {
                if (++i >= code.Length) return false;
                continue;
            }
            if (c == '"')
            {
                while (++i < code.Length && code[i] != '"') { }
                if (i >= code.Length) return false;
                continue;
            }
            if (c == '[')
            {
                int end = code.IndexOf(']', i + 1);
                if (end < 0) return false;
                string bracket = code[(i + 1)..end];
                var match = Regex.Match(bracket, @"^(<=|>=|<>|=|<|>)(.+)$");
                if (match.Success)
                {
                    if (condition != null || !double.TryParse(match.Groups[2].Value, NumberStyles.Float, Invariant, out threshold) || !double.IsFinite(threshold)) return false;
                    condition = match.Groups[1].Value;
                }
                else if (!(bracket.StartsWith('$') || Regex.IsMatch(bracket, @"^(Black|Blue|Cyan|Green|Magenta|Red|White|Yellow|Color\d+|[hms]+)$", RegexOptions.IgnoreCase))) return false;
                i = end;
                continue;
            }
            if (c == ';')
            {
                sections.Add((tokens.ToString(), condition, threshold));
                tokens.Clear(); condition = null; threshold = 0;
                continue;
            }
            tokens.Append(c);
        }
        sections.Add((tokens.ToString(), condition, threshold));
        if (sections.Count > 4) return false;
        string? active = null;
        var numericSections = sections.Take(3).ToList();
        if (numericSections.Any(s => s.Operator != null))
        {
            foreach (var section in numericSections)
            {
                bool applies = section.Operator switch
                {
                    null => true,
                    "<" => value < section.Threshold, "<=" => value <= section.Threshold,
                    ">" => value > section.Threshold, ">=" => value >= section.Threshold,
                    "=" => value == section.Threshold, "<>" => value != section.Threshold,
                    _ => false
                };
                if (applies) { active = section.Tokens; break; }
            }
        }
        else
        {
            int index = value < 0 && numericSections.Count >= 2 ? 1 : value == 0 && numericSections.Count >= 3 ? 2 : 0;
            active = numericSections[index].Tokens;
        }
        return active != null && (Regex.IsMatch(active, "[dy]", RegexOptions.IgnoreCase) ||
            (Regex.IsMatch(active, "m", RegexOptions.IgnoreCase) && !Regex.IsMatch(active, "[hs]", RegexOptions.IgnoreCase)));
    }
    private static int RowNumber(string address) => int.Parse(Regex.Match(address, "[0-9]+$").Value, Invariant);
    private static int ColumnNumber(string address)
    {
        int column = 0;
        foreach (var character in address.TakeWhile(char.IsLetter)) column = checked(column * 26 + char.ToUpperInvariant(character) - 'A' + 1);
        return column;
    }
}


using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ContractDateReplacer.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace ContractDateReplacer.Infrastructure.Services;

/// <summary>Recognizes the supplied attendance layout; edits a new package and verifies the allowed changes.</summary>
public sealed partial class AttendanceCalendarService : ContractDateReplacer.Core.Interfaces.IAttendanceCalendarService
{
    private static readonly Regex Period = new(@"(?i)\btháng\s*:?\s*(?<month>\d{1,2})\s*(?:/\s*|năm\s*:?\s*)(?<year>\d{4})(?!\d)", RegexOptions.Compiled);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public AttendanceAnalysis Analyze(string path)
    {
        var tables = new List<AttendanceTable>(); var errors = new List<string>();
        using var doc = SpreadsheetDocument.Open(path, false);
        var wb = doc.WorkbookPart ?? throw new InvalidDataException("Không tìm thấy workbook.");
        foreach (var sheet in wb.Workbook.Sheets!.Elements<Sheet>())
        {
            if (wb.GetPartById(sheet.Id!.Value!) is not WorksheetPart part) continue;
            var cells = Cells(part.Worksheet);
            foreach (var first in cells.Values.Where(c => Read(wb, c) == "1"))
            {
                var (col, row) = Position(first.CellReference!.Value!);
                int count = 0;
                while (count < 31 && Read(wb, Get(cells, col + count, row)) == (count + 1).ToString(Invariant)) count++;
                if (count < 28) continue;
                int totalColumn = col + count;
                while (totalColumn <= col + 31 && string.IsNullOrWhiteSpace(Read(wb, Get(cells, totalColumn, row)))) totalColumn++;
                if (totalColumn > col + 31 || !IsTotal(Read(wb, Get(cells, totalColumn, row)))) continue;
                var periodCells = cells.Values.Where(c => Position(c.CellReference!.Value!).Row < row && Period.IsMatch(Read(wb, c)))
                    .OrderByDescending(c => Position(c.CellReference!.Value!).Row).ToList();
                if (periodCells.Count == 0) continue;
                var periodCell = periodCells[0]; var match = Period.Match(Read(wb, periodCell));
                int year = int.Parse(match.Groups["year"].Value), month = int.Parse(match.Groups["month"].Value);
                if (year < 1900 || year > 2100 || month < 1 || month > 12 || DateTime.DaysInMonth(year, month) != count) continue;
                if (Enumerable.Range(1, count).Any(d => Read(wb, Get(cells, col + d - 1, row + 1)).Trim().ToUpperInvariant() != Weekday(new DateTime(year, month, d)))) continue;
                // The final employee is determined from STT + name, never from footer formatting.
                int sttColumn = Enumerable.Range(1, col - 1).FirstOrDefault(c => Read(wb, Get(cells, c, row)).Trim().Equals("STT", StringComparison.OrdinalIgnoreCase));
                int nameColumn = Enumerable.Range(1, col - 1).FirstOrDefault(c => Regex.IsMatch(Read(wb, Get(cells, c, row)).Trim(), @"^họ\s+và\s+tên$", RegexOptions.IgnoreCase));
                if (sttColumn == 0 || nameColumn == 0) continue;
                int firstEmployee = row + 2, lastEmployee = firstEmployee - 1, number = 1;
                for (int r = firstEmployee; ; r++)
                {
                    if (!double.TryParse(Read(wb, Get(cells, sttColumn, r)), NumberStyles.Float, Invariant, out double stt) || stt != number || string.IsNullOrWhiteSpace(Read(wb, Get(cells, nameColumn, r)))) break;
                    lastEmployee = r; number++;
                }
                if (lastEmployee < firstEmployee) continue;
                int nextHeader = cells.Values.Where(c => Position(c.CellReference!.Value!).Column == sttColumn && Position(c.CellReference.Value!).Row > row && Read(wb, c).Trim().Equals("STT", StringComparison.OrdinalIgnoreCase))
                    .Select(c => Position(c.CellReference!.Value!).Row).DefaultIfEmpty(int.MaxValue).Min();
                string role = Read(wb, Get(cells, col - 1, firstEmployee)).Trim();
                var interruptedWorker = cells.Values.Where(c => Position(c.CellReference!.Value!).Column == nameColumn && Position(c.CellReference.Value!).Row > lastEmployee && Position(c.CellReference.Value!).Row < nextHeader && !string.IsNullOrWhiteSpace(Read(wb, c)))
                    .FirstOrDefault(c =>
                    {
                        int r = Position(c.CellReference!.Value!).Row;
                        return double.TryParse(Read(wb, Get(cells, sttColumn, r)), NumberStyles.Float, Invariant, out _) ||
                            (role.Length > 0 && Read(wb, Get(cells, col - 1, r)).Trim().Equals(role, StringComparison.OrdinalIgnoreCase));
                    });
                if (interruptedWorker != null)
                {
                    errors.Add($"{sheet.Name}!{interruptedWorker.CellReference}: danh sách nhân viên bị gián đoạn hoặc thiếu STT/họ tên; không thể xác định cuối bảng an toàn.");
                    continue;
                }
                int totalRow = cells.Values.Where(c => Position(c.CellReference!.Value!).Row > lastEmployee && Position(c.CellReference!.Value!).Row <= lastEmployee + 2 && Position(c.CellReference!.Value!).Column < col && IsTotal(Read(wb, c)))
                    .Select(c => Position(c.CellReference!.Value!).Row).DefaultIfEmpty(0).Min();
                bool hasTotal = totalRow > 0;
                if (!hasTotal) totalRow = lastEmployee;
                string id = $"{sheet.Id!.Value}:{row}:{col}";
                tables.Add(new AttendanceTable(id, sheet.Name!.Value!, sheet.Id.Value!, row, col, firstEmployee, totalRow, totalColumn,
                    periodCell.CellReference!.Value!, year, month, hasTotal));
            }
        }
        if (tables.Count == 0) errors.Add("Không nhận diện được bảng: cần dòng tháng/năm, 28–31 ngày liên tiếp đúng lịch, hàng thứ, STT, Họ và tên và cột Tổng công hoặc Tổng cộng. Không bắt buộc hàng tổng cuối bảng.");
        return new AttendanceAnalysis(tables, errors);
    }

    public AttendancePreview Preview(string path, string tableId, int year, int month)
    {
        using var lockedSource = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lockedSource));
        var analysis = Analyze(path);
        var table = analysis.Tables.SingleOrDefault(t => t.Id == tableId);
        var errors = new List<string>();
        if (table == null) errors.AddRange(analysis.Errors.Count > 0 ? analysis.Errors : new[] { "Bảng đã chọn không còn tồn tại. Hãy chọn lại tệp." });
        if (year < 1900 || year > 2100 || month < 1 || month > 12) errors.Add("Chọn tháng 1–12 và năm 1900–2100.");
        if (errors.Count > 0) return new AttendancePreview(table, year, month, "", Array.Empty<AttendanceDay>(), errors);
        int length = DateTime.DaysInMonth(year, month);
        var days = Enumerable.Range(1, length).Select(d => new AttendanceDay(d, Weekday(new DateTime(year, month, d)), new DateTime(year, month, d).DayOfWeek == DayOfWeek.Sunday)).ToArray();
        using var doc = SpreadsheetDocument.Open(path, false); var wb = doc.WorkbookPart!;
        var ws = ((WorksheetPart)wb.GetPartById(table!.SheetRelationshipId)).Worksheet; var cells = Cells(ws);
        if (ws.GetFirstChild<SheetProtection>()?.Sheet?.Value == true) errors.Add($"{table.SheetName}: sheet được bảo vệ, không thể đổi lịch.");
        for (int d = length + 1; d <= table.DayColumnCount; d++)
            for (int row = table.FirstEmployeeRow; row <= table.TotalRow; row++)
            {
                var cell = Get(cells, table.FirstDayColumn + d - 1, row);
                if (cell != null && (cell.CellFormula != null || !string.IsNullOrWhiteSpace(Read(wb, cell))))
                    errors.Add($"{table.SheetName}!{cell.CellReference}: ngày {d} không có trong tháng {month:00}/{year}, vẫn có dữ liệu hoặc công thức.");
            }
        errors.AddRange(ExpansionErrors(wb, ws, table, cells));
        var periodUpdates = PeriodUpdates(wb, ws, table, year, month);
        // A formula in a calendar/title cell cannot be replaced silently.
        var calendarCells = cells.Values.Where(c =>
        {
            var p = Position(c.CellReference!.Value!);
            return periodUpdates.ContainsKey(c.CellReference!.Value!) || ((p.Row == table.DayRow || p.Row == table.DayRow + 1) && p.Column >= table.FirstDayColumn && p.Column < table.TotalColumn);
        });
        if (calendarCells.Any(c => c.CellFormula != null)) errors.Add($"{table.SheetName}: vùng lịch hoặc dòng thời gian chứa công thức, không hỗ trợ mẫu này.");
        foreach (var merge in ws.GetFirstChild<MergeCells>()?.Elements<MergeCell>() ?? Enumerable.Empty<MergeCell>())
        {
            var ends = merge.Reference!.Value!.Split(':'); var start = Position(ends[0]); var end = Position(ends[^1]);
            if (start.Row <= table.TotalRow && end.Row >= table.DayRow && start.Column < table.TotalColumn && end.Column >= table.FirstDayColumn)
                errors.Add($"{table.SheetName}!{merge.Reference}: vùng ngày có ô gộp, không hỗ trợ mẫu này.");
        }
        return new AttendancePreview(table, year, month,
            $"Thời gian: tháng {month:00}/{year}(từ 01/{month:00}/{year} đến {length:00}/{month:00}/{year})", days, errors, checksum);
    }

    public void Export(string source, string destination, string tableId, int year, int month, string? expectedSourceChecksum = null)
    {
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase) || File.Exists(destination))
            throw new IOException("Phải xuất ra tệp mới, khác tệp gốc và chưa tồn tại.");
        // Keep the source locked for writing between validation and verification.
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var preview = Preview(source, tableId, year, month);
        if (expectedSourceChecksum != null && expectedSourceChecksum != preview.SourceChecksum)
            throw new InvalidOperationException("Tệp nguồn đã thay đổi sau khi xem trước. Hãy xem trước lại.");
        if (!preview.CanExport) throw new InvalidOperationException(string.Join(Environment.NewLine, preview.Errors));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        bool created = false;
        try
        {
            Dictionary<string, string> periodUpdates;
            using (var periodSource = SpreadsheetDocument.Open(source, false))
            {
                var sourceWb = periodSource.WorkbookPart!;
                var sourceWs = ((WorksheetPart)sourceWb.GetPartById(preview.Table!.SheetRelationshipId)).Worksheet;
                periodUpdates = PeriodUpdates(sourceWb, sourceWs, preview.Table, year, month);
            }
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) { created = true; input.CopyTo(output); }
            string expectedWorksheet; string expectedStyles; string worksheetPart;
            using (var doc = SpreadsheetDocument.Open(destination, true))
            {
                var wb = doc.WorkbookPart!; var table = preview.Table!;
                var part = (WorksheetPart)wb.GetPartById(table.SheetRelationshipId); worksheetPart = part.Uri.OriginalString.TrimStart('/');
                Apply(wb, part.Worksheet, preview, periodUpdates);
                ResetCalculation(wb);
                expectedWorksheet = part.Worksheet.OuterXml;
                expectedStyles = wb.WorkbookStylesPart!.Stylesheet.OuterXml;
            }
            Verify(source, destination, preview, worksheetPart, expectedWorksheet, expectedStyles);
        }
        catch { if (created) File.Delete(destination); throw; }
    }

    private static void Apply(WorkbookPart wb, Worksheet ws, AttendancePreview preview, IReadOnlyDictionary<string, string> periodUpdates)
    {
        var table = NormalizeLayout(wb, ws, preview.Table!);
        var cells = Cells(ws); var styles = wb.WorkbookStylesPart?.Stylesheet
            ?? throw new InvalidDataException("Không tìm thấy định dạng bảng.");
        var formats = styles.CellFormats ?? throw new InvalidDataException("Không tìm thấy định dạng ô.");
        var fills = styles.Fills ?? throw new InvalidDataException("Không tìm thấy màu nền.");
        uint FillOf(Cell? c) => formats.Elements<CellFormat>().ElementAt((int)(c?.StyleIndex?.Value ?? 0)).FillId?.Value ?? 0;
        var oldSundays = Enumerable.Range(1, DateTime.DaysInMonth(table.SourceYear, table.SourceMonth))
            .Where(d => new DateTime(table.SourceYear, table.SourceMonth, d).DayOfWeek == DayOfWeek.Sunday).ToHashSet();
        var oldSundayFillsByRow = Enumerable.Range(table.DayRow, table.TotalRow - table.DayRow + 1)
            .ToDictionary(row => row, row => oldSundays.Select(d => FillOf(Get(cells, table.FirstDayColumn + d - 1, row))).Where(f => f != 0).ToHashSet());
        var green = new Fill(new PatternFill(new ForegroundColor { Rgb = "FF9BBB59" }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid });
        var greenIndex = fills.Elements<Fill>().Select((f, i) => (f, i)).FirstOrDefault(p => p.f.OuterXml == green.OuterXml);
        uint sundayFill = greenIndex.f != null ? (uint)greenIndex.i : (uint)fills.ChildElements.Count;
        if (greenIndex.f == null) fills.Append(green);
        fills.Count = (uint)fills.ChildElements.Count;
        var originalFormats = formats.Elements<CellFormat>().Select(f => (CellFormat)f.CloneNode(true)).ToList();
        static string WithoutFill(CellFormat f) { var copy = (CellFormat)f.CloneNode(true); copy.FillId = null; copy.ApplyFill = null; return copy.OuterXml; }
        var cache = new Dictionary<(uint Style, uint Fill), uint>();
        uint StyleWithFill(uint styleIndex, uint fill)
        {
            if (cache.TryGetValue((styleIndex, fill), out var index)) return index;
            var copy = (CellFormat)originalFormats[(int)styleIndex].CloneNode(true); copy.FillId = fill; copy.ApplyFill = true;
            var existing = formats.Elements<CellFormat>().Select((f, i) => (f, i)).FirstOrDefault(p => p.f.OuterXml == copy.OuterXml);
            index = existing.f != null ? (uint)existing.i : (uint)formats.ChildElements.Count;
            if (existing.f == null) formats.Append(copy);
            cache[(styleIndex, fill)] = index; return index;
        }
        for (int row = table.DayRow; row <= table.TotalRow; row++)
            for (int day = 1; day <= 31; day++)
            {
                var cell = Ensure(ws, cells, table.FirstDayColumn + day - 1, row);
                uint style = cell.StyleIndex?.Value ?? 0, oldFill = FillOf(cell), baseline = oldFill;
                if (oldSundayFillsByRow[row].Contains(oldFill))
                {
                    // Change only the fill; preserve each target cell's own borders, font, alignment and number format.
                    string shape = WithoutFill(originalFormats[(int)style]);
                    var normal = originalFormats.FirstOrDefault(f => !oldSundayFillsByRow[row].Contains(f.FillId?.Value ?? 0) && WithoutFill(f) == shape);
                    baseline = normal?.FillId?.Value ?? 0;
                }
                bool sunday = day <= preview.Days.Count && preview.Days[day - 1].IsSunday;
                uint desiredFill = sunday ? sundayFill : baseline;
                if (desiredFill != oldFill) cell.StyleIndex = StyleWithFill(style, desiredFill);
                if (row == table.DayRow) SetText(cell, day <= preview.Days.Count ? day.ToString(Invariant) : "", numeric: day <= preview.Days.Count);
                else if (row == table.DayRow + 1) SetText(cell, day <= preview.Days.Count ? preview.Days[day - 1].Weekday : "");
            }
        formats.Count = (uint)formats.ChildElements.Count;
        foreach (var edit in periodUpdates) SetText(cells[edit.Key], edit.Value);
        if (!table.HasTotalRow) return;
        var total = Ensure(ws, cells, table.TotalColumn, table.TotalRow);
        string first = Address(table.TotalColumn, table.FirstEmployeeRow), last = Address(table.TotalColumn, table.TotalRow - 1);
        total.RemoveAllChildren(); total.DataType = null;
        total.Append(new CellFormula($"SUM({first}:{last})"));
        // Attendance and employee formulas remain unchanged, so their existing numeric caches can be summed safely.
        double sum = 0; bool cached = true;
        for (int row = table.FirstEmployeeRow; row < table.TotalRow; row++)
        {
            var c = Get(cells, table.TotalColumn, row);
            if (c == null || string.IsNullOrWhiteSpace(c.InnerText)) continue;
            if (double.TryParse(c.CellValue?.Text, NumberStyles.Float, Invariant, out double value)) sum += value; else cached = false;
        }
        if (cached) total.Append(new CellValue(sum.ToString("R", Invariant)));
    }

    private static void Verify(string source, string output, AttendancePreview preview, string targetPart, string expectedWorksheet, string expectedStyles)
    {
        string? removedChain = null, removedChainRelationship = null;
        string workbookPath = "xl/workbook.xml", workbookRelations = "xl/_rels/workbook.xml.rels";
        using (var doc = SpreadsheetDocument.Open(output, false))
        {
            var wb = doc.WorkbookPart!;
            var ws = ((WorksheetPart)wb.GetPartById(preview.Table!.SheetRelationshipId)).Worksheet;
            if (!EquivalentXml(ws.OuterXml, expectedWorksheet) || !EquivalentXml(wb.WorkbookStylesPart!.Stylesheet.OuterXml, expectedStyles))
                throw new InvalidDataException("Xác minh thất bại: lịch hoặc định dạng sau khi lưu không khớp.");
            using var original = SpreadsheetDocument.Open(source, false);
            var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
            var baseline = validator.Validate(original).Select(e => e.Description + "|" + e.Path?.XPath).ToHashSet();
            var addedErrors = validator.Validate(doc).Where(e => !baseline.Contains(e.Description + "|" + e.Path?.XPath)).Take(5).ToList();
            if (addedErrors.Count > 0) throw new InvalidDataException("Lỗi OpenXML: " + string.Join("; ", addedErrors.Select(e => e.Description)));
            // Independently verify preserved employee contents/formulas and non-fill formatting in the changed range.
            var originalWb = original.WorkbookPart!;
            if (wb.CalculationChainPart != null || !EquivalentXml(wb.Workbook.OuterXml, ExpectedCalculationWorkbook(originalWb.Workbook).OuterXml))
                throw new InvalidDataException("Siêu dữ liệu tính toán hoặc workbook không khớp.");
            workbookPath = originalWb.Uri.OriginalString.TrimStart('/');
            int slash = workbookPath.LastIndexOf('/');
            workbookRelations = workbookPath[..(slash + 1)] + "_rels/" + workbookPath[(slash + 1)..] + ".rels";
            if (originalWb.CalculationChainPart is { } oldChain)
            {
                removedChain = oldChain.Uri.OriginalString.TrimStart('/');
                removedChainRelationship = originalWb.GetIdOfPart(oldChain);
            }
            var originalWs = ((WorksheetPart)originalWb.GetPartById(preview.Table.SheetRelationshipId)).Worksheet;
            var originalCells = Cells(originalWs); var actualCells = Cells(ws); var t = preview.Table;
            var periodUpdates = PeriodUpdates(originalWb, originalWs, t, preview.Year, preview.Month);
            foreach (var edit in periodUpdates)
                if (!actualCells.TryGetValue(edit.Key, out var periodCell) || Read(wb, periodCell) != edit.Value)
                    throw new InvalidDataException("Dòng thời gian không khớp tại " + edit.Key);
            foreach (var pair in originalCells)
            {
                var p = Position(pair.Key);
                bool inCalendar = p.Column >= t.FirstDayColumn && p.Column < t.TotalColumn && p.Row >= t.DayRow && p.Row <= t.TotalRow;
                bool editableText = periodUpdates.ContainsKey(pair.Key) || (t.HasTotalRow && p.Row == t.TotalRow && p.Column == t.TotalColumn) || (inCalendar && p.Row <= t.DayRow + 1);
                if (editableText) continue;
                if (t.DayColumnCount < 31 && p.Row >= t.DayRow && p.Row <= t.TotalRow && p.Column > t.TotalColumn + 1 && p.Column <= t.FirstDayColumn + 32)
                {
                    // These formerly blank cells become the shifted total/notes columns.
                    if (pair.Value.CellFormula != null || !string.IsNullOrWhiteSpace(Read(originalWb, pair.Value)))
                        throw new InvalidDataException("Vị trí mở rộng không trống: " + pair.Key);
                    continue;
                }
                string actualAddress = MappedAddress(pair.Key, t);
                if (!actualCells.TryGetValue(actualAddress, out var actual)) throw new InvalidDataException("Mất ô " + pair.Key);
                var before = (Cell)pair.Value.CloneNode(true); var after = (Cell)actual.CloneNode(true);
                before.CellReference = actualAddress;
                if (before.CellFormula != null && p.Row >= t.DayRow && p.Row <= t.TotalRow && p.Column >= t.FirstDayColumn && p.Column <= t.TotalColumn + 1)
                {
                    before.CellFormula.Text = TranslateFormula(before.CellFormula.Text, t);
                    if (before.CellFormula.Reference?.Value is { } reference) before.CellFormula.Reference = MappedRange(reference, t);
                }
                if (inCalendar) { before.StyleIndex = null; after.StyleIndex = null; }
                if (!EquivalentXml(before.OuterXml, after.OuterXml)) throw new InvalidDataException("Thay đổi dữ liệu ngoài phạm vi tại " + pair.Key);
            }
        }
        using var originalZip = ZipFile.OpenRead(source); using var outputZip = ZipFile.OpenRead(output);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { targetPart, "xl/styles.xml", workbookPath };
        if (!originalZip.Entries.Select(e => e.FullName).Where(name => name != removedChain).Order().SequenceEqual(outputZip.Entries.Select(e => e.FullName).Order()))
            throw new InvalidDataException("Cấu trúc gói Excel bị thay đổi.");
        foreach (var entry in originalZip.Entries.Where(e => !allowed.Contains(e.FullName) && e.FullName != removedChain))
        {
            using var a = entry.Open(); using var b = outputZip.GetEntry(entry.FullName)!.Open();
            using var aa = new MemoryStream(); using var bb = new MemoryStream(); a.CopyTo(aa); b.CopyTo(bb);
            if (aa.ToArray().SequenceEqual(bb.ToArray())) continue;
            if (removedChain != null && (entry.FullName == "[Content_Types].xml" || entry.FullName == workbookRelations))
            {
                var expected = XDocument.Load(new MemoryStream(aa.ToArray()));
                if (entry.FullName == "[Content_Types].xml")
                    expected.Root!.Elements().Where(e => e.Attribute("PartName")?.Value == "/" + removedChain).Remove();
                else expected.Root!.Elements().Where(e => e.Attribute("Id")?.Value == removedChainRelationship).Remove();
                if (!EquivalentXml(expected.ToString(SaveOptions.DisableFormatting), XDocument.Load(new MemoryStream(bb.ToArray())).ToString(SaveOptions.DisableFormatting)))
                    throw new InvalidDataException("Siêu dữ liệu gói bị thay đổi ngoài việc xóa calcChain: " + entry.FullName);
                continue;
            }
            if ((entry.FullName.EndsWith(".xml", StringComparison.Ordinal) || entry.FullName.EndsWith(".rels", StringComparison.Ordinal)) &&
                XNode.DeepEquals(XDocument.Load(new MemoryStream(aa.ToArray())), XDocument.Load(new MemoryStream(bb.ToArray())))) continue;
            throw new InvalidDataException("Nội dung ngoài phạm vi bị thay đổi: " + entry.FullName);
        }
    }

    private static bool EquivalentXml(string left, string right)
    {
        // Prefix declarations can move or be duplicated when Excel's default namespace is reserialized.
        // Parsed element/attribute XNames retain their namespace URIs after removing declarations.
        var a = XDocument.Parse(left); var b = XDocument.Parse(right);
        foreach (var document in new[] { a, b })
            foreach (var element in document.Descendants())
            {
                var attributes = element.Attributes().Where(x => !x.IsNamespaceDeclaration)
                    .OrderBy(x => x.Name.ToString(), StringComparer.Ordinal).Select(x => new XAttribute(x)).ToArray();
                element.RemoveAttributes(); element.Add(attributes);
            }
        return XNode.DeepEquals(a, b);
    }

    private static bool IsTotal(string text) => Regex.IsMatch(text.Trim(), @"^tổng\s+(?:công|cộng)$", RegexOptions.IgnoreCase);
    private static string Weekday(DateTime date) => date.DayOfWeek == DayOfWeek.Sunday ? "CN" : "T" + ((int)date.DayOfWeek + 1);
    private static Dictionary<string, Cell> Cells(Worksheet ws) => ws.Descendants<Cell>().Where(c => c.CellReference?.Value != null).ToDictionary(c => c.CellReference!.Value!, StringComparer.OrdinalIgnoreCase);
    private static Cell? Get(Dictionary<string, Cell> cells, int col, int row) => cells.GetValueOrDefault(Address(col, row));
    private static string Address(int col, int row) { string s = ""; while (col > 0) { col--; s = (char)('A' + col % 26) + s; col /= 26; } return s + row.ToString(Invariant); }
    private static (int Column, int Row) Position(string address)
    {
        int col = 0, index = 0; while (index < address.Length && char.IsLetter(address[index])) col = col * 26 + char.ToUpperInvariant(address[index++]) - 'A' + 1;
        return (col, int.Parse(address[index..], Invariant));
    }
    private static string Read(WorkbookPart wb, Cell? cell)
    {
        if (cell == null) return "";
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(cell.CellValue?.Text, out int index))
            return wb.SharedStringTablePart!.SharedStringTable.Elements<SharedStringItem>().ElementAt(index).InnerText;
        return cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? "";
    }
    private static Cell Ensure(Worksheet ws, Dictionary<string, Cell> cells, int col, int row)
    {
        string address = Address(col, row); if (cells.TryGetValue(address, out var cell)) return cell;
        var data = ws.GetFirstChild<SheetData>()!; var r = data.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == (uint)row);
        if (r == null) { r = new Row { RowIndex = (uint)row }; data.InsertBefore(r, data.Elements<Row>().FirstOrDefault(r => r.RowIndex!.Value > row)); }
        cell = new Cell { CellReference = address };
        r.InsertBefore(cell, r.Elements<Cell>().FirstOrDefault(c => Position(c.CellReference!.Value!).Column > col)); cells.Add(address, cell); return cell;
    }
    private static void SetText(Cell cell, string text, bool numeric = false)
    {
        cell.RemoveAllChildren(); cell.DataType = null;
        if (text.Length == 0) return;
        if (numeric) cell.CellValue = new CellValue(text);
        else { cell.DataType = CellValues.InlineString; cell.InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }); }
    }
}

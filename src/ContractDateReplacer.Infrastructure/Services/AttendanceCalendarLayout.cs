using System.Globalization;
using System.Text.RegularExpressions;
using ContractDateReplacer.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ContractDateReplacer.Infrastructure.Services;

public sealed partial class AttendanceCalendarService
{
    private static AttendanceTable Expanded(AttendanceTable table) => table with { TotalColumn = table.FirstDayColumn + 31 };
    private static string MappedAddress(string address, AttendanceTable table)
    {
        var p = Position(address); int delta = 31 - table.DayColumnCount;
        return delta > 0 && p.Row >= table.DayRow && p.Row <= table.TotalRow && p.Column >= table.TotalColumn && p.Column <= table.TotalColumn + 1
            ? Address(p.Column + delta, p.Row) : address;
    }
    private static IEnumerable<string> ExpansionErrors(WorkbookPart wb, Worksheet ws, AttendanceTable table, Dictionary<string, Cell> cells)
    {
        int delta = 31 - table.DayColumnCount; if (delta <= 0) yield break;
        if (!Regex.IsMatch(Read(wb, Get(cells, table.TotalColumn + 1, table.DayRow)).Trim(), @"^ghi\s+chú$", RegexOptions.IgnoreCase))
            yield return $"{table.SheetName}: chưa xác định được cột Ghi chú để thêm cột ngày an toàn.";
        foreach (var cell in cells.Values)
        {
            var p = Position(cell.CellReference!.Value!);
            if (p.Row < table.DayRow || p.Row > table.TotalRow) continue;
            if (p.Column > table.TotalColumn + 1 && p.Column <= table.TotalColumn + delta + 1 && (cell.CellFormula != null || !string.IsNullOrWhiteSpace(Read(wb, cell))))
                yield return $"{table.SheetName}!{cell.CellReference}: cần thêm {delta} cột ngày nhưng vị trí này đã có dữ liệu.";
            if (cell.CellFormula != null && cell.CellFormula.FormulaType?.Value is { } type && type != CellFormulaValues.Normal && type != CellFormulaValues.Shared)
                yield return $"{table.SheetName}!{cell.CellReference}: không thể mở rộng vùng có công thức mảng hoặc bảng dữ liệu.";
        }
        foreach (var merge in ws.GetFirstChild<MergeCells>()?.Elements<MergeCell>() ?? Enumerable.Empty<MergeCell>())
        {
            var ends = merge.Reference!.Value!.Split(':'); var a = Position(ends[0]); var b = Position(ends[^1]);
            bool intersects = a.Row <= table.TotalRow && b.Row >= table.DayRow && a.Column <= table.TotalColumn + delta + 1 && b.Column >= table.TotalColumn;
            bool movable = a.Row >= table.DayRow && b.Row <= table.TotalRow && a.Column >= table.TotalColumn && b.Column <= table.TotalColumn + 1;
            if (intersects && !movable) yield return $"{table.SheetName}!{merge.Reference}: ô gộp cắt ngang vùng mở rộng cột ngày, cần xử lý trước khi xuất.";
        }
        bool InTable(Cell c) { var p = Position(c.CellReference!.Value!); return p.Row >= table.DayRow && p.Row <= table.TotalRow && p.Column >= table.FirstDayColumn && p.Column <= table.TotalColumn + 1; }
        foreach (var group in cells.Values.Where(c => c.CellFormula?.FormulaType?.Value == CellFormulaValues.Shared).GroupBy(c => c.CellFormula!.SharedIndex?.Value))
        {
            if (!group.Any(InTable)) continue;
            var masters = group.Where(c => !string.IsNullOrWhiteSpace(c.CellFormula!.Text)).ToList();
            bool valid = group.Key.HasValue && masters.Count == 1;
            if (valid)
            {
                var master = masters[0];
                valid = group.All(InTable) && group.Select(c => Position(MappedAddress(c.CellReference!.Value!, table)).Column - Position(c.CellReference.Value!).Column).Distinct().Count() == 1;
                if (master.CellFormula!.Reference?.Value is { } range)
                {
                    var ends = range.Split(':').Select(Position).ToArray();
                    valid &= ends.All(p => p.Row >= table.DayRow && p.Row <= table.TotalRow && p.Column >= table.FirstDayColumn && p.Column <= table.TotalColumn + 1);
                    valid &= ends.Select(p => Position(MappedAddress(Address(p.Column, p.Row), table)).Column - p.Column).Distinct().Count() == 1;
                }
                else valid = false;
            }
            if (!valid) yield return $"{table.SheetName}!{group.First().CellReference}: nhóm công thức dùng chung vượt ranh giới vùng được dời, không thể mở rộng an toàn.";
        }
        foreach (var sheet in wb.Workbook.Sheets!.Elements<Sheet>())
        {
            if (wb.GetPartById(sheet.Id!.Value!) is not WorksheetPart part) continue;
            foreach (var c in part.Worksheet.Descendants<Cell>().Where(c => c.CellFormula != null))
            {
                if (sheet.Id.Value == table.SheetRelationshipId && InTable(c)) continue;
                if (ReferencesMovedArea(c.CellFormula!.Text, table, sheet.Name!.Value!))
                    yield return $"{sheet.Name}!{c.CellReference}: công thức ngoài bảng tham chiếu cột tổng/ghi chú sẽ được dời; cần xử lý trước khi mở rộng lịch.";
            }
        }
        foreach (var name in wb.Workbook.DefinedNames?.Elements<DefinedName>() ?? Enumerable.Empty<DefinedName>())
        {
            if (name.Name?.Value is "_xlnm.Print_Area" or "_xlnm.Print_Titles") continue;
            if (ReferencesMovedArea(name.Text, table, "")) yield return $"Vùng tên {name.Name}: tham chiếu cột tổng/ghi chú sẽ được dời, cần xử lý trước khi xuất.";
        }
    }
    private static AttendanceTable NormalizeLayout(WorkbookPart wb, Worksheet ws, AttendanceTable table)
    {
        int delta = 31 - table.DayColumnCount; if (delta <= 0) return table;
        var cells = Cells(ws); int oldTotal = table.TotalColumn, lastDay = oldTotal - 1;
        foreach (var row in ws.GetFirstChild<SheetData>()!.Elements<Row>().Where(r => r.RowIndex!.Value >= table.DayRow && r.RowIndex.Value <= table.TotalRow))
        {
            int r = (int)row.RowIndex!.Value;
            var moved = row.Elements<Cell>().Where(c => Position(c.CellReference!.Value!).Column >= oldTotal && Position(c.CellReference.Value!).Column <= oldTotal + 1)
                .Select(c => (Cell)c.CloneNode(true)).ToList();
            var dayStyle = Get(cells, lastDay, r);
            foreach (var c in row.Elements<Cell>().Where(c => Position(c.CellReference!.Value!).Column >= oldTotal && Position(c.CellReference.Value!).Column <= oldTotal + delta + 1).ToList()) c.Remove();
            for (int col = oldTotal; col < oldTotal + delta; col++)
            {
                var blank = dayStyle == null ? new Cell() : (Cell)dayStyle.CloneNode(true);
                blank.RemoveAllChildren(); blank.DataType = null; blank.CellReference = Address(col, r); Insert(row, blank);
            }
            foreach (var c in moved) { c.CellReference = Address(Position(c.CellReference!.Value!).Column + delta, r); Insert(row, c); }
            foreach (var c in row.Elements<Cell>().Where(c => c.CellFormula != null && Position(c.CellReference!.Value!).Column >= table.FirstDayColumn && Position(c.CellReference.Value!).Column <= oldTotal + delta + 1))
            {
                c.CellFormula!.Text = TranslateFormula(c.CellFormula.Text, table);
                if (c.CellFormula.Reference?.Value is { } reference) c.CellFormula.Reference = MappedRange(reference, table);
            }
        }
        // Move only table header merges; signature blocks and footer cells retain their addresses.
        foreach (var merge in ws.GetFirstChild<MergeCells>()?.Elements<MergeCell>() ?? Enumerable.Empty<MergeCell>())
        {
            var ends = merge.Reference!.Value!.Split(':'); var a = Position(ends[0]); var b = Position(ends[^1]);
            if (a.Row >= table.DayRow && b.Row <= table.TotalRow && a.Column >= oldTotal && b.Column <= oldTotal + 1)
                merge.Reference = $"{Address(a.Column + delta, a.Row)}:{Address(b.Column + delta, b.Row)}";
            else if (b.Row < table.DayRow && a.Column <= table.FirstDayColumn && b.Column == oldTotal + 1)
                merge.Reference = $"{Address(a.Column, a.Row)}:{Address(b.Column + delta, b.Row)}";
        }
        // Split width ranges rather than shifting entire worksheet columns (which would move signatures/drawings).
        var columns = ws.GetFirstChild<Columns>();
        if (columns == null) { columns = new Columns(); ws.InsertBefore(columns, ws.GetFirstChild<SheetData>()); }
        var originals = columns.Elements<Column>().Select(c => (Column)c.CloneNode(true)).ToList();
        Column ColumnAt(int col) => originals.FirstOrDefault(c => c.Min!.Value <= col && c.Max!.Value >= col) is { } found
            ? (Column)found.CloneNode(true) : new Column { Width = ws.SheetFormatProperties?.DefaultColumnWidth?.Value ?? 8.43, CustomWidth = true };
        var dayWidth = ColumnAt(lastDay); var totalWidth = ColumnAt(oldTotal); var noteWidth = ColumnAt(oldTotal + 1);
        columns.RemoveAllChildren();
        foreach (var c in originals)
        {
            if (c.Max!.Value < oldTotal || c.Min!.Value > oldTotal + delta + 1) { columns.Append(c); continue; }
            if (c.Min!.Value < oldTotal) { var left = (Column)c.CloneNode(true); left.Max = (uint)(oldTotal - 1); columns.Append(left); }
            if (c.Max!.Value > oldTotal + delta + 1) { var right = (Column)c.CloneNode(true); right.Min = (uint)(oldTotal + delta + 2); columns.Append(right); }
        }
        for (int col = oldTotal; col < oldTotal + delta; col++) { var c = (Column)dayWidth.CloneNode(true); c.Min = c.Max = (uint)col; columns.Append(c); }
        totalWidth.Min = totalWidth.Max = (uint)(oldTotal + delta); columns.Append(totalWidth);
        noteWidth.Min = noteWidth.Max = (uint)(oldTotal + delta + 1); columns.Append(noteWidth);
        var sorted = columns.Elements<Column>().OrderBy(c => c.Min!.Value).ToList(); columns.RemoveAllChildren(); columns.Append(sorted);
        return Expanded(table);
    }
    private static string MappedRange(string reference, AttendanceTable table) => string.Join(":", reference.Split(':').Select(a => MappedAddress(a, table)));
    private static void Insert(Row row, Cell cell) => row.InsertBefore(cell, row.Elements<Cell>().FirstOrDefault(c => Position(c.CellReference!.Value!).Column > Position(cell.CellReference!.Value!).Column));

    private static readonly Regex FormulaReference = new(@"(?<![\w.!])(?:(?<book>\[[^\]]+\])?(?<sheet>'(?:[^']|'')+'|[\p{L}_][\p{L}\p{N}_.]*)!)?(?<a>\$?[A-Z]{1,3}\$?\d+)(?::(?<b>\$?[A-Z]{1,3}\$?\d+))?(?![\w])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static bool ReferencesMovedArea(string formula, AttendanceTable table, string currentSheet)
    {
        foreach (var chunk in Regex.Split(formula, "(\"(?:[^\"]|\"\")*\")").Where((_, i) => i % 2 == 0))
            foreach (Match match in FormulaReference.Matches(chunk))
            {
                if (match.Groups["book"].Success) continue;
                string qualifier = match.Groups["sheet"].Value;
                string sheet = qualifier.Length == 0 ? currentSheet : qualifier.Trim('\'').Replace("''", "'");
                if (!sheet.Equals(table.SheetName, StringComparison.OrdinalIgnoreCase)) continue;
                var a = Position(match.Groups["a"].Value.Replace("$", ""));
                var b = match.Groups["b"].Success ? Position(match.Groups["b"].Value.Replace("$", "")) : a;
                if (a.Column <= table.TotalColumn + 1 && b.Column >= table.TotalColumn && a.Row <= table.TotalRow && b.Row >= table.DayRow) return true;
            }
        return false;
    }
    private static string TranslateFormula(string formula, AttendanceTable table)
    {
        int delta = 31 - table.DayColumnCount; if (delta <= 0) return formula;
        string Reference(string value, bool extendDayRange)
        {
            var p = Position(value.Replace("$", "")); int col = p.Column;
            if (p.Row >= table.DayRow && p.Row <= table.TotalRow && col >= table.TotalColumn && col <= table.TotalColumn + 1) col += delta;
            else if (extendDayRange && p.Row >= table.FirstEmployeeRow && p.Row <= table.LastEmployeeRow && col == table.TotalColumn - 1) col += delta;
            bool absoluteCol = value.StartsWith('$'); bool absoluteRow = value[1..].Contains('$');
            var address = Address(col, p.Row); int i = 0; while (char.IsLetter(address[i])) i++;
            return (absoluteCol ? "$" : "") + address[..i] + (absoluteRow ? "$" : "") + address[i..];
        }
        // Excel string literals must not be treated as references.
        var chunks = Regex.Split(formula, "(\"(?:[^\"]|\"\")*\")");
        for (int i = 0; i < chunks.Length; i += 2)
            chunks[i] = FormulaReference.Replace(chunks[i], match =>
            {
                string qualifier = match.Groups["sheet"].Value;
                if (match.Groups["book"].Success) return match.Value;
                if (qualifier.Length > 0 && !qualifier.Trim('\'').Replace("''", "'").Equals(table.SheetName, StringComparison.OrdinalIgnoreCase)) return match.Value;
                string a = match.Groups["a"].Value, b = match.Groups["b"].Value;
                bool extend = b.Length > 0 && Position(a.Replace("$", "")).Column < table.TotalColumn;
                return (qualifier.Length > 0 ? qualifier + "!" : "") + Reference(a, false) + (b.Length > 0 ? ":" + Reference(b, extend) : "");
            });
        return string.Concat(chunks);
    }
}

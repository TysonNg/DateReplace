using ContractDateReplacer.Infrastructure.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
namespace ContractDateReplacer.Core.Tests;

public sealed class CompactAttendanceCalendarTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "CompactAttendance_" + Guid.NewGuid().ToString("N"));
    public CompactAttendanceCalendarTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);
    private static string Col(int n) { string s = ""; while (n > 0) { n--; s = (char)('A' + n % 26) + s; n /= 26; } return s; }
    private static Cell Text(string a, string value) => new(new InlineString(new Text(value))) { CellReference = a, DataType = CellValues.InlineString };
    internal string Create(int year, int month, bool totalRow = false)
    {
        string path = Path.Combine(dir, Guid.NewGuid() + ".xlsx"); int length = DateTime.DaysInMonth(year, month); string total = Col(4 + length), note = Col(5 + length), last = Col(3 + length);
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart(); wb.Workbook = new Workbook(new Sheets());
        wb.AddNewPart<WorkbookStylesPart>().Stylesheet = new Stylesheet(new Fonts(new Font()), new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })), new Borders(new Border()), new CellStyleFormats(new CellFormat()), new CellFormats(new CellFormat()));
        var part = wb.AddNewPart<WorksheetPart>(); var data = new SheetData();
        part.Worksheet = new Worksheet(new Columns(new Column { Min = 4, Max = (uint)(length + 3), Width = 3, CustomWidth = true }, new Column { Min = (uint)(length + 4), Max = (uint)(length + 4), Width = 12, CustomWidth = true }, new Column { Min = (uint)(length + 5), Max = (uint)(length + 5), Width = 9, CustomWidth = true }), data);
        wb.Workbook.Sheets!.Append(new Sheet { Id = wb.GetIdOfPart(part), SheetId = 1, Name = "NSG" });
        data.Append(new Row(Text("A3", $"Thời gian: tháng {month:00}/{year} (từ 01/{month:00}/{year} đến {length:00}/{month:00}/{year})")) { RowIndex = 3 });
        var days = new Row { RowIndex = 5 }; var weekdays = new Row { RowIndex = 6 };
        days.Append(Text("A5", "STT"), Text("B5", "Họ và tên"), Text("C5", "Hạng mục"));
        for (int d = 1; d <= length; d++)
        {
            days.Append(new Cell(new CellValue(d)) { CellReference = Col(d + 3) + "5" });
            var dow = new DateTime(year, month, d).DayOfWeek; weekdays.Append(Text(Col(d + 3) + "6", dow == DayOfWeek.Sunday ? "CN" : "T" + ((int)dow + 1)));
        }
        days.Append(Text(total + "5", "Tổng cộng"), Text(note + "5", "Ghi chú")); data.Append(days, weekdays);
        for (int r = 7; r <= 8; r++)
        {
            var person = new Row { RowIndex = (uint)r }; person.Append(Text("A" + r, (r - 6).ToString()), Text("B" + r, "Nhân viên " + r), Text("C" + r, "BẢO VỆ"));
            for (int d = 1; d <= length; d++)
            {
                var c = new Cell { CellReference = Col(d + 3) + r }; if (d == 1 || d == length) c.CellValue = new CellValue(1); person.Append(c);
            }
            person.Append(new Cell(new CellFormula($"SUM(D{r}:{last}{r})"), new CellValue(2)) { CellReference = total + r }, Text(note + r, "Ghi chú " + r)); data.Append(person);
        }
        if (totalRow) data.Append(new Row(Text("A9", "TỔNG CỘNG"), new Cell(new CellValue(4)) { CellReference = total + "9" }) { RowIndex = 9 });
        else data.Append(new Row { RowIndex = 9 });
        data.Append(new Row(Text("B10", "BAN QUẢN LÝ"), Text("W10", "Đơn vị bảo vệ")) { RowIndex = 10 });
        part.Worksheet.Append(new MergeCells(new MergeCell { Reference = $"A3:{note}3" }, new MergeCell { Reference = $"{total}5:{total}6" }, new MergeCell { Reference = $"{note}5:{note}6" }, new MergeCell { Reference = "W10:AJ10" }));
        var other = wb.AddNewPart<WorksheetPart>(); other.Worksheet = new Worksheet(new SheetData(new Row(Text("A1", "Không thay đổi")) { RowIndex = 1 }));
        wb.Workbook.Sheets.Append(new Sheet { Id = wb.GetIdOfPart(other), SheetId = 2, Name = "Other" });
        return path;
    }
    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 9, 30)]
    [InlineData(2026, 10, 31)]
    public void RecognizeAndExport_CompactWithoutTotalRow(int year, int month, int length)
    {
        string source = Create(year, month); byte[] before = File.ReadAllBytes(source);
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.Equal(length + 4, table.TotalColumn); Assert.Equal(8, table.TotalRow);
        string output = Path.Combine(dir, "out.xlsx"); var preview = service.Preview(source, table.Id, 2026, 10); Assert.True(preview.CanExport, string.Join(";", preview.Errors));
        service.Export(source, output, table.Id, 2026, 10, preview.SourceChecksum);
        Assert.Equal(before, File.ReadAllBytes(source));
        using var result = SpreadsheetDocument.Open(output, false); var ws = result.WorkbookPart!.WorksheetParts.First().Worksheet;
        Cell At(string address) => ws.Descendants<Cell>().Single(c => c.CellReference == address);
        Assert.Equal("31", At("AH5").InnerText); Assert.Equal("Tổng cộng", At("AI5").InnerText); Assert.Equal("Ghi chú", At("AJ5").InnerText);
        Assert.Equal("SUM(D7:AH7)", At("AI7").CellFormula!.Text); Assert.Equal("SUM(D8:AH8)", At("AI8").CellFormula!.Text);
        Assert.Equal("Ghi chú 7", At("AJ7").InnerText); Assert.Equal("Ghi chú 8", At("AJ8").InnerText);
        Assert.Equal(length == 31 ? "1" : "", At("AH7").InnerText); Assert.Equal("1", At(Col(length + 3) + "7").InnerText);
        Assert.Equal("BAN QUẢN LÝ", At("B10").InnerText); Assert.Equal("Đơn vị bảo vệ", At("W10").InnerText);
        Assert.Contains(ws.GetFirstChild<MergeCells>()!.Elements<MergeCell>(), m => m.Reference == "AI5:AI6");
        Assert.Contains(ws.GetFirstChild<MergeCells>()!.Elements<MergeCell>(), m => m.Reference == "AJ5:AJ6");
        Assert.Contains(ws.GetFirstChild<MergeCells>()!.Elements<MergeCell>(), m => m.Reference == "W10:AJ10");
        var columns = ws.GetFirstChild<Columns>()!.Elements<Column>();
        Assert.Equal(3, columns.Single(c => c.Min!.Value <= 34 && c.Max!.Value >= 34).Width!.Value);
        Assert.Equal(12, columns.Single(c => c.Min!.Value <= 35 && c.Max!.Value >= 35).Width!.Value);
        Assert.Equal(9, columns.Single(c => c.Min!.Value <= 36 && c.Max!.Value >= 36).Width!.Value);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator(FileFormatVersions.Office2013).Validate(result));
        Assert.Single(service.Analyze(output).Tables);
    }
    [Fact]
    public void CompactWithTotalRow_PreservesAndMovesGrandTotal()
    {
        string source = Create(2026, 9, true); var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.Equal(9, table.TotalRow); string output = Path.Combine(dir, "total.xlsx"); service.Export(source, output, table.Id, 2026, 10);
        using var doc = SpreadsheetDocument.Open(output, false);
        Assert.Equal("SUM(AI7:AI8)", doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "AI9").CellFormula!.Text);
    }
    [Fact]
    public void ShortMonth_DoesNotMistakeTotalColumnForDay31()
    {
        string source = Create(2026, 9); var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.True(service.Preview(source, table.Id, 2026, 11).CanExport);
        var preview = service.Preview(source, table.Id, 2027, 2);
        Assert.Contains(preview.Errors, e => e.Contains("NSG!AG7"));
        Assert.DoesNotContain(preview.Errors, e => e.Contains("NSG!AH7"));
    }
    [Fact]
    public void SharedTotals_KeepGroupAndExtendRangesWhenExpanded()
    {
        string source = Create(2026, 9);
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var ws = doc.WorkbookPart!.WorksheetParts.First().Worksheet;
            ws.Descendants<Cell>().Single(c => c.CellReference == "AH7").CellFormula = new CellFormula("SUM(D7:AG7)") { FormulaType = CellFormulaValues.Shared, SharedIndex = 0, Reference = "AH7:AH8" };
            ws.Descendants<Cell>().Single(c => c.CellReference == "AH8").CellFormula = new CellFormula { FormulaType = CellFormulaValues.Shared, SharedIndex = 0 };
            // Excel commonly stores styled blank padding after the notes column.
            foreach (uint row in new[] { 5U, 7U, 8U }) ws.GetFirstChild<SheetData>()!.Elements<Row>().Single(r => r.RowIndex!.Value == row).Append(new Cell { CellReference = "AJ" + row, StyleIndex = 0 });
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        var preview = service.Preview(source, table.Id, 2026, 10); Assert.True(preview.CanExport, string.Join(";", preview.Errors));
        string output = Path.Combine(dir, "shared.xlsx"); service.Export(source, output, table.Id, 2026, 10);
        using var result = SpreadsheetDocument.Open(output, false); var part = result.WorkbookPart!.WorksheetParts.First();
        var anchor = part.Worksheet.Descendants<Cell>().Single(c => c.CellReference == "AI7").CellFormula!;
        var follower = part.Worksheet.Descendants<Cell>().Single(c => c.CellReference == "AI8").CellFormula!;
        Assert.Equal("SUM(D7:AH7)", anchor.Text); Assert.Equal("AI7:AI8", anchor.Reference!.Value);
        Assert.Equal(CellFormulaValues.Shared, follower.FormulaType!.Value); Assert.Equal(0U, follower.SharedIndex!.Value);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator(FileFormatVersions.Office2013).Validate(result));
    }    [Fact]
    public void Expansion_BlocksExternalReferencesToMovedTotals()
    {
        string source = Create(2026, 9);
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var other = doc.WorkbookPart!.WorksheetParts.Last().Worksheet;
            other.GetFirstChild<SheetData>()!.Elements<Row>().Single().Append(new Cell(new CellFormula("'NSG'!AH7"), new CellValue(2)) { CellReference = "B1" });
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.Contains(service.Preview(source, table.Id, 2026, 10).Errors, e => e.Contains("Other!B1"));
    }
    [Fact]
    public void Expansion_BlocksSharedGroupThatExtendsOutsideTable()
    {
        string source = Create(2026, 9);
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var ws = doc.WorkbookPart!.WorksheetParts.First().Worksheet;
            ws.Descendants<Cell>().Single(c => c.CellReference == "AH7").CellFormula = new CellFormula("SUM(D7:AG7)") { FormulaType = CellFormulaValues.Shared, SharedIndex = 0, Reference = "AH7:AH10" };
            ws.GetFirstChild<SheetData>()!.Elements<Row>().Single(r => r.RowIndex!.Value == 10).Append(new Cell(new CellFormula { FormulaType = CellFormulaValues.Shared, SharedIndex = 0 }) { CellReference = "AH10" });
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.Contains(service.Preview(source, table.Id, 2026, 10).Errors, e => e.Contains("dùng chung"));
    }
    [Fact]
    public void Analyze_DoesNotSilentlyDropWorkerWithMissingSerialNumber()
    {
        string source = Create(2026, 9);
        using (var doc = SpreadsheetDocument.Open(source, true))
            doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "A8").RemoveAllChildren();
        Assert.Empty(new AttendanceCalendarService().Analyze(source).Tables);
    }    [Fact]
    public void Expansion_BlocksMergeCrossingTotalsAndDestination()
    {
        string source = Create(2026, 9);
        using (var doc = SpreadsheetDocument.Open(source, true))
            doc.WorkbookPart!.WorksheetParts.First().Worksheet.GetFirstChild<MergeCells>()!.Append(new MergeCell { Reference = "AH7:AJ7" });
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.Contains(service.Preview(source, table.Id, 2026, 10).Errors, e => e.Contains("AH7:AJ7"));
    }}

using ContractDateReplacer.Infrastructure.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
namespace ContractDateReplacer.Core.Tests;

public sealed class AttendancePeriodTests
{
    private static Cell Text(string address, string text) => new(new InlineString(new Text(text))) { CellReference = address, DataType = CellValues.InlineString };
    [Theory]
    [InlineData(2027, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 11, 30)]
    [InlineData(2027, 1, 31)]
    public void SameCell_UpdatesMonthAndBothDates_KeepingLabels(int year, int month, int last)
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var c = doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "A3");
            c.InlineString = new InlineString(new Text("THỜI GIAN: Tháng 10 năm 2026 (Từ ngày 01/10/2026 đến ngày 31/10/2026)"));
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "period.xlsx"); service.Export(source, output, table.Id, year, month);
        using var result = SpreadsheetDocument.Open(output, false);
        Assert.Equal($"THỜI GIAN: Tháng {month:00} năm {year} (Từ ngày 01/{month:00}/{year} đến ngày {last:00}/{month:00}/{year})",
            result.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "A3").InnerText);
    }
    [Fact]
    public void SeparateCells_UpdatesBothDates_WithoutTouchingOtherDates()
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var ws = doc.WorkbookPart!.WorksheetParts.First().Worksheet; var data = ws.GetFirstChild<SheetData>()!;
            data.InsertBefore(new Row(Text("A2", "Ngày ký: 12/10/2026")) { RowIndex = 2 }, data.Elements<Row>().First());
            ws.Descendants<Cell>().Single(c => c.CellReference == "A3").InlineString = new InlineString(new Text("Thời gian: THÁNG 10/2026"));
            data.InsertBefore(new Row(Text("A4", "Từ ngày: 01/10/2026"), Text("Q4", "Đến ngày: 31/10/2026")) { RowIndex = 4 }, data.Elements<Row>().First(r => r.RowIndex?.Value == 5U));
            data.Append(new Row(Text("A10", "Từ ngày 01/10/2026 đến ngày 31/10/2026 — ghi chú ngoài bảng")) { RowIndex = 10 });
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "separate.xlsx"); service.Export(source, output, table.Id, 2028, 2);
        using var result = SpreadsheetDocument.Open(output, false); var cells = result.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("Thời gian: THÁNG 02/2028", cells["A3"].InnerText);
        Assert.Equal("Từ ngày: 01/02/2028", cells["A4"].InnerText); Assert.Equal("Đến ngày: 29/02/2028", cells["Q4"].InnerText);
        Assert.Equal("Ngày ký: 12/10/2026", cells["A2"].InnerText);
        Assert.Equal("Từ ngày 01/10/2026 đến ngày 31/10/2026 — ghi chú ngoài bảng", cells["A10"].InnerText);
    }
    [Fact]
    public void LabelsAndValuesInSeparateCells_UpdateLinkedDateValues()
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var ws = doc.WorkbookPart!.WorksheetParts.First().Worksheet; var data = ws.GetFirstChild<SheetData>()!;
            ws.Descendants<Cell>().Single(c => c.CellReference == "A3").InlineString = new InlineString(new Text("Tháng 10/2026"));
            data.InsertBefore(new Row(Text("A4", "Từ ngày:"), Text("B4", "1/10/2026"), Text("S4", "Đến ngày:"), Text("T4", "31/10/2026")) { RowIndex = 4 }, data.Elements<Row>().First(r => r.RowIndex?.Value == 5U));
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "labels.xlsx"); service.Export(source, output, table.Id, 2027, 2);
        using var result = SpreadsheetDocument.Open(output, false); var cells = result.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("Từ ngày:", cells["A4"].InnerText); Assert.Equal("1/02/2027", cells["B4"].InnerText);
        Assert.Equal("Đến ngày:", cells["S4"].InnerText); Assert.Equal("28/02/2027", cells["T4"].InnerText);
    }
    [Fact]
    public void DashRangeWithoutYears_KeepsFormatAndUsesMonthEnd()
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
            doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "A3").InlineString = new InlineString(new Text("Tháng 10/2026 (Từ ngày 01/10 – 31/10)"));
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "dash.xlsx"); service.Export(source, output, table.Id, 2027, 2);
        using var result = SpreadsheetDocument.Open(output, false);
        Assert.Equal("Tháng 02/2027 (Từ ngày 01/02 – 28/02)", result.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "A3").InnerText);
    }
    [Fact]
    public void FormulaInLinkedEndDate_BlocksExportInsteadOfOverwritingFormula()
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var ws = doc.WorkbookPart!.WorksheetParts.First().Worksheet; var data = ws.GetFirstChild<SheetData>()!;
            data.InsertBefore(new Row(Text("A4", "Từ ngày: 01/10/2026"), Text("S4", "Đến ngày:"),
                new Cell(new CellFormula("TEXT(DATE(2026,10,31),\"dd/mm/yyyy\")"), new CellValue("31/10/2026")) { CellReference = "T4", DataType = CellValues.String }) { RowIndex = 4 }, data.Elements<Row>().First(r => r.RowIndex?.Value == 5U));
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.False(service.Preview(source, table.Id, 2027, 2).CanExport);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "formula.xlsx");
        Assert.Throws<InvalidOperationException>(() => service.Export(source, output, table.Id, 2027, 2)); Assert.False(File.Exists(output));
    }    [Fact]
    public void SharedStringPeriod_PreservesTheOriginalSharedStringPart()
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var wb = doc.WorkbookPart!;
            var strings = wb.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text("Tháng 10/2026 (Từ ngày 01/10/2026 đến ngày 31/10/2026)"))) { Count = 1, UniqueCount = 1 };
            var c = wb.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "A3");
            c.RemoveAllChildren(); c.DataType = CellValues.SharedString; c.CellValue = new CellValue("0");
        }
        using (var zip = System.IO.Compression.ZipFile.Open(source, System.IO.Compression.ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("xl/sharedStrings.xml")!; string xml;
            using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd(); entry.Delete();
            xml = xml.Replace("<x:", "<").Replace("</x:", "</").Replace("xmlns:x=", "xmlns=");
            using var writer = new StreamWriter(zip.CreateEntry("xl/sharedStrings.xml").Open()); writer.Write(xml);
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "strings.xlsx"); service.Export(source, output, table.Id, 2028, 2);
        using var originalZip = System.IO.Compression.ZipFile.OpenRead(source); using var outputZip = System.IO.Compression.ZipFile.OpenRead(output);
        using var a = new StreamReader(originalZip.GetEntry("xl/sharedStrings.xml")!.Open()); using var b = new StreamReader(outputZip.GetEntry("xl/sharedStrings.xml")!.Open());
        Assert.Equal(a.ReadToEnd(), b.ReadToEnd());
    }    [Fact]
    public void MonthCellToTheRight_StillUpdatesFromDateAtTableLeft()
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var ws = doc.WorkbookPart!.WorksheetParts.First().Worksheet; var data = ws.GetFirstChild<SheetData>()!;
            ws.GetFirstChild<MergeCells>()!.Elements<MergeCell>().Single(m => m.Reference == "A3:AJ3").Remove();
            data.Elements<Row>().Single(r => r.RowIndex?.Value == 3U).Elements<Cell>().Single().RemoveAllChildren();
            data.Elements<Row>().Single(r => r.RowIndex?.Value == 3U).Append(Text("E3", "Tháng 10/2026"));
            data.InsertBefore(new Row(Text("A4", "Từ ngày:"), Text("B4", "01/10/2026"), Text("Q4", "Đến ngày: 31/10/2026")) { RowIndex = 4 }, data.Elements<Row>().First(r => r.RowIndex?.Value == 5U));
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "left.xlsx"); service.Export(source, output, table.Id, 2028, 2);
        using var result = SpreadsheetDocument.Open(output, false); var cells = result.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("01/02/2028", cells["B4"].InnerText); Assert.Equal("Đến ngày: 29/02/2028", cells["Q4"].InnerText);
    }}

using ContractDateReplacer.Infrastructure.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ContractDateReplacer.Core.Tests;

public class AttendanceCalendarTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "AttendanceCalendarTests_" + Guid.NewGuid().ToString("N"));
    public AttendanceCalendarTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    internal string CreateTemplate(bool occupiedLastDay = false)
    {
        string path = Path.Combine(dir, Guid.NewGuid() + ".xlsx");
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart(); wb.Workbook = new Workbook(new Sheets());
        var styles = wb.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(new Fonts(new Font()),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = "FF9BBB59" }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid })),
            new Borders(new Border()), new CellStyleFormats(new CellFormat()),
            new CellFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0 }, new CellFormat { FontId = 0, FillId = 2, BorderId = 0, ApplyFill = true }));
        var ws = wb.AddNewPart<WorksheetPart>(); ws.Worksheet = new Worksheet(new SheetData());
        wb.Workbook.Sheets!.Append(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Cong" });
        var data = ws.Worksheet.GetFirstChild<SheetData>()!;
        var period = new Row { RowIndex = 3 }; period.Append(Text("A3", "Thời gian: tháng 10/2026(từ 01/10/2026 đến 31/10/2026)")); data.Append(period);
        var days = new Row { RowIndex = 5 }; var weekdays = new Row { RowIndex = 6 };
        days.Append(Text("A5", "STT"), Text("B5", "Họ và tên"), Text("C5", "Hạng mục"));
        for (int d = 1; d <= 31; d++)
        {
            string col = Col(d + 3); bool sunday = new DateTime(2026, 10, d).DayOfWeek == DayOfWeek.Sunday;
            days.Append(new Cell(new CellValue(d)) { CellReference = col + "5", StyleIndex = sunday ? 1U : 0U });
            weekdays.Append(Text(col + "6", sunday ? "CN" : "T" + ((int)new DateTime(2026, 10, d).DayOfWeek + 1), sunday ? 1U : 0U));
        }
        days.Append(Text("AI5", "Tổng công"), Text("AJ5", "Ghi chú")); data.Append(days, weekdays);
        var person = new Row { RowIndex = 7 }; person.Append(Text("A7", "1"), Text("B7", "Nguyễn Văn A"), Text("C7", "Bảo vệ"));
        for (int d = 1; d <= 31; d++)
        {
            var cell = new Cell { CellReference = Col(d + 3) + "7", StyleIndex = new DateTime(2026, 10, d).DayOfWeek == DayOfWeek.Sunday ? 1U : 0U };
            if (d == 1 || (d == 31 && occupiedLastDay)) cell.CellValue = new CellValue(1);
            person.Append(cell);
        }
        person.Append(new Cell(new CellFormula("SUM(D7:AH7)"), new CellValue(occupiedLastDay ? 2 : 1)) { CellReference = "AI7" }); data.Append(person);
        var total = new Row { RowIndex = 8 }; total.Append(Text("A8", "Tổng công"));
        for (int d = 1; d <= 31; d++) total.Append(new Cell { CellReference = Col(d + 3) + "8", StyleIndex = new DateTime(2026, 10, d).DayOfWeek == DayOfWeek.Sunday ? 1U : 0U });
        total.Append(new Cell(new CellValue(occupiedLastDay ? 2 : 1)) { CellReference = "AI8" }); data.Append(total);
        ws.Worksheet.Append(new MergeCells(new MergeCell { Reference = "A3:AJ3" }, new MergeCell { Reference = "A8:C8" }));
        var other = wb.AddNewPart<WorksheetPart>(); other.Worksheet = new Worksheet(new SheetData(new Row(Text("A1", "Giữ nguyên")) { RowIndex = 1 }));
        wb.Workbook.Sheets.Append(new Sheet { Id = wb.GetIdOfPart(other), SheetId = 2, Name = "Khac" });
        return path;
    }
    private static Cell Text(string address, string value, uint style = 0) => new(new InlineString(new Text(value))) { CellReference = address, DataType = CellValues.InlineString, StyleIndex = style };
    private static string Col(int col) { string s = ""; while (col > 0) { col--; s = (char)('A' + col % 26) + s; col /= 26; } return s; }

    [Theory]
    [InlineData(2027, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 11, 30)]
    [InlineData(2027, 1, 31)]
    public void Export_UpdatesCalendarAndPreservesAttendance(int year, int month, int length)
    {
        string source = CreateTemplate(); byte[] original = File.ReadAllBytes(source);
        var service = new AttendanceCalendarService();
        var analysis = service.Analyze(source); Assert.Empty(analysis.Errors);
        var table = Assert.Single(analysis.Tables);
        Assert.Equal(5, table.DayRow); // Not hardcoded to the supplied workbook's row 13.
        var preview = service.Preview(source, table.Id, year, month);
        Assert.Empty(preview.Errors); Assert.Equal(length, preview.Days.Count);
        Assert.All(preview.Days, day => Assert.Equal(new DateTime(year, month, day.Day).DayOfWeek == DayOfWeek.Sunday, day.IsSunday));
        string output = Path.Combine(dir, "out.xlsx");
        service.Export(source, output, table.Id, year, month);
        Assert.Equal(original, File.ReadAllBytes(source));
        using var doc = SpreadsheetDocument.Open(output, false);
        var ws = doc.WorkbookPart!.WorksheetParts.First(p => p.Worksheet.Descendants<Cell>().Any(c => c.CellReference == "AI8"));
        Cell At(string a) => ws.Worksheet.Descendants<Cell>().Single(c => c.CellReference == a);
        Assert.Equal("1", At("D7").CellValue!.Text);
        Assert.Equal("SUM(D7:AH7)", At("AI7").CellFormula!.Text);
        Assert.Equal("SUM(AI7:AI7)", At("AI8").CellFormula!.Text);
        Assert.Contains($"{month:00}/{year}", At("A3").InnerText);
        for (int d = 1; d <= 31; d++)
        {
            var dayCell = At(Col(d + 3) + "5"); var weekdayCell = At(Col(d + 3) + "6");
            Assert.Equal(d <= length ? d.ToString() : "", dayCell.InnerText);
            Assert.Equal(d <= length ? (new DateTime(year, month, d).DayOfWeek == DayOfWeek.Sunday ? "CN" : "T" + ((int)new DateTime(year, month, d).DayOfWeek + 1)) : "", weekdayCell.InnerText);
        }
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator(FileFormatVersions.Office2013).Validate(doc));
        Assert.Single(service.Analyze(output).Tables); // Generated short months remain usable.
        Assert.Throws<IOException>(() => service.Export(source, output, table.Id, year, month));
    }

    [Fact]
    public void ShortMonth_WithOccupiedExtraDay_BlocksExport()
    {
        string source = CreateTemplate(true); var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        var preview = service.Preview(source, table.Id, 2027, 2);
        Assert.Contains(preview.Errors, error => error.Contains("Cong!AH7"));
        string output = Path.Combine(dir, "blocked.xlsx");
        Assert.Throws<InvalidOperationException>(() => service.Export(source, output, table.Id, 2027, 2));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Analyze_DoesNotGuessAnUnrecognizedWorkbook()
    {
        string source = CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
            doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().First(c => c.CellReference == "E5").CellValue = new CellValue(9);
        var analysis = new AttendanceCalendarService().Analyze(source);
        Assert.Empty(analysis.Tables); Assert.NotEmpty(analysis.Errors);
    }
    [Fact]
    public void ShortMonth_WithFormulaInDailyTotal_BlocksExport()
    {
        string source = CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var cell = doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "AH8");
            cell.CellFormula = new CellFormula("SUM(AH7:AH7)");
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        Assert.Contains(service.Preview(source, table.Id, 2027, 2).Errors, e => e.Contains("Cong!AH8"));
    }

    [Fact]
    public void Analyze_SkipsUnrelatedChartSheets()
    {
        string source = CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var wb = doc.WorkbookPart!; var chart = wb.AddNewPart<ChartsheetPart>(); chart.Chartsheet = new Chartsheet();
            wb.Workbook.Sheets!.Append(new Sheet { Id = wb.GetIdOfPart(chart), SheetId = 3, Name = "Chart" });
        }
        Assert.Single(new AttendanceCalendarService().Analyze(source).Tables);
    }

    [Fact]
    public void Export_HandlesOfficeNamespaceAttributeReordering()
    {
        string source = CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            foreach (var root in new OpenXmlElement[] { doc.WorkbookPart!.WorksheetParts.First().Worksheet, doc.WorkbookPart.WorkbookStylesPart!.Stylesheet })
            {
                root.AddNamespaceDeclaration("x14ac", "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac");
                root.MCAttributes = new MarkupCompatibilityAttributes { Ignorable = "x14ac" };
            }
        }
        // Excel writes a default namespace; OpenXML reserializes it with an explicit prefix.
        using (var zip = System.IO.Compression.ZipFile.Open(source, System.IO.Compression.ZipArchiveMode.Update))
        {
            foreach (string name in new[] { "xl/worksheets/sheet1.xml", "xl/styles.xml" })
            {
                var entry = zip.GetEntry(name)!; string xml;
                using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd();
                entry.Delete();
                xml = xml.Replace("<x:", "<").Replace("</x:", "</").Replace("xmlns:x=", "xmlns=");
                using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(xml);
            }
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        service.Export(source, Path.Combine(dir, "namespaces.xlsx"), table.Id, 2026, 12);
    }

    [Fact]
    public void Export_ClearsDifferentOldSundayFillsAndUsesGreenWithoutChangingFont()
    {
        string source = CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var wb = doc.WorkbookPart!; var styles = wb.WorkbookStylesPart!.Stylesheet;
            styles.Fills!.Elements<Fill>().ElementAt(2).PatternFill!.ForegroundColor!.Rgb = "FFFF0000";
            styles.Fills.Append(new Fill(new PatternFill(new ForegroundColor { Rgb = "FF0000FF" }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid })); styles.Fills.Count = 4;
            styles.Fonts!.Append(new Font(new Bold())); styles.Fonts.Count = 2;
            styles.CellFormats!.Append(new CellFormat { FontId = 1, FillId = 3, BorderId = 0, ApplyFont = true }); styles.CellFormats.Count = 3;
            foreach (var c in wb.WorksheetParts.First().Worksheet.Descendants<Cell>().Where(c => c.CellReference!.Value!.EndsWith("7") && c.StyleIndex?.Value == 1)) c.StyleIndex = 2;
        }
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(dir, "green.xlsx"); service.Export(source, output, table.Id, 2026, 12);
        using var result = SpreadsheetDocument.Open(output, false); var resultWb = result.WorkbookPart!;
        var ws = resultWb.WorksheetParts.First(); var stylesOut = resultWb.WorkbookStylesPart!.Stylesheet;
        CellFormat Format(string address) => stylesOut.CellFormats!.Elements<CellFormat>().ElementAt((int)(ws.Worksheet.Descendants<Cell>().Single(c => c.CellReference == address).StyleIndex?.Value ?? 0));
        Assert.Equal(0U, Format("G7").FillId!.Value); Assert.Equal(1U, Format("G7").FontId!.Value);
        foreach (string address in new[] { "I5", "I6", "I7", "I8" })
        {
            var fill = stylesOut.Fills!.Elements<Fill>().ElementAt((int)Format(address).FillId!.Value);
            Assert.Equal("FF9BBB59", fill.PatternFill!.ForegroundColor!.Rgb!.Value);
        }
    }

    [Fact]
    public void Export_RejectsSourceChangedSincePreview()
    {
        string source = CreateTemplate(); var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        var preview = service.Preview(source, table.Id, 2026, 12);
        using (var doc = SpreadsheetDocument.Open(source, true))
            doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<Cell>().Single(c => c.CellReference == "D7").CellValue = new CellValue(2);
        string output = Path.Combine(dir, "changed.xlsx");
        Assert.Throws<InvalidOperationException>(() => service.Export(source, output, table.Id, 2026, 12, preview.SourceChecksum));
        Assert.False(File.Exists(output));
    }}

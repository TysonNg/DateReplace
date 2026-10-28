using System.IO.Compression;
using System.Xml.Linq;
using ContractDateReplacer.Infrastructure.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
namespace ContractDateReplacer.Core.Tests;
public sealed class AttendanceCalculationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Export_DropsStaleCalculationChainAndRequestsRebuild(bool compact)
    {
        using var full = new AttendanceCalendarTests(); using var small = new CompactAttendanceCalendarTests();
        string source = compact ? small.Create(2026, 9) : full.CreateTemplate();
        using (var doc = SpreadsheetDocument.Open(source, true))
        {
            var wb = doc.WorkbookPart!;
            wb.AddNewPart<CalculationChainPart>().CalculationChain = new CalculationChain(new CalculationCell { CellReference = compact ? "AH7" : "AI7", SheetId = 1 });
            wb.Workbook.CalculationProperties = new CalculationProperties { CalculationId = 123, CalculationMode = CalculateModeValues.Manual, FullCalculationOnLoad = false, ForceFullCalculation = false };
            wb.Workbook.DefinedNames = new DefinedNames(new DefinedName((compact ? "NSG" : "Cong") + "!$A$1:$AK$40") { Name = "_xlnm.Print_Area", LocalSheetId = 0 });
        }
        // Native Excel writes Id first; packaging may rewrite the same attributes in another order.
        using (var package = ZipFile.Open(source, ZipArchiveMode.Update))
        {
            var entry = package.GetEntry("xl/_rels/workbook.xml.rels")!; XDocument relations;
            using (var stream = entry.Open()) relations = XDocument.Load(stream);
            foreach (var relation in relations.Root!.Elements())
            {
                var attributes = relation.Attributes().OrderBy(a => a.Name.LocalName == "Id" ? 0 : 1).Select(a => new XAttribute(a)).ToArray();
                relation.RemoveAttributes(); relation.Add(attributes);
            }
            entry.Delete(); using var writer = new StreamWriter(package.CreateEntry("xl/_rels/workbook.xml.rels").Open()); writer.Write(relations.ToString(SaveOptions.DisableFormatting));
        }
        byte[] before = File.ReadAllBytes(source); var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "rebuild.xlsx"); service.Export(source, output, table.Id, 2026, 10);
        Assert.Equal(before, File.ReadAllBytes(source));
        using (var result = SpreadsheetDocument.Open(output, false))
        {
            var wb = result.WorkbookPart!; Assert.Null(wb.CalculationChainPart);
            Assert.True(wb.Workbook.CalculationProperties!.FullCalculationOnLoad!.Value);
            Assert.True(wb.Workbook.CalculationProperties.ForceFullCalculation!.Value);
            Assert.Equal(CalculateModeValues.Manual, wb.Workbook.CalculationProperties.CalculationMode!.Value);
            Assert.Equal(123U, wb.Workbook.CalculationProperties.CalculationId!.Value);
            Assert.Equal((compact ? "NSG" : "Cong") + "!$A$1:$AK$40", wb.Workbook.DefinedNames!.Elements<DefinedName>().Single().Text);
        }
        using var zip = ZipFile.OpenRead(output); Assert.Null(zip.GetEntry("xl/calcChain.xml"));
        using var contentReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        Assert.DoesNotContain(XDocument.Parse(contentReader.ReadToEnd()).Descendants(), e => e.Attribute("PartName")?.Value == "/xl/calcChain.xml");
        using var relationsReader = new StreamReader(zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open());
        Assert.DoesNotContain(XDocument.Parse(relationsReader.ReadToEnd()).Descendants(), e => e.Attribute("Type")?.Value.EndsWith("/calcChain", StringComparison.Ordinal) == true);
    }
    [Fact]
    public void Export_WithoutExistingChain_AlsoRequestsCalculation()
    {
        using var fixture = new AttendanceCalendarTests(); string source = fixture.CreateTemplate();
        var service = new AttendanceCalendarService(); var table = Assert.Single(service.Analyze(source).Tables);
        string output = Path.Combine(Path.GetDirectoryName(source)!, "new-chain.xlsx"); service.Export(source, output, table.Id, 2026, 10);
        using var result = SpreadsheetDocument.Open(output, false);
        Assert.Null(result.WorkbookPart!.CalculationChainPart);
        Assert.True(result.WorkbookPart.Workbook.CalculationProperties?.FullCalculationOnLoad?.Value);
    }
}

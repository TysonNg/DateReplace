using System.Globalization;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Infrastructure.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ContractDateReplacer.Core.Tests;

public class ExcelDocumentServiceTests : IDisposable
{
 private readonly string root = Path.Combine(Path.GetTempPath(), "ExcelDates_" + Guid.NewGuid().ToString("N"));
 private readonly ExcelDocumentService service = new();
 public ExcelDocumentServiceTests() => Directory.CreateDirectory(root);
 public void Dispose() => Directory.Delete(root, true);
 private string Create(bool date1904 = false)
 {
  var path = Path.Combine(root, Guid.NewGuid()+".xlsx");
  using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
  var wb = doc.AddWorkbookPart();
  wb.Workbook = new Workbook(new WorkbookProperties { Date1904 = date1904 });
  var styles = wb.AddNewPart<WorkbookStylesPart>();
  styles.Stylesheet = new Stylesheet(new Fonts(new Font()), new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })), new Borders(new Border()), new CellStyleFormats(new CellFormat()), new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
  var shared = wb.AddNewPart<SharedStringTablePart>();
  shared.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text("Signed 01/02/2026 end")));
  var ws = wb.AddNewPart<WorksheetPart>();
  double serial = new DateTime(2026,2,1).ToOADate() - (date1904 ? 1462 : 0) + .625;
  ws.Worksheet = new Worksheet(new SheetData(
   new Row(TextCell("A1", "Contract date"), TextCell("B1", "Signed date")) { RowIndex = 1 },
   new Row(TextCell("A2", "Effective"), new Cell { CellReference="B2", DataType=CellValues.SharedString, CellValue=new CellValue("0") }) { RowIndex=2 },
   new Row(new Cell { CellReference="A3", StyleIndex=1, CellValue=new CellValue(serial.ToString("R",CultureInfo.InvariantCulture)) }, new Cell { CellReference="B3", CellValue=new CellValue("20260201") }) { RowIndex=3 },
   new Row(new Cell { CellReference="A4", StyleIndex=1, CellFormula=new CellFormula("A3"), CellValue=new CellValue(serial.ToString("R",CultureInfo.InvariantCulture)) }) { RowIndex=4 },
   new Row(TextCell("A5", "From 01/02/2026 through 03/04/2026.")) { RowIndex=5 },
   new Row(new Cell { CellReference="A6", DataType=CellValues.Date, CellValue=new CellValue(new DateTime(2026,2,1,15,0,0)) }) { RowIndex=6 }));
  wb.Workbook.Append(new Sheets(new Sheet { Name="Contracts", SheetId=1, Id=wb.GetIdOfPart(ws), State=SheetStateValues.Hidden }));
  return path;
 }
 private static Cell TextCell(string address,string text) => new() { CellReference=address, DataType=CellValues.InlineString, InlineString=new InlineString(new Text(text)) };
 private static ReplacementOccurrence Occ(DocumentTextSegment s, int index=0, string text="09/10/2027") => new() { Segment=s, Candidate=s.DetectedDates[index], NewDateText=text, NewDateTime=new DateTime(2027,10,9) };
 [Fact] public async Task Scan_handles_shared_inline_numeric_formula_hidden_and_context()
 {
  var segments=await service.ScanDocumentAsync(Create());
  var shared=segments.Single(s=>s.SourceLocation=="Contracts!B2");
  Assert.Equal("Signed 01/02/2026 end",shared.LogicalText);
  Assert.Contains("Signed date",shared.DetectedDates.Single().ContextBefore);
  Assert.Contains("Effective",shared.DetectedDates.Single().ContextBefore);
  Assert.Equal(7,shared.DetectedDates.Single().StartIndex);
  Assert.Empty(segments.Single(s=>s.SourceLocation=="Contracts!B3").DetectedDates);
  Assert.Equal("01/02/2026",segments.Single(s=>s.SourceLocation=="Contracts!A3").LogicalText);
  Assert.False(segments.Single(s=>s.SourceLocation=="Contracts!A4").IsSupportedForReplacement);
  Assert.Single(segments.Single(s=>s.SourceLocation=="Contracts!A4").DetectedDates);
  Assert.Equal(2,segments.Single(s=>s.SourceLocation=="Contracts!A5").DetectedDates.Count);
  Assert.All(segments,s=>Assert.True(s.IsHiddenSheet));
 }
 [Theory] [InlineData(false)] [InlineData(true)]
 public async Task Replace_preserves_original_serial_fraction_styles_and_formula(bool date1904)
 {
  var source=Create(date1904); var bytes=File.ReadAllBytes(source);
  var segments=await service.ScanDocumentAsync(source);
  var occurrences=segments.Where(s=>s.DetectedDates.Count>0).Select(s=>Occ(s)).ToList();
  var dest=Path.Combine(root,"out.xlsx");
  Assert.True(await service.ReplaceDatesAsync(source,dest,occurrences));
  Assert.Equal(bytes,File.ReadAllBytes(source));
  using(var doc=SpreadsheetDocument.Open(dest,false))
  {
   var cells=doc.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().ToDictionary(c=>c.CellReference!.Value!);
   var value=double.Parse(cells["A3"].CellValue!.Text,CultureInfo.InvariantCulture);
   Assert.Equal(new DateTime(2027,10,9).ToOADate()-(date1904?1462:0)+.625,value,8);
   Assert.Equal(1U,cells["A3"].StyleIndex!.Value);
   Assert.Equal("A3",cells["A4"].CellFormula!.Text);
   Assert.Equal(CellValues.Date,cells["A6"].DataType!.Value);
   Assert.Contains("T15:00:00",cells["A6"].CellValue!.Text);
  }
  var result=await service.VerifyOutputAsync(source,dest,occurrences);
  Assert.True(result.IsPassed,string.Join(";",result.Issues));
  Assert.Equal(4,result.ActualReplacementsDone);
 }
 [Fact] public async Task Multiple_dates_are_replaced_right_to_left_and_verified_with_shift()
 {
  var source=Create(); var s=(await service.ScanDocumentAsync(source)).Single(s=>s.SourceLocation=="Contracts!A5");
  var first=Occ(s,0,"9/10/2027"); var second=Occ(s,1); second.IsSelected=false;
  var dest=Path.Combine(root,"out.xlsx");
  await service.ReplaceDatesAsync(source,dest,new[]{first,second},false);
  Assert.Equal("From 9/10/2027 through 03/04/2026.",(await service.ScanDocumentAsync(dest)).Single(x=>x.SourceLocation==s.SourceLocation).LogicalText);
  var verification=await service.VerifyOutputAsync(source,dest,new[]{first,second}); Assert.True(verification.IsPassed,string.Join(";",verification.Issues));
 }
 [Fact] public async Task Verification_detects_preserved_cell_tampering_and_invalid_package()
 {
  var source=Create(); var dest=Path.Combine(root,"out.xlsx");
  await service.ReplaceDatesAsync(source,dest,Array.Empty<ReplacementOccurrence>());
  using(var doc=SpreadsheetDocument.Open(dest,true)) doc.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c=>c.CellReference=="B3").CellValue=new CellValue("999");
  Assert.False((await service.VerifyOutputAsync(source,dest,Array.Empty<ReplacementOccurrence>())).IsPassed);
  File.WriteAllText(dest,"invalid");
  var result=await service.VerifyOutputAsync(source,dest,Array.Empty<ReplacementOccurrence>());
  Assert.False(result.CanOpenDocument); Assert.False(result.IsPassed);
 }
 [Fact] public async Task Replacement_refuses_existing_destination_and_original_path()
 {
  var source=Create();
  await Assert.ThrowsAsync<IOException>(()=>service.ReplaceDatesAsync(source,source,Array.Empty<ReplacementOccurrence>()));
 }
 [Theory]
 [InlineData("dd mmm yyyy", true)]
 [InlineData("[$-409]mmm-yy", true)]
 [InlineData("0 \"days\"", false)]
 [InlineData("[h]:mm:ss", false)]
 [InlineData("0.00", false)]
 [InlineData(@"0 \d", false)]
 public async Task Custom_formats_distinguish_dates_from_numeric_and_time_values(string code,bool isDate)
 {
  var source=Create();
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var styles=doc.WorkbookPart!.WorkbookStylesPart!.Stylesheet;
   styles.InsertAt(new NumberingFormats(new NumberingFormat { NumberFormatId=164, FormatCode=code }),0);
   styles.CellFormats!.Elements<CellFormat>().ElementAt(1).NumberFormatId=164;
  }
  var segment=(await service.ScanDocumentAsync(source)).Single(s=>s.CellAddress=="A3");
  Assert.Equal(isDate,segment.IsSpreadsheetNumericDate);
  Assert.Equal(isDate?1:0,segment.DetectedDates.Count);
 }
 [Fact] public async Task Shared_string_alias_and_merged_cells_are_preserved()
 {
  var source=Create();
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var ws=doc.WorkbookPart!.WorksheetParts.Single().Worksheet;
   ws.GetFirstChild<SheetData>()!.Append(new Row(new Cell { CellReference="A7",DataType=CellValues.SharedString,CellValue=new CellValue("0") }) { RowIndex=7 });
   ws.Append(new MergeCells(new MergeCell { Reference="A8:B8" }));
  }
  var segment=(await service.ScanDocumentAsync(source)).Single(s=>s.CellAddress=="B2");
  var occurrence=Occ(segment); var dest=Path.Combine(root,"alias.xlsx");
  await service.ReplaceDatesAsync(source,dest,new[]{occurrence});
  var scanned=await service.ScanDocumentAsync(dest);
  Assert.Equal("Signed 09/10/2027 end",scanned.Single(s=>s.CellAddress=="B2").LogicalText);
  Assert.Equal("Signed 01/02/2026 end",scanned.Single(s=>s.CellAddress=="A7").LogicalText);
  var verification=await service.VerifyOutputAsync(source,dest,new[]{occurrence});
  Assert.True(verification.IsPassed,string.Join(";",verification.Issues));
 }
 [Fact] public async Task Two_selected_dates_are_replaced_and_verified_at_exact_shifted_positions()
 {
  var source=Create(); var segment=(await service.ScanDocumentAsync(source)).Single(s=>s.CellAddress=="A5");
  var first=Occ(segment,0,"9/10/2027"); var second=Occ(segment,1,"10/11/2028");
  var dest=Path.Combine(root,"two.xlsx");
  await service.ReplaceDatesAsync(source,dest,new[]{first,second},false);
  Assert.Equal("From 9/10/2027 through 10/11/2028.",(await service.ScanDocumentAsync(dest)).Single(s=>s.CellAddress=="A5").LogicalText);
  var result=await service.VerifyOutputAsync(source,dest,new[]{first,second});
  Assert.True(result.IsPassed,string.Join(";",result.Issues)); Assert.Equal(2,result.ActualReplacementsDone);
 }
 [Fact] public async Task Formula_protection_uses_actual_cell_even_when_segment_metadata_is_forged()
 {
  var source=Create(); var segment=(await service.ScanDocumentAsync(source)).Single(s=>s.CellAddress=="A4");
  segment.IsSupportedForReplacement=true; segment.IsSpreadsheetFormula=false;
  var occurrence=Occ(segment); var dest=Path.Combine(root,"formula.xlsx");
  await service.ReplaceDatesAsync(source,dest,new[]{occurrence});
  Assert.Equal("01/02/2026",(await service.ScanDocumentAsync(dest)).Single(s=>s.CellAddress=="A4").LogicalText);
  var result=await service.VerifyOutputAsync(source,dest,new[]{occurrence}); Assert.True(result.IsPassed); Assert.Equal(0,result.ExpectedReplacements);
 }
 [Fact] public async Task CanRead_returns_false_for_invalid_package_and_honors_cancellation()
 {
  var path=Path.Combine(root,"bad.xlsx"); File.WriteAllText(path,"not a zip");
  Assert.False(await service.CanReadAsync(path));
  using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>service.CanReadAsync(path,cancellation.Token));
 }
 [Fact] public async Task Verification_rejects_invalid_xml_in_an_openable_package()
 {
  var source=Create(); var dest=Path.Combine(root,"invalidxml.xlsx");
  await service.ReplaceDatesAsync(source,dest,Array.Empty<ReplacementOccurrence>());
  using(var doc=SpreadsheetDocument.Open(dest,true))
   doc.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c=>c.CellReference=="B3").StyleIndex=999;
  var result=await service.VerifyOutputAsync(source,dest,Array.Empty<ReplacementOccurrence>());
  Assert.True(result.CanOpenDocument); Assert.False(result.IsValidXmlStructure); Assert.False(result.IsPassed);
 } [Theory] [InlineData(false)] [InlineData(true)]
 public async Task Rich_split_dates_preserve_run_properties_and_shared_aliases(bool shared)
 {
  var source=Create(); string[] properties;
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var wb=doc.WorkbookPart!;
   var rich=new InlineString(
    new Run(new RunProperties(new Italic()),new Text("From ") { Space=SpaceProcessingModeValues.Preserve }),
    new Run(new RunProperties(new Bold(),new Color { Rgb="FFFF0000" }),new Text("01/")),
    new Run(new RunProperties(new Underline()),new Text("02/2026 through ") { Space=SpaceProcessingModeValues.Preserve }),
    new Run(new RunProperties(new FontSize { Val=15 }),new Text("03/04/")),
    new Run(new RunProperties(new Strike()),new Text("2026 end")));
   properties=rich.Elements<Run>().Select(r=>r.RunProperties!.OuterXml).ToArray();
   var cell=wb.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c=>c.CellReference=="A5");
   if(shared)
   {
    wb.SharedStringTablePart!.SharedStringTable.Append(new SharedStringItem(rich.ChildElements.Select(c=>c.CloneNode(true))));
    cell.InlineString=null; cell.DataType=CellValues.SharedString; cell.CellValue=new CellValue("1");
    wb.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!.Append(new Row(new Cell { CellReference="A7",DataType=CellValues.SharedString,CellValue=new CellValue("1") }) { RowIndex=7 });
   }
   else cell.InlineString=rich;
  }
  var segment=(await service.ScanDocumentAsync(source)).Single(s=>s.CellAddress=="A5");
  var occurrences=new[]{Occ(segment,0,"9/10/2027"),Occ(segment,1,"10/11/2028")};
  var dest=Path.Combine(root,"rich.xlsx");
  await service.ReplaceDatesAsync(source,dest,occurrences,false);
  using(var doc=SpreadsheetDocument.Open(dest,false))
  {
   var cell=doc.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c=>c.CellReference=="A5");
   var runs=cell.InlineString!.Elements<Run>().ToList();
   Assert.Equal(properties,runs.Select(r=>r.RunProperties!.OuterXml).ToArray());
   Assert.Equal(new[]{"From ","9/10/2027"," through ","10/11/2028"," end"},runs.Select(r=>r.Text!.Text).ToArray());
  }
  if(shared) Assert.Equal("From 01/02/2026 through 03/04/2026 end",(await service.ScanDocumentAsync(dest)).Single(s=>s.CellAddress=="A7").LogicalText);
  var verification=await service.VerifyOutputAsync(source,dest,occurrences);
  Assert.True(verification.IsPassed,string.Join(";",verification.Issues));
 }
 [Fact] public async Task Header_context_uses_latest_preceding_header_and_exact_left_neighbor_in_unsorted_rows()
 {
  var source=Create();
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var data=doc.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
   data.Append(new Row(TextCell("B10","Renewal date")) { RowIndex=10 });
   data.PrependChild(new Row(TextCell("A11","Label"),TextCell("B11","01/02/2026")) { RowIndex=11 });
  }
  var candidate=(await service.ScanDocumentAsync(source)).Single(s=>s.CellAddress=="B11").DetectedDates.Single();
  Assert.Equal("Renewal date | Label | ",candidate.ContextBefore);
  Assert.Equal(0,candidate.StartIndex); Assert.Equal("01/02/2026",candidate.RawText);
 } [Theory] [InlineData(true)] [InlineData(false)]
 public async Task Worksheet_names_containing_bang_use_exact_metadata_or_last_separator(bool metadata)
 {
  var source=Create();
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var wb=doc.WorkbookPart!; var original=wb.Workbook.Sheets!.Elements<Sheet>().Single(); original.Name="A";
   var part=wb.AddNewPart<WorksheetPart>(); part.Worksheet=(Worksheet)wb.WorksheetParts.First(p=>p!=part).Worksheet.CloneNode(true);
   wb.Workbook.Sheets.Append(new Sheet { Name="A!B", SheetId=2, Id=wb.GetIdOfPart(part) });
  }
  var segment=(await service.ScanDocumentAsync(source)).Single(s=>s.SourceLocation=="A!B!B2");
  if(!metadata) { segment.WorksheetName=null; segment.CellAddress=null; }
  var occurrence=Occ(segment); var dest=Path.Combine(root,"bang.xlsx");
  await service.ReplaceDatesAsync(source,dest,new[]{occurrence});
  var output=await service.ScanDocumentAsync(dest);
  Assert.Equal("Signed 09/10/2027 end",output.Single(s=>s.SourceLocation=="A!B!B2").LogicalText);
  Assert.Equal("Signed 01/02/2026 end",output.Single(s=>s.SourceLocation=="A!B2").LogicalText);
  Assert.True((await service.VerifyOutputAsync(source,dest,new[]{occurrence})).IsPassed);
 }
 [Theory]
 [InlineData("[>=10000]0;dd/mm/yyyy",45000,false)]
 [InlineData("[>=10000]0;dd/mm/yyyy",5000,true)]
 [InlineData("[>=10000]0;dd/mm/yyyy",0,true)]
 [InlineData("[>=10000]0;dd/mm/yyyy",-100,true)]
 [InlineData("[>0]dd/mm/yyyy;[<0]0;0",45000,true)]
 [InlineData("[>0]dd/mm/yyyy;[<0]0;0",-100,false)]
 [InlineData("[>0]dd/mm/yyyy;[<0]0;0",0,false)]
 [InlineData("[<0]dd/mm/yyyy;[=0]dd/mm/yyyy;0",-100,true)]
 [InlineData("[<0]dd/mm/yyyy;[=0]dd/mm/yyyy;0",0,true)]
 [InlineData("[<0]dd/mm/yyyy;[=0]dd/mm/yyyy;0",45000,false)]
 [InlineData("0;dd/mm/yyyy;0",45000,false)]
 [InlineData("0;dd/mm/yyyy;0",-100,true)]
 [InlineData("0;0;dd/mm/yyyy",0,true)]
 [InlineData("dd/mm/yyyy;0",0,true)]
 [InlineData("[>10]0;[<0]dd/mm/yyyy",5,false)]
 [InlineData("[unsupported]dd/mm/yyyy",45000,false)]
 [InlineData("[>=oops]dd/mm/yyyy;0",45000,false)]
 [InlineData("[>=10000]0;\"dd;mm;yyyy\"0",5000,false)]
 [InlineData(@"[>=10000]0;0\;\d",5000,false)]
 [InlineData("[>=10000]0;\"unterminated",5000,false)]
 public async Task Only_applicable_valid_number_format_section_can_classify_native_date(string code,double value,bool date)
 {
  var source=Create();
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var wb=doc.WorkbookPart!; var styles=wb.WorkbookStylesPart!.Stylesheet;
   styles.InsertAt(new NumberingFormats(new NumberingFormat { NumberFormatId=164, FormatCode=code }),0);
   styles.CellFormats!.Elements<CellFormat>().ElementAt(1).NumberFormatId=164;
   wb.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c=>c.CellReference=="A3").CellValue=new CellValue(value.ToString("R",CultureInfo.InvariantCulture));
  }
  Assert.Equal(date,(await service.ScanDocumentAsync(source)).Single(s=>s.CellAddress=="A3").IsSpreadsheetNumericDate);
 }
 [Theory] [InlineData(true)] [InlineData(false)]
 public async Task Protected_locked_cells_remain_visible_but_cannot_be_replaced_even_with_forged_flags(bool enabled)
 {
  var source=Create();
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var wb=doc.WorkbookPart!; var formats=wb.WorkbookStylesPart!.Stylesheet.CellFormats!;
   formats.Append(new CellFormat(new Protection { Locked=false }));
   var ws=wb.WorksheetParts.Single().Worksheet;
   ws.Descendants<Cell>().Single(c=>c.CellReference=="A5").StyleIndex=2;
   ws.Append(new SheetProtection { Sheet=enabled });
  }
  var segments=await service.ScanDocumentAsync(source);
  var locked=segments.Single(s=>s.CellAddress=="B2"); var unlocked=segments.Single(s=>s.CellAddress=="A5");
  Assert.Equal(!enabled,locked.IsSupportedForReplacement); Assert.Single(locked.DetectedDates);
  Assert.True(unlocked.IsSupportedForReplacement);
  locked.IsSupportedForReplacement=true;
  var occurrences=new[]{Occ(locked),Occ(unlocked)}; var dest=Path.Combine(root,"protected.xlsx");
  await service.ReplaceDatesAsync(source,dest,occurrences);
  var output=await service.ScanDocumentAsync(dest);
  Assert.Equal(enabled?"Signed 01/02/2026 end":"Signed 09/10/2027 end",output.Single(s=>s.CellAddress=="B2").LogicalText);
  Assert.Equal("From 09/10/2027 through 03/04/2026.",output.Single(s=>s.CellAddress=="A5").LogicalText);
  var result=await service.VerifyOutputAsync(source,dest,occurrences);
  Assert.True(result.IsPassed,string.Join(";",result.Issues)); Assert.Equal(enabled?1:2,result.ActualReplacementsDone);
  Assert.Equal(enabled?1:2,result.ExpectedReplacements);
 } [Theory] [InlineData(false)] [InlineData(true)]
 public async Task Partial_dates_replace_and_verify_without_changing_native_numeric_dates(bool date1904)
 {
  var source=Create(date1904);
  string numeric;
  using(var doc=SpreadsheetDocument.Open(source,true))
  {
   var cells=doc.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().ToList();
   cells.Single(c=>c.CellReference=="A5").InlineString=new InlineString(new Text("From 1/02 through 29/2; full 01/02/2026."));
   numeric=cells.Single(c=>c.CellReference=="A3").OuterXml;
  }
  var segments=await service.ScanDocumentAsync(source);
  var segment=segments.Single(s=>s.CellAddress=="A5");
  Assert.Equal(3,segment.DetectedDates.Count);
  var target=new DateTime(2028,4,3);
  var occurrences=segment.DetectedDates.Select(c=>new ReplacementOccurrence {
   Segment=segment,Candidate=c,IsSelected=c.Year==null,NewDateTime=target,
   NewDateText=ContractDateReplacer.Core.Helpers.DateFormattingHelper.FormatReplacementDate(c,target)
  }).ToArray();
  var dest=Path.Combine(root,"partial.xlsx");
  Assert.True(await service.ReplaceDatesAsync(source,dest,occurrences));
  Assert.Equal("From 3/04 through 03/4; full 01/02/2026.",(await service.ScanDocumentAsync(dest)).Single(s=>s.CellAddress=="A5").LogicalText);
  using(var doc=SpreadsheetDocument.Open(dest,false))
   Assert.Equal(numeric,doc.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c=>c.CellReference=="A3").OuterXml);
  var result=await service.VerifyOutputAsync(source,dest,occurrences);
  Assert.True(result.IsPassed,string.Join("; ",result.Issues));
  Assert.Equal(2,result.ActualReplacementsDone);
 }}

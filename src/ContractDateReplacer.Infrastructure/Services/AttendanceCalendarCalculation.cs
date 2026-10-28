using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ContractDateReplacer.Infrastructure.Services;

public sealed partial class AttendanceCalendarService
{
    private static void ResetCalculation(WorkbookPart workbook)
    {
        // A cached chain can still point to the old total cells after column expansion.
        // Deleting the part through the SDK also removes its relationship and content type.
        if (workbook.CalculationChainPart is { } chain) workbook.DeletePart(chain);
        RequestCalculation(workbook.Workbook);
    }
    private static void RequestCalculation(Workbook workbook)
    {
        workbook.CalculationProperties ??= new CalculationProperties();
        workbook.CalculationProperties.FullCalculationOnLoad = true;
        workbook.CalculationProperties.ForceFullCalculation = true;
    }
    private static Workbook ExpectedCalculationWorkbook(Workbook source)
    {
        var expected = (Workbook)source.CloneNode(true); RequestCalculation(expected); return expected;
    }
}

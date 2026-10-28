namespace ContractDateReplacer.Core.Models;

public sealed record AttendanceTable(string Id, string SheetName, string SheetRelationshipId,
    int DayRow, int FirstDayColumn, int FirstEmployeeRow, int TotalRow, int TotalColumn,
    string PeriodCell, int SourceYear, int SourceMonth, bool HasTotalRow = true)
{
    public int DayColumnCount => TotalColumn - FirstDayColumn;
    public int LastEmployeeRow => HasTotalRow ? TotalRow - 1 : TotalRow;
    public string DisplayName => $"{SheetName} • hàng {DayRow} • {SourceMonth:00}/{SourceYear}";
}
public sealed record AttendanceDay(int Day, string Weekday, bool IsSunday);
public sealed record AttendanceAnalysis(IReadOnlyList<AttendanceTable> Tables, IReadOnlyList<string> Errors);
public sealed record AttendancePreview(AttendanceTable? Table, int Year, int Month, string PeriodText,
    IReadOnlyList<AttendanceDay> Days, IReadOnlyList<string> Errors, string SourceChecksum = "")
{
    public bool CanExport => Table != null && Errors.Count == 0;
}

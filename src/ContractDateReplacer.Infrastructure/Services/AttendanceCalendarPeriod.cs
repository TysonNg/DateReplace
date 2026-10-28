using System.Globalization;
using System.Text.RegularExpressions;
using ContractDateReplacer.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ContractDateReplacer.Infrastructure.Services;

public sealed partial class AttendanceCalendarService
{
    private const string HeaderDateLiteral = @"\d{1,2}/\d{1,2}(?:/\d{4})?";
    private static readonly Regex HeaderFieldStart = new(@"(?i)^\s*\(?\s*(?:thời\s+gian\s*:?\s*)?(?:tháng\b|từ\b|đến\b)", RegexOptions.Compiled);
    private static readonly Regex HeaderDateLabelOnly = new(@"(?i)^\s*\(?\s*(?<label>từ|đến)\b\s*(?:ngày\b\s*)?:?\s*$", RegexOptions.Compiled);
    private static readonly Regex HeaderLabelledDate = new(@"(?i)\b(?<label>từ|đến)\b\s*(?:ngày\b\s*)?:?\s*(?<date>" + HeaderDateLiteral + @")(?![\d/])", RegexOptions.Compiled);
    private static readonly Regex HeaderDashRange = new(@"(?i)\btừ\b\s*(?:ngày\b\s*)?:?\s*(?<from>" + HeaderDateLiteral + @")\s*[-–—]\s*(?<to>" + HeaderDateLiteral + @")(?![\d/])", RegexOptions.Compiled);

    /// <summary>Only period fields in the selected table's immediate header belong to this operation.</summary>
    private static Dictionary<string, string> PeriodUpdates(WorkbookPart wb, Worksheet ws, AttendanceTable table, int year, int month)
    {
        var edits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cells = Cells(ws); var periodPosition = Position(table.PeriodCell);
        int leftEdge = cells.Values.Where(c => Position(c.CellReference!.Value!).Row == table.DayRow && Position(c.CellReference.Value!).Column < table.FirstDayColumn && Read(wb, c).Trim().Equals("STT", StringComparison.OrdinalIgnoreCase))
            .Select(c => Position(c.CellReference!.Value!).Column).DefaultIfEmpty(1).Min();
        var header = cells.Values.Where(c =>
        {
            var p = Position(c.CellReference!.Value!);
            return c.CellReference == table.PeriodCell || (p.Row >= Math.Max(1, periodPosition.Row - 1) && p.Row < table.DayRow && p.Column >= leftEdge && p.Column <= table.TotalColumn + 1);
        }).ToArray();
        foreach (var cell in header)
        {
            string address = cell.CellReference!.Value!, text = Read(wb, cell);
            if (address != table.PeriodCell && !HeaderFieldStart.IsMatch(text)) continue;
            string value = Period.Replace(text, match => ReplaceHeaderGroups(match,
                ("month", month.ToString(match.Groups["month"].Length == 2 ? "D2" : "D", CultureInfo.InvariantCulture)),
                ("year", year.ToString("D4", CultureInfo.InvariantCulture))));
            value = HeaderDashRange.Replace(value, match => ReplaceHeaderGroups(match,
                ("from", HeaderDate(match.Groups["from"].Value, year, month, 1)),
                ("to", HeaderDate(match.Groups["to"].Value, year, month, DateTime.DaysInMonth(year, month)))));
            value = HeaderLabelledDate.Replace(value, match => ReplaceHeaderGroups(match,
                ("date", HeaderDate(match.Groups["date"].Value, year, month, match.Groups["label"].Value.Equals("từ", StringComparison.OrdinalIgnoreCase) ? 1 : DateTime.DaysInMonth(year, month)))));
            if (value != text || address == table.PeriodCell) edits[address] = value;

            // Also support a label and its date in neighboring cells, including merged label cells.
            var label = HeaderDateLabelOnly.Match(text);
            if (!label.Success) continue;
            var p = Position(address);
            var next = header.Where(c => Position(c.CellReference!.Value!).Row == p.Row && Position(c.CellReference.Value!).Column > p.Column && !string.IsNullOrWhiteSpace(Read(wb, c)))
                .OrderBy(c => Position(c.CellReference!.Value!).Column).FirstOrDefault();
            if (next == null) continue;
            string nextText = Read(wb, next);
            if (!Regex.IsMatch(nextText.Trim(), "^" + HeaderDateLiteral + "$")) continue;
            int day = label.Groups["label"].Value.Equals("từ", StringComparison.OrdinalIgnoreCase) ? 1 : DateTime.DaysInMonth(year, month);
            edits[next.CellReference!.Value!] = HeaderDate(nextText.Trim(), year, month, day);
        }
        return edits;
    }
    private static string HeaderDate(string original, int year, int month, int day)
    {
        string[] parts = original.Split('/');
        string result = day.ToString(parts[0].Length == 2 ? "D2" : "D", CultureInfo.InvariantCulture) + "/" + month.ToString(parts[1].Length == 2 ? "D2" : "D", CultureInfo.InvariantCulture);
        return parts.Length == 3 ? result + "/" + year.ToString("D4", CultureInfo.InvariantCulture) : result;
    }
    private static string ReplaceHeaderGroups(Match match, params (string Name, string Value)[] edits)
    {
        string value = match.Value;
        foreach (var edit in edits.OrderByDescending(e => match.Groups[e.Name].Index))
        {
            var group = match.Groups[edit.Name]; int start = group.Index - match.Index;
            value = value.Remove(start, group.Length).Insert(start, edit.Value);
        }
        return value;
    }
}

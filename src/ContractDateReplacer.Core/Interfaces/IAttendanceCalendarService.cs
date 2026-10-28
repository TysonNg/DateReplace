using ContractDateReplacer.Core.Models;
namespace ContractDateReplacer.Core.Interfaces;
public interface IAttendanceCalendarService
{
    AttendanceAnalysis Analyze(string path);
    AttendancePreview Preview(string path, string tableId, int year, int month);
    void Export(string source, string destination, string tableId, int year, int month, string? expectedSourceChecksum = null);
}

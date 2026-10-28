namespace ContractDateReplacer.App.Services;
public interface IAttendanceFileDialogService
{
    string? OpenWorkbook();
    string? SaveWorkbook(string suggestedPath);
}

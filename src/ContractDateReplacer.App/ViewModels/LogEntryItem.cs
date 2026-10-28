using System;

namespace ContractDateReplacer.App.ViewModels;

/// <summary>
/// Đại diện cho một dòng nhật ký hoạt động trên màn hình Execution & Progress View.
/// </summary>
public class LogEntryItem
{
    public string Timestamp { get; }
    public string Message { get; }
    public string Level { get; }

    public LogEntryItem(string message, string level = "Info")
    {
        Timestamp = DateTime.Now.ToString("HH:mm:ss");
        Message = message;
        Level = level;
    }

    public string FormattedText => $"{Timestamp} - {Message}";

    public string ForegroundColor => Level switch
    {
        "Success" => "#FF107C41",
        "Error" => "#FFE81123",
        "Warning" => "#FFF5A623",
        _ => "#FFCCCCCC"
    };
}

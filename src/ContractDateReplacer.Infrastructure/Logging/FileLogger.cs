using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.Infrastructure.Logging;

/// <summary>
/// Logger ghi trực tiếp nhật ký vào thư mục logs của ứng dụng với tính năng tự động chia file theo ngày.
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logDirectory;
    private static readonly object _lock = new();

    public FileLogger(string categoryName, string logDirectory)
    {
        _categoryName = categoryName;
        _logDirectory = logDirectory;

        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logLine = $"[{timestamp}] [{logLevel,-11}] [{_categoryName}] {message}";

        if (exception != null)
        {
            logLine += Environment.NewLine + exception.ToString();
        }

        string logFile = Path.Combine(_logDirectory, $"app_{DateTime.Now:yyyyMMdd}.log");

        lock (_lock)
        {
            try
            {
                File.AppendAllText(logFile, logLine + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Tránh throw exception từ logger
            }
        }
    }
}

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _logDirectory);
    }

    public void Dispose() { }
}

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFileLogging(this ILoggingBuilder builder, string logDirectory)
    {
        builder.AddProvider(new FileLoggerProvider(logDirectory));
        return builder;
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ContractDateReplacer.App.Services;
using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.App.Views;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Services;
using ContractDateReplacer.Infrastructure.Logging;
using ContractDateReplacer.Infrastructure.Services;
using ContractDateReplacer.Persistence;
using ContractDateReplacer.Persistence.Repositories;
using ContractDateReplacer.Persistence.Services;
using ContractDateReplacer.Word;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ContractDateReplacer.App;

public partial class App : Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    public App()
    {
        SetupGlobalExceptionHandling();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appData = new AppDataService();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddFileLogging(appData.LogsDirectory);
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                // Infrastructure
                // Infrastructure
                services.AddSingleton<IAppDataService>(appData);
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IExportService, ExportService>();
                services.AddSingleton<IWebhookNotificationService, WebhookNotificationService>();
                services.AddSingleton<IBatchProcessingService, HighPerformanceBatchProcessor>();

                // Persistence
                services.AddSingleton<DatabaseInitializer>();
                services.AddSingleton<IRuleRepository, SqliteRuleRepository>();
                services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
                services.AddSingleton<IAuditLogService, AuditLogService>();
                services.AddSingleton<RuleExportImportService>();
                services.AddSingleton<IBackupRollbackService, BackupRollbackService>();
                services.AddSingleton<IPresetRepository, SqlitePresetRepository>();

                // Core Detection & Classification & Presets & Intelligence & OCR
                services.AddSingleton<IDateDetector, DateDetector>();
                services.AddSingleton<IDateClassifier, DateClassifier>();
                services.AddSingleton<IDateIntelligenceService, DateIntelligenceService>();
                services.AddSingleton<IPresetApplicationService, PresetApplicationService>();
                services.AddSingleton<IOcrService, SmartOcrService>();

                // Word OpenXML & Image Extraction
                services.AddSingleton<WordDocumentScanner>();
                services.AddSingleton<ExcelDocumentService>();
                services.AddSingleton<DocumentFormatRouter>();
                services.AddSingleton<IDocumentScanner>(sp => sp.GetRequiredService<DocumentFormatRouter>());
                services.AddSingleton<WordDocumentReplacementService>();
                services.AddSingleton<IDocumentReplacementService>(sp => sp.GetRequiredService<DocumentFormatRouter>());
                services.AddSingleton<WordDocumentVerificationService>(sp => new WordDocumentVerificationService(
                    sp.GetRequiredService<WordDocumentScanner>(), sp.GetRequiredService<IDateDetector>(),
                    sp.GetRequiredService<WordOpenXmlValidator>(), sp.GetRequiredService<ILogger<WordDocumentVerificationService>>()));
                services.AddSingleton<IDocumentVerificationService>(sp => sp.GetRequiredService<DocumentFormatRouter>());
                services.AddSingleton<IWordImageExtractionService, WordImageExtractionService>();
                services.AddSingleton<WordOpenXmlValidator>();

                // UI & ViewModels & Dialogs
                services.AddSingleton<IFileDialogService, FileDialogService>();
                services.AddSingleton<IAttendanceCalendarService, AttendanceCalendarService>();
                services.AddSingleton<IAttendanceFileDialogService, AttendanceFileDialogService>();
                services.AddSingleton<AttendanceCalendarViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<ReviewViewModel>();
                services.AddTransient<ReviewView>();
                services.AddSingleton<PreviewViewModel>();
                services.AddTransient<PreviewView>();
                services.AddSingleton<ExecutionViewModel>();
                services.AddTransient<ExecutionView>();
                services.AddSingleton<ReportViewModel>();
                services.AddTransient<ReportView>();
                services.AddSingleton<HistoryRollbackViewModel>();
                services.AddTransient<HistoryRollbackView>();
                services.AddSingleton<RuleManagementViewModel>();
                services.AddTransient<RuleManagementView>();
                services.AddSingleton<PresetManagementViewModel>();
                services.AddTransient<PresetManagementView>();
                services.AddSingleton<AuditComplianceViewModel>();
                services.AddTransient<AuditLogView>();
                services.AddSingleton<SettingsViewModel>();
                services.AddTransient<SettingsView>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Application starting up...");

        try
        {
            // Khởi tạo Database SQLite và nạp rules mẫu
            var dbInitializer = _host.Services.GetRequiredService<DatabaseInitializer>();
            await dbInitializer.InitializeAsync();

            var ruleRepo = _host.Services.GetRequiredService<IRuleRepository>();
            await ruleRepo.EnsureSeedRulesAsync();
            _logger.LogInformation("Database initialized and seed rules verified.");

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Lỗi nghiêm trọng khi khởi động ứng dụng.");
            MessageBox.Show(
                $"Đã xảy ra lỗi khi khởi động ứng dụng:{Environment.NewLine}{ex.Message}",
                "Lỗi khởi động",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Application exiting (ExitCode: {ExitCode})", e.ApplicationExitCode);

        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private void SetupGlobalExceptionHandling()
    {
        // UI Thread Exception Handler
        DispatcherUnhandledException += (sender, e) =>
        {
            _logger?.LogError(e.Exception, "Unhandled UI exception caught by Dispatcher.");
            MessageBox.Show(
                $"Đã xảy ra sự cố không mong muốn trong giao diện:{Environment.NewLine}{e.Exception.Message}",
                "Cảnh báo hệ thống",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true; // Ngăn chặn crash app đột ngột
        };

        // AppDomain Unhandled Exception Handler
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            _logger?.LogCritical(ex, "Unhandled AppDomain exception. IsTerminating: {IsTerminating}", e.IsTerminating);
        };

        // Unobserved Task Exception Handler
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            _logger?.LogError(e.Exception, "Unobserved Task exception caught.");
            e.SetObserved(); // Đánh dấu đã xử lý
        };
    }
}


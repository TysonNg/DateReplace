using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace ContractDateReplacer.Persistence;

/// <summary>
/// Khởi tạo và nâng cấp schema cơ sở dữ liệu SQLite cục bộ.
/// </summary>
public class DatabaseInitializer
{
    private readonly IAppDataService _appDataService;

    public DatabaseInitializer(IAppDataService appDataService)
    {
        _appDataService = appDataService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _appDataService.EnsureDirectoriesExist();

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appDataService.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        string createTablesSql = @"
            CREATE TABLE IF NOT EXISTS Rules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Keywords TEXT NOT NULL,
                TargetType INTEGER NOT NULL,
                Priority INTEGER NOT NULL DEFAULT 100,
                DefaultConfidence REAL NOT NULL DEFAULT 0.95,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                IsBuiltIn INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS AuditLogs (
                BatchId TEXT PRIMARY KEY,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT,
                TotalFiles INTEGER NOT NULL,
                PassedFilesCount INTEGER NOT NULL,
                ReviewRequiredFilesCount INTEGER NOT NULL,
                ErrorFilesCount INTEGER NOT NULL,
                SourceDirectory TEXT,
                OutputDirectory TEXT,
                DetailsJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Backups (
                BackupId TEXT PRIMARY KEY,
                BatchId TEXT NOT NULL,
                OriginalPath TEXT NOT NULL,
                BackupPath TEXT NOT NULL,
                TargetModifiedPath TEXT,
                ChecksumSHA256 TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ReplacementCount INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Available'
            );

            CREATE TABLE IF NOT EXISTS Presets (
                PresetId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT,
                CreatedAt TEXT NOT NULL,
                ConfigJson TEXT NOT NULL,
                OutputMode TEXT NOT NULL DEFAULT 'Suffix',
                SuffixPattern TEXT NOT NULL DEFAULT '_2026',
                CustomFolder TEXT,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                IsBuiltIn INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ComplianceLogs (
                LogId TEXT PRIMARY KEY,
                Timestamp TEXT NOT NULL,
                ActionType INTEGER NOT NULL,
                UserName TEXT,
                MachineName TEXT,
                BatchId TEXT,
                TargetFile TEXT,
                DetailsJson TEXT NOT NULL,
                Severity INTEGER NOT NULL DEFAULT 0,
                IntegrityHash TEXT NOT NULL
            );
        ";

        await using var command = new SqliteCommand(createTablesSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

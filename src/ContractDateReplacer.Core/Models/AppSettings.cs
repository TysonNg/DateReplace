using System;

namespace ContractDateReplacer.Core.Models;

/// <summary>
/// Cấu hình toàn diện của ứng dụng Contract Date Replacer.
/// </summary>
public class AppSettings
{
    // 1. General Settings
    public string Language { get; set; } = "vi-VN";
    public string Theme { get; set; } = "Dark"; // "Dark" | "Light" | "System"
    public bool AutoCheckUpdates { get; set; } = true;

    // 2. Performance & Processing
    public int MaxDegreeOfParallelism { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
    public int ScanTimeoutSecondsPerFile { get; set; } = 30;
    public bool EnableOpenXmlValidation { get; set; } = true;

    // 3. Safety & Backup
    public int BackupRetentionDays { get; set; } = 30;
    public bool AutoBackupBeforeReplacement { get; set; } = true;
    public bool VerifyChecksumAfterRestore { get; set; } = true;
    public bool WarnOnLowConfidenceDates { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = 0.85;

    // 4. Date Replacement Defaults
    public string DefaultDateFormat { get; set; } = "dd/MM/yyyy";
    public string DefaultOutputMode { get; set; } = "Suffix"; // "Suffix" | "NewFolder" | "InPlace"
    public string DefaultSuffixPattern { get; set; } = "_2026";
    public string DefaultOutputFolder { get; set; } = string.Empty;

    // 5. Privacy & Compliance
    public bool MaskPersonalDataInLogs { get; set; } = true;
    public int AuditLogRetentionDays { get; set; } = 90;

    /// <summary>
    /// Kiểm tra và chuẩn hóa các giá trị nằm trong ngưỡng hợp lệ an toàn.
    /// </summary>
    public void ValidateAndNormalize()
    {
        MaxDegreeOfParallelism = Math.Clamp(MaxDegreeOfParallelism, 1, 32);
        ScanTimeoutSecondsPerFile = Math.Clamp(ScanTimeoutSecondsPerFile, 5, 300);
        BackupRetentionDays = Math.Clamp(BackupRetentionDays, 1, 3650);
        AuditLogRetentionDays = Math.Clamp(AuditLogRetentionDays, 1, 3650);
        ConfidenceThreshold = Math.Clamp(ConfidenceThreshold, 0.1, 1.0);

        if (string.IsNullOrWhiteSpace(Language)) Language = "vi-VN";
        if (string.IsNullOrWhiteSpace(Theme)) Theme = "Dark";
        if (string.IsNullOrWhiteSpace(DefaultDateFormat)) DefaultDateFormat = "dd/MM/yyyy";
        if (string.IsNullOrWhiteSpace(DefaultOutputMode)) DefaultOutputMode = "Suffix";
        if (string.IsNullOrWhiteSpace(DefaultSuffixPattern)) DefaultSuffixPattern = "_2026";
    }

    /// <summary>
    /// Nhân bản cấu hình sâu (Deep Clone).
    /// </summary>
    public AppSettings Clone()
    {
        return (AppSettings)this.MemberwiseClone();
    }
}

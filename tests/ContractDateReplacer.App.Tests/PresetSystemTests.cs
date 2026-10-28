using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using ContractDateReplacer.Persistence;
using ContractDateReplacer.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ContractDateReplacer.App.Tests;

public class PresetSystemTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _dbPath;
    private readonly Mock<IAppDataService> _mockAppData;
    private readonly DatabaseInitializer _initializer;
    private readonly SqlitePresetRepository _repository;
    private readonly PresetApplicationService _applicationService;

    public PresetSystemTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PresetSystemTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _dbPath = Path.Combine(_tempDirectory, "presets_test.db");

        _mockAppData = new Mock<IAppDataService>();
        _mockAppData.Setup(a => a.RootDirectory).Returns(_tempDirectory);
        _mockAppData.Setup(a => a.DatabasePath).Returns(_dbPath);

        _initializer = new DatabaseInitializer(_mockAppData.Object);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _repository = new SqlitePresetRepository(_mockAppData.Object, NullLogger<SqlitePresetRepository>.Instance);
        _applicationService = new PresetApplicationService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task Preset_CrudOperations_SucceedInSqlite()
    {
        // 1. Create
        var newPreset = new PresetModel
        {
            PresetId = "CUSTOM_PRESET_01",
            Name = "Mẫu hợp đồng thử nghiệm",
            Description = "Mô tả mẫu",
            OutputMode = "Suffix",
            SuffixPattern = "_Test",
            IsDefault = false,
            IsBuiltIn = false,
            DateMappingRules = new List<DateOffsetRule>
            {
                new()
                {
                    TargetClassificationType = DateClassificationType.ContractSignDate,
                    Mode = DateOffsetMode.CurrentDate
                }
            }
        };

        bool saved = await _repository.SavePresetAsync(newPreset);
        saved.Should().BeTrue();

        // 2. Read
        var retrieved = await _repository.GetPresetByIdAsync("CUSTOM_PRESET_01");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Mẫu hợp đồng thử nghiệm");
        retrieved.DateMappingRules.Should().HaveCount(1);
        retrieved.IsBuiltIn.Should().BeFalse();

        // 3. Update
        retrieved.Name = "Mẫu hợp đồng đã cập nhật";
        retrieved.DateMappingRules.Add(new DateOffsetRule
        {
            TargetClassificationType = DateClassificationType.ContractEndDate,
            Mode = DateOffsetMode.OffsetYearsFromSelf,
            OffsetYears = 2
        });

        await _repository.SavePresetAsync(retrieved);

        var updated = await _repository.GetPresetByIdAsync("CUSTOM_PRESET_01");
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Mẫu hợp đồng đã cập nhật");
        updated.DateMappingRules.Should().HaveCount(2);

        // 4. Delete custom
        bool deleted = await _repository.DeletePresetAsync("CUSTOM_PRESET_01");
        deleted.Should().BeTrue();

        var checkDeleted = await _repository.GetPresetByIdAsync("CUSTOM_PRESET_01");
        checkDeleted.Should().BeNull();

        // 5. Thử xóa mẫu BuiltIn -> Phải ném ngoại lệ
        await _repository.EnsureSeedPresetsAsync();
        var allPresets = await _repository.GetAllPresetsAsync();
        var builtIn = allPresets.First(p => p.IsBuiltIn);

        var act = async () => await _repository.DeletePresetAsync(builtIn.PresetId);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Không thể xóa mẫu thiết lập mặc định*");
    }

    [Fact]
    public async Task ApplyPreset_GiaHan1Nam_CalculatesCorrectTargetDates()
    {
        await _repository.EnsureSeedPresetsAsync();
        var allPresets = await _repository.GetAllPresetsAsync();
        var giaHanPreset = allPresets.First(p => p.Name == "Gia hạn 1 năm");

        var referenceDate = new DateTime(2026, 9, 3);
        var originalDates = new Dictionary<DateClassificationType, DateTime>
        {
            [DateClassificationType.ContractSignDate] = new DateTime(2025, 1, 15),
            [DateClassificationType.ContractStartDate] = new DateTime(2025, 2, 1),
            [DateClassificationType.ContractEndDate] = new DateTime(2026, 1, 31)
        };

        var resolved = _applicationService.ResolveTargetDates(giaHanPreset, originalDates, referenceDate);

        // Ngày ký = Hôm nay (03/09/2026)
        resolved[DateClassificationType.ContractSignDate].Should().Be(new DateTime(2026, 9, 3));

        // Ngày bắt đầu = Hôm nay (03/09/2026)
        resolved[DateClassificationType.ContractStartDate].Should().Be(new DateTime(2026, 9, 3));

        // Ngày kết thúc = +1 năm từ Ngày bắt đầu (03/09/2027)
        resolved[DateClassificationType.ContractEndDate].Should().Be(new DateTime(2027, 9, 3));
    }

    [Fact]
    public void Preset_OffsetFromAnotherDate_CalculatesDependencyChainCorrectly()
    {
        var customPreset = new PresetModel
        {
            PresetId = "PRESET_CHAIN",
            Name = "Chuỗi ngày phụ thuộc",
            DateMappingRules = new List<DateOffsetRule>
            {
                new()
                {
                    TargetClassificationType = DateClassificationType.ContractSignDate,
                    Mode = DateOffsetMode.FixedDate,
                    FixedValue = new DateTime(2026, 10, 1)
                },
                new()
                {
                    TargetClassificationType = DateClassificationType.ContractStartDate,
                    Mode = DateOffsetMode.OffsetFromAnotherDate,
                    BaseDateType = DateClassificationType.ContractSignDate,
                    OffsetDays = 15 // Ngày bắt đầu sau ngày ký 15 ngày
                },
                new()
                {
                    TargetClassificationType = DateClassificationType.ContractEndDate,
                    Mode = DateOffsetMode.OffsetFromAnotherDate,
                    BaseDateType = DateClassificationType.ContractStartDate,
                    OffsetMonths = 6 // Ngày kết thúc sau ngày bắt đầu 6 tháng
                }
            }
        };

        var resolved = _applicationService.ResolveTargetDates(customPreset);

        // 1. Ngày ký = 01/10/2026
        resolved[DateClassificationType.ContractSignDate].Should().Be(new DateTime(2026, 10, 1));

        // 2. Ngày bắt đầu = 01/10/2026 + 15 ngày = 16/10/2026
        resolved[DateClassificationType.ContractStartDate].Should().Be(new DateTime(2026, 10, 16));

        // 3. Ngày kết thúc = 16/10/2026 + 6 tháng = 16/04/2027
        resolved[DateClassificationType.ContractEndDate].Should().Be(new DateTime(2027, 4, 16));
    }

    [Fact]
    public async Task EnsureSeedPresetsAsync_Seeds3DefaultPresetsOnStartup()
    {
        await _repository.EnsureSeedPresetsAsync();

        var presets = await _repository.GetAllPresetsAsync();
        presets.Should().HaveCountGreaterOrEqualTo(3);

        presets.Should().Contain(p => p.Name == "Gia hạn 1 năm");
        presets.Should().Contain(p => p.Name == "Đổi ngày sang năm mới");
        presets.Should().Contain(p => p.Name == "Đặt ngày ký hôm nay");

        var defaultPreset = await _repository.GetDefaultPresetAsync();
        defaultPreset.Should().NotBeNull();
        defaultPreset!.Name.Should().Be("Gia hạn 1 năm");
        defaultPreset.IsDefault.Should().BeTrue();
    }
}

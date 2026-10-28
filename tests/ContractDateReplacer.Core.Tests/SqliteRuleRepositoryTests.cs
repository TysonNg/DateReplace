using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Infrastructure.Services;
using ContractDateReplacer.Persistence;
using ContractDateReplacer.Persistence.Repositories;
using ContractDateReplacer.Persistence.Services;
using FluentAssertions;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class SqliteRuleRepositoryTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly AppDataService _appDataService;
    private readonly DatabaseInitializer _dbInitializer;
    private readonly SqliteRuleRepository _ruleRepository;

    public SqliteRuleRepositoryTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SqliteRuleTests_" + Guid.NewGuid().ToString("N"));
        _appDataService = new AppDataService(_testDirectory);
        _dbInitializer = new DatabaseInitializer(_appDataService);
        _ruleRepository = new SqliteRuleRepository(_appDataService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try { Directory.Delete(_testDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task EnsureSeedRules_ShouldPopulateDefaultRules()
    {
        await _dbInitializer.InitializeAsync();
        await _ruleRepository.EnsureSeedRulesAsync();

        var rules = await _ruleRepository.GetAllRulesAsync();

        rules.Should().NotBeEmpty();
        rules.Count.Should().BeGreaterOrEqualTo(10);
        rules.Should().Contain(r => r.TargetType == DateClassificationType.BirthDate);
        rules.Should().Contain(r => r.TargetType == DateClassificationType.ContractSignDate);
        rules.Should().Contain(r => r.TargetType == DateClassificationType.IdentityIssueDate);
    }

    [Fact]
    public async Task SaveRule_And_DeleteRule_ShouldWorkCorrectly()
    {
        await _dbInitializer.InitializeAsync();

        var customRule = new ClassificationRule
        {
            Name = "Quy tắc thử nghiệm",
            Keywords = new List<string> { "thử nghiệm", "test" },
            TargetType = DateClassificationType.DocumentDate,
            Priority = 50,
            DefaultConfidence = 0.88,
            IsEnabled = true
        };

        int id = await _ruleRepository.SaveRuleAsync(customRule);
        id.Should().BeGreaterThan(0);

        var retrieved = await _ruleRepository.GetRuleByIdAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Quy tắc thử nghiệm");
        retrieved.Keywords.Should().Contain("thử nghiệm");

        // Update
        retrieved.Name = "Quy tắc đã sửa";
        await _ruleRepository.SaveRuleAsync(retrieved);
        var updated = await _ruleRepository.GetRuleByIdAsync(id);
        updated!.Name.Should().Be("Quy tắc đã sửa");

        // Delete
        bool deleted = await _ruleRepository.DeleteRuleAsync(id);
        deleted.Should().BeTrue();

        var afterDelete = await _ruleRepository.GetRuleByIdAsync(id);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task ExportAndImportRules_ShouldPreserveRuleData()
    {
        await _dbInitializer.InitializeAsync();
        await _ruleRepository.EnsureSeedRulesAsync();

        var exportImport = new RuleExportImportService(_ruleRepository);
        string exportFile = Path.Combine(_testDirectory, "exported_rules.json");

        await exportImport.ExportRulesToJsonAsync(exportFile);
        File.Exists(exportFile).Should().BeTrue();

        string jsonContent = await File.ReadAllTextAsync(exportFile);
        jsonContent.Should().Contain("Ngày sinh");

        // Import
        int importedCount = await exportImport.ImportRulesFromJsonAsync(exportFile, overwriteExisting: false);
        importedCount.Should().BeGreaterOrEqualTo(10);
    }
}

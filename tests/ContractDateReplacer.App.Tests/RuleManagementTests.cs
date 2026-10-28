using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContractDateReplacer.App.ViewModels;
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

public class RuleManagementTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _dbPath;
    private readonly Mock<IAppDataService> _mockAppData;
    private readonly DatabaseInitializer _initializer;
    private readonly SqliteRuleRepository _repository;
    private readonly DateDetector _detector;
    private readonly DateClassifier _classifier;

    public RuleManagementTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "RuleManagementTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _dbPath = Path.Combine(_tempDirectory, "rules_test.db");

        _mockAppData = new Mock<IAppDataService>();
        _mockAppData.Setup(a => a.RootDirectory).Returns(_tempDirectory);
        _mockAppData.Setup(a => a.DatabasePath).Returns(_dbPath);

        _initializer = new DatabaseInitializer(_mockAppData.Object);
        _initializer.InitializeAsync().GetAwaiter().GetResult();

        _repository = new SqliteRuleRepository(_mockAppData.Object);
        _repository.EnsureSeedRulesAsync().GetAwaiter().GetResult();

        _detector = new DateDetector();
        _classifier = new DateClassifier(_repository);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, true); } catch { }
        }
    }

    [Fact]
    public async Task AddNewRule_SavesToSqliteSuccessfully()
    {
        var newRule = new ClassificationRule
        {
            Name = "Quy tắc thời hạn bảo hành",
            Keywords = new List<string> { "bảo hành từ ngày", "thời hạn bảo hành", "bảo hành đến" },
            TargetType = DateClassificationType.ContractEndDate,
            Priority = 75,
            DefaultConfidence = 0.92,
            IsEnabled = true,
            IsBuiltIn = false
        };

        int newId = await _repository.SaveRuleAsync(newRule);
        newId.Should().BeGreaterThan(0);

        var retrieved = await _repository.GetRuleByIdAsync(newId);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Quy tắc thời hạn bảo hành");
        retrieved.Keywords.Should().Contain("bảo hành từ ngày");
        retrieved.IsBuiltIn.Should().BeFalse();
        retrieved.Priority.Should().Be(75);
    }

    [Fact]
    public async Task EditCustomRule_UpdatesInSqliteSuccessfully()
    {
        var rule = new ClassificationRule
        {
            Name = "Luật thử nghiệm",
            Keywords = new List<string> { "từ khóa 1" },
            TargetType = DateClassificationType.PaymentDate,
            Priority = 60,
            DefaultConfidence = 0.85,
            IsEnabled = true,
            IsBuiltIn = false
        };

        int id = await _repository.SaveRuleAsync(rule);

        // Chỉnh sửa luật
        rule.Name = "Luật thử nghiệm đã sửa";
        rule.Keywords.Add("từ khóa 2");
        rule.Priority = 85;

        await _repository.SaveRuleAsync(rule);

        var updated = await _repository.GetRuleByIdAsync(id);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Luật thử nghiệm đã sửa");
        updated.Keywords.Should().HaveCount(2);
        updated.Priority.Should().Be(85);
    }

    [Fact]
    public async Task DeleteCustomRule_Succeeds_AndBuiltInRuleCannotBeDeleted()
    {
        var allRules = await _repository.GetAllRulesAsync();
        var builtInRule = allRules.First(r => r.IsBuiltIn);

        // 1. Cố tình xóa luật mặc định -> Phải throw exception
        var act = async () => await _repository.DeleteRuleAsync(builtInRule.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Không thể xóa quy tắc mặc định*");

        // 2. Thêm luật custom và xóa -> Phải thành công
        var customRule = new ClassificationRule
        {
            Name = "Luật tạm xóa",
            Keywords = new List<string> { "xóa tôi đi" },
            TargetType = DateClassificationType.Other,
            IsBuiltIn = false
        };
        int customId = await _repository.SaveRuleAsync(customRule);

        bool deleted = await _repository.DeleteRuleAsync(customId);
        deleted.Should().BeTrue();

        var checkDeleted = await _repository.GetRuleByIdAsync(customId);
        checkDeleted.Should().BeNull();
    }

    [Fact]
    public async Task ResetDefaultRules_RestoresOriginal12Rules()
    {
        // Thêm một custom rule
        await _repository.SaveRuleAsync(new ClassificationRule
        {
            Name = "Custom Rule Để Giữ",
            Keywords = new List<string> { "giữ tôi" },
            TargetType = DateClassificationType.Other,
            IsBuiltIn = false
        });

        // Reset về mặc định
        await _repository.ResetDefaultRulesAsync();

        var rules = await _repository.GetAllRulesAsync();
        var builtInCount = rules.Count(r => r.IsBuiltIn);

        builtInCount.Should().Be(12);
        rules.Should().Contain(r => r.Name == "Ngày sinh cá nhân");
        rules.Should().Contain(r => r.Name == "Ngày cấp CCCD/CMND/Hộ chiếu");
        rules.Should().Contain(r => r.Name == "Ngày ký hợp đồng");
        rules.Should().Contain(r => r.Name == "Ngày hiệu lực hợp đồng");
    }

    [Fact]
    public async Task RuleTester_ClassifiesSampleTextCorrectly()
    {
        var vm = new RuleManagementViewModel(
            _repository,
            _detector,
            _classifier,
            NullLogger<RuleManagementViewModel>.Instance);

        vm.TestInputText = "Hôm nay, ngày 02/09/2026 hai bên cùng thống nhất ký kết hợp đồng dịch vụ.";
        await vm.RunRuleTesterAsync();

        vm.HasTestResult.Should().BeTrue();
        vm.TestResultType.Should().Be("ContractSignDate");
        vm.TestResultText.Should().Contain("02/09/2026");
        vm.TestResultText.Should().Contain("CÓ");
    }
}

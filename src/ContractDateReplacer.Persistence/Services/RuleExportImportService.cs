using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Persistence.Services;

/// <summary>
/// Dịch vụ xuất nhập bộ quy tắc ra/vào định dạng file JSON để chia sẻ giữa các máy tính.
/// </summary>
public class RuleExportImportService
{
    private readonly IRuleRepository _ruleRepository;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RuleExportImportService(IRuleRepository ruleRepository)
    {
        _ruleRepository = ruleRepository;
    }

    public async Task ExportRulesToJsonAsync(string targetFilePath, CancellationToken cancellationToken = default)
    {
        var rules = await _ruleRepository.GetAllRulesAsync(cancellationToken);
        string json = JsonSerializer.Serialize(rules, _jsonOptions);
        await File.WriteAllTextAsync(targetFilePath, json, cancellationToken);
    }

    public async Task<int> ImportRulesFromJsonAsync(string sourceFilePath, bool overwriteExisting = false, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath)) return 0;

        string json = await File.ReadAllTextAsync(sourceFilePath, cancellationToken);
        var importedRules = JsonSerializer.Deserialize<List<ClassificationRule>>(json);
        if (importedRules == null) return 0;

        int count = 0;
        foreach (var rule in importedRules)
        {
            if (!overwriteExisting)
            {
                rule.Id = 0; // Luôn thêm mới nếu không ghi đè
            }
            await _ruleRepository.SaveRuleAsync(rule, cancellationToken);
            count++;
        }

        return count;
    }
}

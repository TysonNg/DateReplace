using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Interfaces;

/// <summary>
/// Quản lý truy xuất và lưu trữ bộ quy tắc phân loại ngữ cảnh ngày.
/// </summary>
public interface IRuleRepository
{
    Task<List<ClassificationRule>> GetAllRulesAsync(CancellationToken cancellationToken = default);
    Task<ClassificationRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveRuleAsync(ClassificationRule rule, CancellationToken cancellationToken = default);
    Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default);
    Task EnsureSeedRulesAsync(CancellationToken cancellationToken = default);
    Task ResetDefaultRulesAsync(CancellationToken cancellationToken = default);
}

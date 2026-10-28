using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;

namespace ContractDateReplacer.Core.Services;

/// <summary>
/// Bộ máy phân loại ngữ cảnh ngày tháng trong hợp đồng (Semantic Date Classification Engine).
/// Kết hợp Rule-based từ SQLite, Keyword proximity weighting, Heuristics địa danh và vị trí văn bản.
/// </summary>
public class DateClassifier : IDateClassifier
{
    private readonly IRuleRepository? _ruleRepository;
    private List<ClassificationRule>? _cachedRules;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Regex phát hiện tiền tố địa danh (TP.HCM, Hà Nội, Đà Nẵng, Bình Dương...) đứng trước ngày
    private static readonly Regex RegexGeographicPrefix = new(
        @"(?i)(tp\.?\s*hcm|thành phố hồ chí minh|hà nội|đà nẵng|hải phòng|cần thơ|bình dương|đồng nai|quảng ninh|bà rịa|vũng tàu|long an|bắc ninh|tỉnh\s+[\w\s]+|tp\.?\s*[\w\s]+)[,\s]+(ngày)?\s*$",
        RegexOptions.Compiled);

    public DateClassifier(IRuleRepository? ruleRepository = null)
    {
        _ruleRepository = ruleRepository;
    }

    public async Task<ClassificationResult> ClassifyAsync(DateCandidate candidate, CancellationToken cancellationToken = default)
    {
        var rules = await GetRulesAsync(cancellationToken);
        return ClassifyCandidate(candidate, rules);
    }

    public async Task ClassifyBatchAsync(IEnumerable<DateCandidate> candidates, CancellationToken cancellationToken = default, DocumentTextSegment? segment = null)
    {
        var rules = await GetRulesAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidate.Classification = ClassifyCandidate(candidate, rules, segment);
        }
    }

    /// <summary>
    /// Phương thức phân loại đồng bộ (dùng trong test hoặc khi đã nạp sẵn rules).
    /// </summary>
    public ClassificationResult Classify(DateCandidate candidate, DocumentTextSegment? segment = null)
    {
        var rules = _cachedRules ?? GetFallbackRules();
        return ClassifyCandidate(candidate, rules, segment);
    }

    private async Task<List<ClassificationRule>> GetRulesAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_ruleRepository != null)
            {
                var dbRules = await _ruleRepository.GetAllRulesAsync(cancellationToken);
                var activeRules = dbRules.Where(r => r.IsEnabled).ToList();
                if (dbRules.Count > 0)
                {
                    _cachedRules = activeRules;
                    return _cachedRules;
                }
            }

            _cachedRules = GetFallbackRules();
            return _cachedRules;
        }
        finally
        {
            _lock.Release();
        }
    }

    private ClassificationResult ClassifyCandidate(
        DateCandidate candidate,
        List<ClassificationRule> rules,
        DocumentTextSegment? segment = null)
    {
        if (candidate == null || rules.Count == 0)
            return new ClassificationResult
            {
                Type = DateClassificationType.Unknown,
                Confidence = 0.0,
                IsRecommendedForReplacement = false,
                Explanation = "Không có quy tắc phân loại đang bật."
            };

        // Spreadsheet headings commonly reverse the wording used in contract prose.
        string contextBefore = candidate.ContextBefore;
        if (segment?.StoryType == StoryType.SpreadsheetCell)
            contextBefore = contextBefore
                .Replace("ngày ký", "ký ngày", StringComparison.OrdinalIgnoreCase)
                .Replace("ngày hiệu lực", "có hiệu lực từ ngày", StringComparison.OrdinalIgnoreCase)
                .Replace("ngày thanh toán", "thời hạn thanh toán vào ngày", StringComparison.OrdinalIgnoreCase);
        string before = contextBefore.Trim();
        string after = candidate.ContextAfter.Trim();

        // Chuẩn hóa khoảng trắng để tránh lỗi lệch khoảng trắng kép
        string beforeLower = Regex.Replace(contextBefore, @"\s+", " ").ToLowerInvariant();
        string afterLower = Regex.Replace(candidate.ContextAfter, @"\s+", " ").ToLowerInvariant();
        string beforeAndDate = Regex.Replace(contextBefore + " " + candidate.RawText, @"\s+", " ").ToLowerInvariant();
        string combined = Regex.Replace($"{candidate.ContextBefore} {candidate.RawText} {candidate.ContextAfter}", @"\s+", " ").ToLowerInvariant();

        // Bảng tích lũy điểm số cho từng nhóm TargetType
        var matches = new List<(DateClassificationType Type, double Score, double BaseConfidence, string RuleName, string Explanation)>();

        // 1. Kiểm tra tiền tố địa danh (TP.HCM, ngày... / Hà Nội, ngày...)
        if (RegexGeographicPrefix.IsMatch(before))
        {
            matches.Add((DateClassificationType.ContractSignDate, 150.0, 0.98, "Địa danh ký kết",
                "Phát hiện tiền tố địa danh (TP.HCM / Hà Nội / Tỉnh thành...) đứng ngay trước ngày."));
        }

        // 2. So khớp từng quy tắc trong bộ rules
        foreach (var rule in rules.Where(r => r.IsEnabled))
        {
            foreach (var keyword in rule.Keywords)
            {
                string kwLower = Regex.Replace(keyword.Trim(), @"\s+", " ").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(kwLower)) continue;

                // Kiểm tra từ khóa trong chuỗi trước + ngày
                int idxBefore = beforeAndDate.LastIndexOf(kwLower, StringComparison.Ordinal);
                if (idxBefore >= 0)
                {
                    // Measure proximity to the start of the date, excluding its formatted length.
                    int distance = Math.Max(0, beforeLower.TrimEnd().Length - (idxBefore + kwLower.Length));
                    double proximityMultiplier = distance switch
                    {
                        <= 15 => 1.25,
                        <= 35 => 1.15,
                        <= 60 => 1.00,
                        _ => 0.85
                    };

                    // Từ khóa càng dài và cụ thể (ví dụ: "hợp đồng có giá trị đến" vs "có giá trị đến") càng có độ chính xác cao
                    double specificityMultiplier = 1.0 + Math.Min(0.5, kwLower.Length * 0.02);
                    double ruleScore = rule.Priority * proximityMultiplier * specificityMultiplier;
                    matches.Add((rule.TargetType, ruleScore, rule.DefaultConfidence, rule.Name,
                        $"Khớp từ khóa '{keyword}' trong ngữ cảnh trước (cách {distance} ký tự)."));
                }
                else
                {
                    // Kiểm tra từ khóa trong ContextAfter
                    int idxAfter = afterLower.IndexOf(kwLower, StringComparison.Ordinal);
                    if (idxAfter >= 0)
                    {
                        double proximityMultiplier = idxAfter switch
                        {
                            <= 15 => 1.15,
                            <= 40 => 1.00,
                            _ => 0.80
                        };

                        double ruleScore = rule.Priority * proximityMultiplier * 0.9;
                        matches.Add((rule.TargetType, ruleScore, rule.DefaultConfidence * 0.95, rule.Name,
                            $"Khớp từ khóa '{keyword}' trong ngữ cảnh sau (cách {idxAfter} ký tự)."));
                    }
                }
            }
        }

        // 3. Heuristic vị trí tài liệu (Document Location Heuristic)
        if (segment != null)
        {
            if (segment.StoryType == StoryType.MainDocument && segment.ParagraphIndex is >= 1 and <= 5)
            {
                matches.Add((DateClassificationType.ContractSignDate, 35.0, 0.65, "Vị trí đầu hợp đồng",
                    "Ngày nằm ở các đoạn đầu tài liệu, thường là ngày ký hợp đồng."));
            }

            if (segment.StoryType == StoryType.Table)
            {
                if (combined.Contains("bên a") || combined.Contains("bên b") || combined.Contains("cmnd") || combined.Contains("cccd"))
                {
                    matches.Add((DateClassificationType.IdentityIssueDate, 25.0, 0.65, "Bảng thông tin các bên",
                        "Nằm trong bảng mô tả nhân thân hoặc thông tin bên tham gia."));
                }
            }
        }

        // Use the strongest evidence per type so overlapping keywords cannot inflate scores.
        if (matches.Count > 0)
        {
            var ranked = matches
                .GroupBy(m => m.Type)
                .Select(g => new
                {
                    Type = g.Key,
                    TotalScore = g.Max(x => x.Score),
                    MaxConfidence = g.Max(x => x.BaseConfidence),
                    BestRuleName = g.OrderByDescending(x => x.Score).First().RuleName,
                    Explanation = string.Join(" ", g.Select(x => x.Explanation).Distinct())
                })
                .OrderByDescending(g => g.TotalScore)
                .ToList();

            var grouped = ranked[0];
            bool ambiguous = ranked.Count > 1 &&
                grouped.TotalScore - ranked[1].TotalScore <= Math.Abs(grouped.TotalScore) * 0.05;
            bool isRecommended = !ambiguous && grouped.MaxConfidence >= 0.90 && DetermineRecommendation(grouped.Type);

            return new ClassificationResult
            {
                Type = grouped.Type,
                Confidence = Math.Clamp(ambiguous ? Math.Min(0.85, grouped.MaxConfidence) : grouped.MaxConfidence, 0.0, 0.99),
                MatchedRuleName = grouped.BestRuleName,
                Explanation = grouped.Explanation + (ambiguous ? $" Cần rà soát: bằng chứng gần ngang với {ranked[1].Type}." : string.Empty),
                MatchedRules = new List<string> { grouped.BestRuleName },
                IsRecommendedForReplacement = isRecommended
            };
        }

        // 5. Nếu không khớp từ khóa nào -> Unknown
        return new ClassificationResult
        {
            Type = DateClassificationType.Unknown,
            Confidence = 0.30,
            MatchedRuleName = "Không xác định",
            Explanation = "Không tìm thấy từ khóa ngữ cảnh nhận diện.",
            MatchedRules = new List<string>(),
            IsRecommendedForReplacement = false
        };
    }

    private static bool DetermineRecommendation(DateClassificationType type)
    {
        return type switch
        {
            DateClassificationType.ContractSignDate => true,
            DateClassificationType.ContractEffectiveDate => true,
            DateClassificationType.ContractStartDate => true,
            DateClassificationType.ContractEndDate => true,
            DateClassificationType.PaymentDate => true,
            DateClassificationType.HandoverDate => true,
            DateClassificationType.AppendixDate => true,
            DateClassificationType.DocumentDate => true,

            // BẢO VỆ DỮ LIỆU NHÂN THÂN: Tuyệt đối không khuyến nghị thay thế ngày sinh, ngày cấp giấy tờ cá nhân
            DateClassificationType.BirthDate => false,
            DateClassificationType.IdentityIssueDate => false,
            DateClassificationType.IdentityExpiryDate => false,
            DateClassificationType.DisputeDate => false,

            _ => false
        };
    }

    private static List<ClassificationRule> GetFallbackRules()
    {
        return new List<ClassificationRule>
        {
            new()
            {
                Name = "Ngày sinh cá nhân",
                Keywords = new List<string> { "sinh ngày", "ngày sinh", "sinh lúc", "năm sinh", "sinh:", "sinh" },
                TargetType = DateClassificationType.BirthDate,
                Priority = 100,
                DefaultConfidence = 0.98,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày cấp CCCD/CMND/Hộ chiếu",
                Keywords = new List<string> { "cấp ngày", "ngày cấp", "công an cấp", "cục cảnh sát cấp", "được cấp", "nơi cấp" },
                TargetType = DateClassificationType.IdentityIssueDate,
                Priority = 100,
                DefaultConfidence = 0.98,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày hết hạn giấy tờ tùy thân",
                Keywords = new List<string> { "có giá trị đến", "hết hạn ngày", "ngày hết hạn", "giá trị đến ngày" },
                TargetType = DateClassificationType.IdentityExpiryDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày ký hợp đồng",
                Keywords = new List<string> { "hôm nay", "hôm nay, ngày", "lập ngày", "ký ngày", "hai bên thống nhất ký", "các bên ký ngày", "hợp đồng này được lập", "lập tại" },
                TargetType = DateClassificationType.ContractSignDate,
                Priority = 100,
                DefaultConfidence = 0.96,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày hiệu lực hợp đồng",
                Keywords = new List<string> { "có hiệu lực từ", "hiệu lực kể từ", "có hiệu lực kể từ", "thời điểm có hiệu lực", "hiệu lực hợp đồng từ", "hiệu lực thi hành" },
                TargetType = DateClassificationType.ContractEffectiveDate,
                Priority = 95,
                DefaultConfidence = 0.96,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày bắt đầu thực hiện",
                Keywords = new List<string> { "bắt đầu từ", "thời gian thực hiện từ", "ngày bắt đầu", "ngày khởi công" },
                TargetType = DateClassificationType.ContractStartDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày kết thúc / hết hạn hợp đồng",
                Keywords = new List<string> { "hợp đồng có giá trị đến", "thời hạn hợp đồng", "đến hết ngày", "kết thúc vào", "ngày kết thúc", "hết hiệu lực ngày", "hết hạn hợp đồng", "có giá trị đến" },
                TargetType = DateClassificationType.ContractEndDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày thanh toán",
                Keywords = new List<string> { "thanh toán trước", "thời hạn thanh toán", "ngày thanh toán", "thanh toán đợt", "hạn thanh toán" },
                TargetType = DateClassificationType.PaymentDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày bàn giao / nghiệm thu",
                Keywords = new List<string> { "bàn giao trước", "ngày bàn giao", "thời hạn bàn giao", "nghiệm thu vào", "bàn giao công trình", "bàn giao thiết bị", "bàn giao" },
                TargetType = DateClassificationType.HandoverDate,
                Priority = 90,
                DefaultConfidence = 0.95,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày phụ lục hợp đồng",
                Keywords = new List<string> { "phụ lục số", "phụ lục ngày", "ký phụ lục", "kèm theo phụ lục", "phụ lục đính kèm", "phụ lục hợp đồng" },
                TargetType = DateClassificationType.AppendixDate,
                Priority = 85,
                DefaultConfidence = 0.92,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày phát sinh tranh chấp / tố tụng",
                Keywords = new List<string> { "tranh chấp ngày", "khiếu nại ngày", "khởi kiện ngày", "hòa giải ngày" },
                TargetType = DateClassificationType.DisputeDate,
                Priority = 80,
                DefaultConfidence = 0.90,
                IsEnabled = true
            },
            new()
            {
                Name = "Ngày ban hành văn bản",
                Keywords = new List<string> { "ban hành ngày", "ngày ký quyết định", "ngày ban hành" },
                TargetType = DateClassificationType.DocumentDate,
                Priority = 80,
                DefaultConfidence = 0.90,
                IsEnabled = true
            }
        };
    }
}








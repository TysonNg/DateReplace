using ContractDateReplacer.Core.Enums;
using ContractDateReplacer.Core.Interfaces;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using Moq;
using Xunit;

namespace ContractDateReplacer.Core.Tests;

public class RuleRefreshRegressionTests
{
    [Fact]
    public async Task ClassifyBatchAsync_AllRulesDisabled_DoesNotRecommendFromLocation()
    {
        var repository = new Mock<IRuleRepository>();
        repository.Setup(r => r.GetAllRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClassificationRule> { new() { IsEnabled = false } });
        var candidate = new DateCandidate { ContextBefore = "Hà Nội, ngày", RawText = "01/01/2026" };
        await new DateClassifier(repository.Object).ClassifyBatchAsync(new[] { candidate }, segment: new DocumentTextSegment { ParagraphIndex = 1 });
        Assert.Equal(DateClassificationType.Unknown, candidate.Classification.Type);
        Assert.False(candidate.Classification.IsRecommendedForReplacement);
    }
    [Fact]
    public async Task ClassifyBatchAsync_UsesDocumentLocation()
    {
        var candidate = new DateCandidate { RawText = "01/01/2026" };
        await new DateClassifier().ClassifyBatchAsync(new[] { candidate }, segment: new DocumentTextSegment
        {
            StoryType = StoryType.MainDocument, ParagraphIndex = 1
        });
        Assert.Equal(DateClassificationType.ContractSignDate, candidate.Classification.Type);
        Assert.True(candidate.Classification.IsReviewRequired);
        Assert.False(candidate.Classification.IsRecommendedForReplacement);
    }
    [Fact]
    public async Task ClassifyAsync_OverlappingKeywords_DoNotOutvoteStrongerRule()
    {
        var repository = new Mock<IRuleRepository>();
        repository.Setup(r => r.GetAllRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClassificationRule>
        {
            new() { Name = "weak", Keywords = new() { "mốc", "mốc ngày" }, TargetType = DateClassificationType.PaymentDate, Priority = 60, DefaultConfidence = 0.95, IsEnabled = true },
            new() { Name = "strong", Keywords = new() { "mốc ngày" }, TargetType = DateClassificationType.HandoverDate, Priority = 100, DefaultConfidence = 0.95, IsEnabled = true }
        });
        var result = await new DateClassifier(repository.Object).ClassifyAsync(new DateCandidate { ContextBefore = "mốc ngày", RawText = "01/01/2026" });
        Assert.Equal(DateClassificationType.HandoverDate, result.Type);
    }

    [Fact]
    public async Task ClassifyAsync_EqualEvidence_RequiresReview()
    {
        var repository = new Mock<IRuleRepository>();
        repository.Setup(r => r.GetAllRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClassificationRule>
        {
            new() { Name = "payment", Keywords = new() { "mốc" }, TargetType = DateClassificationType.PaymentDate, Priority = 100, DefaultConfidence = 0.95, IsEnabled = true },
            new() { Name = "handover", Keywords = new() { "mốc" }, TargetType = DateClassificationType.HandoverDate, Priority = 100, DefaultConfidence = 0.95, IsEnabled = true }
        });
        var result = await new DateClassifier(repository.Object).ClassifyAsync(new DateCandidate { ContextBefore = "mốc", RawText = "01/01/2026" });
        Assert.True(result.IsReviewRequired);
        Assert.False(result.IsRecommendedForReplacement);
    }
    [Fact]
    public async Task ClassifyAsync_UsesUpdatedRepositoryRules()
    {
        var rules = new List<ClassificationRule> { new() { Name = "custom", Keywords = new() { "mốc" }, TargetType = DateClassificationType.PaymentDate, Priority = 100, IsEnabled = true } };
        var repository = new Mock<IRuleRepository>();
        repository.Setup(r => r.GetAllRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => rules);
        var classifier = new DateClassifier(repository.Object);
        var candidate = new DateCandidate { ContextBefore = "mốc", RawText = "01/01/2026" };
        Assert.Equal(DateClassificationType.PaymentDate, (await classifier.ClassifyAsync(candidate)).Type);
        rules = new List<ClassificationRule> { new() { Name = "updated", Keywords = new() { "mốc" }, TargetType = DateClassificationType.HandoverDate, Priority = 100, IsEnabled = true } };
        Assert.Equal(DateClassificationType.HandoverDate, (await classifier.ClassifyAsync(candidate)).Type);
    }

    [Fact]
    public async Task ClassifyAsync_AllRulesDisabled_DoesNotRestoreFallbackRules()
    {
        var repository = new Mock<IRuleRepository>();
        repository.Setup(r => r.GetAllRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClassificationRule> { new() { IsEnabled = false } });
        var classifier = new DateClassifier(repository.Object);
        var result = await classifier.ClassifyAsync(new DateCandidate { ContextBefore = "ký ngày", RawText = "01/01/2026" });
        Assert.Equal(DateClassificationType.Unknown, result.Type);
    }
}




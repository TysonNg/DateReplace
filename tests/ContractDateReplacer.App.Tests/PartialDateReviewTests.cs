using ContractDateReplacer.App.ViewModels;
using ContractDateReplacer.Core.Models;
using ContractDateReplacer.Core.Services;
using Xunit;
namespace ContractDateReplacer.App.Tests;
public class PartialDateReviewTests
{
    [Fact]
    public void Partial_date_review_displays_missing_year_and_yearless_replacement()
    {
        var candidate=Assert.Single(new DateDetector().DetectDates("1/02"));
        var item=new DateItemViewModel(candidate,new ContractFileItem());
        Assert.Equal("Thiếu năm", typeof(DateItemViewModel).GetProperty("DateCompletenessText")?.GetValue(item));
        item.TargetNewDate=new System.DateTime(2028,4,3);
        item.IsSelected=true;
        Assert.Equal("3/04",item.FormattedReplacementText);
        Assert.True(item.HasEffectiveChange);
    }
}

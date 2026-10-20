using System.ComponentModel.DataAnnotations;
using Lantern.Core.Administration;

namespace Lantern.Core.Tests;

public sealed class AdministrationModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A\nB")]
    public void InvalidSettingsAreRejected(string name) => Assert.Throws<ValidationException>(() => new ApplicationSetting { DisplayName = name }.Validate());

    [Fact]
    public void DefaultsAndNormalizedSettingsAreValid()
    {
        Assert.Equal("Lantern", new ApplicationSetting().DisplayName);
        var setting = new ApplicationSetting { DisplayName = "  社内検索  " };
        setting.Validate();
        Assert.Equal("社内検索", setting.DisplayName);
        Assert.Throws<ValidationException>(() => new ApplicationSetting { DisplayName = new string('x', 81) }.Validate());
    }

    [Fact]
    public void CrawlerValidationPreservesStableIdentityAndUtc()
    {
        var crawler = new CrawlerDefinition { Name = "  Documents  ", CrawlerType = "web", CreatedAt = DateTimeOffset.Now };
        Guid id = crawler.Id;
        crawler.Validate();
        Assert.Equal(id, crawler.Id);
        Assert.Equal("Documents", crawler.Name);
        Assert.Equal(TimeSpan.Zero, crawler.CreatedAt.Offset);
        crawler.CrawlerType = "unknown";
        Assert.Throws<ValidationException>(crawler.Validate);
        crawler.CrawlerType = "file";
        crawler.Name = " ";
        Assert.Throws<ValidationException>(crawler.Validate);
    }
}

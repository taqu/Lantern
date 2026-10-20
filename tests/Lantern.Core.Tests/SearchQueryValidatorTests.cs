using Lantern.Core.Search;

namespace Lantern.Core.Tests;

public sealed class SearchQueryValidatorTests
{
    // IsValid

    [Fact]
    public void IsValid_NullQuery_ReturnsFalse()
        => Assert.False(SearchQueryValidator.IsValid(null));

    [Fact]
    public void IsValid_EmptyQuery_ReturnsFalse()
        => Assert.False(SearchQueryValidator.IsValid(""));

    [Fact]
    public void IsValid_WhitespaceOnly_ReturnsFalse()
        => Assert.False(SearchQueryValidator.IsValid("   "));

    [Fact]
    public void IsValid_ValidQuery_ReturnsTrue()
        => Assert.True(SearchQueryValidator.IsValid("lantern search"));

    [Fact]
    public void IsValid_ExactMaxLength_ReturnsTrue()
    {
        string query = new string('a', SearchQueryValidator.MaxQueryLength);
        Assert.True(SearchQueryValidator.IsValid(query));
    }

    [Fact]
    public void IsValid_ExceedsMaxLength_ReturnsFalse()
    {
        string query = new string('a', SearchQueryValidator.MaxQueryLength + 1);
        Assert.False(SearchQueryValidator.IsValid(query));
    }

    // NormalizePage

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(-100, 1)]
    public void NormalizePage_BelowOne_ReturnsOne(int input, int expected)
        => Assert.Equal(expected, SearchQueryValidator.NormalizePage(input));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(100, 100)]
    public void NormalizePage_ValidPage_ReturnsUnchanged(int input, int expected)
        => Assert.Equal(expected, SearchQueryValidator.NormalizePage(input));

    // CalculateOffset

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 10, 20)]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    public void CalculateOffset_ReturnsCorrectOffset(int page, int pageSize, int expectedOffset)
        => Assert.Equal(expectedOffset, SearchQueryValidator.CalculateOffset(page, pageSize));

    [Fact]
    public void CalculateOffset_PageZero_TreatedAsPageOne()
        => Assert.Equal(0, SearchQueryValidator.CalculateOffset(0, 10));
}

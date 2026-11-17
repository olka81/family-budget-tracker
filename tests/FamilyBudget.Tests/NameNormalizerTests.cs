using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// Plain unit tests: no database needed.
public class NameNormalizerTests
{
    [Theory]
    [InlineData("  Молоко  ", "молоко")]
    [InlineData("МОЛОКО 3,2%", "молоко 3,2%")]
    public void Normalize_TrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("whole    milk", "whole milk")]
    [InlineData("whole\tmilk", "whole milk")]
    [InlineData("whole\u00A0milk", "whole milk")]
    [InlineData(" Whole \t\u00A0 Milk \t 3,2% ", "whole milk 3,2%")]
    public void Normalize_CollapsesInnerWhitespaceRuns(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\u00A0 ")]
    public void Normalize_EmptyOrWhitespace_ReturnsEmpty(string? input)
    {
        Assert.Equal("", NameNormalizer.Normalize(input));
    }
}

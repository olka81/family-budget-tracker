using System.Globalization;
using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// Plain unit tests: no database needed.
public class DecimalInputTests
{
    // Every case is run under both cultures: "," and "." have different meanings in them.
    public static TheoryData<string, string, decimal> ValidInputs()
    {
        var data = new TheoryData<string, string, decimal>();
        foreach (var culture in new[] { "ru-RU", "en-US" })
        {
            data.Add(culture, "12", 12m);
            data.Add(culture, "12.5", 12.5m);
            data.Add(culture, "12,5", 12.5m);
            data.Add(culture, "-3.20", -3.20m);
            data.Add(culture, "-0,5", -0.5m);
            data.Add(culture, "0", 0m);
            data.Add(culture, "  7.25  ", 7.25m);
            data.Add(culture, ".5", 0.5m);
            data.Add(culture, "5.", 5m);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidInputs))]
    public void TryParse_ValidInput_ReturnsValue(string culture, string input, decimal expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Assert.True(DecimalInput.TryParse(input, out var value));
            Assert.Equal(expected, value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 000")]
    [InlineData("1 000")]
    [InlineData("1,000.5")]
    [InlineData("1.2.3")]
    [InlineData("1,2,3")]
    [InlineData("1e3")]
    [InlineData("+5")]
    [InlineData("abc")]
    [InlineData("5-")]
    [InlineData(",")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("-,")]
    [InlineData("- 5")]
    [InlineData("--5")]
    // Arabic-Indic digits: digits for char.IsDigit, but not accepted.
    [InlineData("١٢")]
    public void TryParse_InvalidInput_ReturnsFalse(string? input)
    {
        Assert.False(DecimalInput.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_ManyDecimals_DoesNotRound()
    {
        Assert.True(DecimalInput.TryParse("1.23456", out var value));
        Assert.Equal(1.23456m, value);
    }

    [Fact]
    public void TryParse_AsManyDigitsAsDecimalCanHold_ReturnsExactValue()
    {
        // 28 digits; the leading zeros are not counted.
        Assert.True(DecimalInput.TryParse("001.234567890123456789012345678", out var value));
        Assert.Equal(1.234567890123456789012345678m, value);
    }

    [Theory]
    // Without the limit the last two would silently become 1 and 0.
    [InlineData("12345678901234567890123456789")]
    [InlineData("1.0000000000000000000000000001")]
    [InlineData("1.00000000000000000000000000000000001")]
    [InlineData("0.00000000000000000000000000000000001")]
    public void TryParse_MoreDigitsThanDecimalCanHold_ReturnsFalse(string input)
    {
        Assert.False(DecimalInput.TryParse(input, out _));
    }
}

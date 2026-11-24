using System.Globalization;
using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// Plain unit tests: no database needed.
public class DecimalFormatTests
{
    private static readonly string[] Cultures = ["ru-RU", "en-US"];

    public static TheoryData<string, decimal, string> MoneyCases()
    {
        var data = new TheoryData<string, decimal, string>();
        foreach (var culture in Cultures)
        {
            data.Add(culture, 1.005m, "1.01");
            data.Add(culture, -2.5m, "-2.50");
            data.Add(culture, 0.66267m, "0.66");
            data.Add(culture, 10m, "10.00");
            data.Add(culture, 1234567.891m, "1234567.89");
        }

        return data;
    }

    public static TheoryData<string, decimal, string> QuantityCases()
    {
        var data = new TheoryData<string, decimal, string>();
        foreach (var culture in Cultures)
        {
            data.Add(culture, 1m, "1");
            data.Add(culture, 0.500m, "0.5");
            data.Add(culture, 1.250m, "1.25");
            data.Add(culture, 0.333m, "0.333");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MoneyCases))]
    public void Money_RoundsAwayFromZeroToTwoDecimals(string culture, decimal value, string expected)
    {
        Assert.Equal(expected, UnderCulture(culture, () => DecimalFormat.Money(value)));
    }

    [Fact]
    public void Money_NegativeThatRoundsToZero_HasNoMinus()
    {
        Assert.Equal("0.00", DecimalFormat.Money(-0.001m));
    }

    [Theory]
    [MemberData(nameof(QuantityCases))]
    public void Quantity_UpToThreeDecimalsWithoutTrailingZeros(string culture, decimal value, string expected)
    {
        Assert.Equal(expected, UnderCulture(culture, () => DecimalFormat.Quantity(value)));
    }

    private static string UnderCulture(string culture, Func<string> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

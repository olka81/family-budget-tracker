using System.Globalization;

namespace FamilyBudget.Services;

// Formats numbers for display only, independently of the current culture (dot as separator).
// The rounded values must never be sent to a service or stored.
public static class DecimalFormat
{
    // Exactly 2 decimal places.
    public static string Money(decimal value) => Format(value, 2, "0.00");

    // Up to 3 decimal places, no trailing zeros.
    public static string Quantity(decimal value) => Format(value, 3, "0.###");

    private static string Format(decimal value, int decimals, string format)
    {
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);

        // A negative value that rounds to zero must not be shown as "-0.00".
        if (rounded == 0m)
        {
            rounded = 0m;
        }

        return rounded.ToString(format, CultureInfo.InvariantCulture);
    }
}

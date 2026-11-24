using System.Globalization;

namespace FamilyBudget.Services;

// Parses numbers typed by the user. Does not depend on the current culture: both ',' and '.'
// are accepted as the decimal separator. Never rounds and does not check the precision of
// the database columns (PurchaseService does that).
public static class DecimalInput
{
    // A decimal holds any number of up to 28 digits exactly; decimal.TryParse silently rounds
    // longer input, so such input is rejected.
    private const int MaxDigits = 28;

    // Accepted: an optional leading '-', digits and at most one separator, with at least one
    // digit (".5" and "5." are valid). Surrounding whitespace is ignored.
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;

        var trimmed = (text ?? "").Trim();
        var unsigned = trimmed.StartsWith('-') ? trimmed[1..] : trimmed;

        var separatorSeen = false;
        var digitSeen = false;
        var countedDigits = 0;
        foreach (var ch in unsigned)
        {
            if (char.IsAsciiDigit(ch))
            {
                digitSeen = true;

                // Leading zeros of the integer part do not take up space in a decimal.
                if (ch != '0' || separatorSeen || countedDigits > 0)
                {
                    countedDigits++;
                }
            }
            else if (ch is ',' or '.' && !separatorSeen)
            {
                separatorSeen = true;
            }
            else
            {
                return false;
            }
        }

        if (!digitSeen || countedDigits > MaxDigits)
        {
            return false;
        }

        return decimal.TryParse(
            trimmed.Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }
}

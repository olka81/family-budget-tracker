using System.Text;

namespace FamilyBudget.Services;

// The single normalization for product and store names, used for every write and lookup.
// Category names are NOT normalized.
public static class NameNormalizer
{
    // Trims, collapses every run of whitespace (anything char.IsWhiteSpace accepts, including
    // tabs and non-breaking spaces) into one space, and converts to lower case.
    // Returns "" for null or whitespace-only input; callers treat "" as an empty name.
    public static string Normalize(string? name)
    {
        if (name is null)
        {
            return "";
        }

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;

        foreach (var ch in name)
        {
            if (char.IsWhiteSpace(ch))
            {
                // Only emit a space between words, never at the start.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString().ToLowerInvariant();
    }
}

namespace LeadManagementSystem.Helpers;

/// <summary>
/// Smart currency formatting helper
/// - Values < 10 Crore: Full exact amount with Indian formatting
/// - Values >= 10 Crore: Crore format with up to 2 decimal places
/// </summary>
public static class CurrencyFormatHelper
{
    private const decimal ONE_CRORE = 10_000_000m;      // ₹1,00,00,000
    private const decimal TEN_CRORE = 100_000_000m;     // ₹10,00,00,000

    /// <summary>
    /// Format decimal value as Indian rupees with smart scaling
    /// </summary>
    public static string Format(decimal? value)
    {
        if (!value.HasValue || value.Value == 0)
            return "₹0";

        decimal amount = value.Value;

        if (amount >= TEN_CRORE)
        {
            // Convert to Crore format with 2 decimal places
            decimal crores = amount / ONE_CRORE;
            return $"₹{crores:F2}Cr".TrimEnd('0').TrimEnd('.');
        }
        else
        {
            // Show full amount with Indian number formatting
            return $"₹{FormatIndianNumber((long)amount)}";
        }
    }

    /// <summary>
    /// Format number with Indian comma placement
    /// Examples: 1,297 | 12,97,000 | 1,25,45,000
    /// </summary>
    private static string FormatIndianNumber(long number)
    {
        if (number == 0)
            return "0";

        string numStr = Math.Abs(number).ToString();
        string result = string.Empty;

        // Add ones, tens, hundreds (last 3 digits)
        if (numStr.Length <= 3)
            return number.ToString("N0").Split('.')[0];

        // Process from right: XXX,XX,XX,XX format
        int lastThreeDigits = numStr.Length - 3;
        result = numStr.Substring(lastThreeDigits);

        // Add remaining digits in groups of 2 from right
        for (int i = lastThreeDigits - 1; i >= 0; i -= 2)
        {
            if (i >= 1)
                result = numStr.Substring(i - 1, 2) + "," + result;
            else
                result = numStr.Substring(0, 1) + "," + result;
        }

        return number < 0 ? "-" + result : result;
    }

    /// <summary>
    /// Get full unformatted value for tooltips
    /// </summary>
    public static string GetFullValue(decimal? value)
    {
        if (!value.HasValue)
            return "₹0";

        long amount = (long)value.Value;
        return $"₹{FormatIndianNumber(amount)}";
    }
}

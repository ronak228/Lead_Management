using System.Text.RegularExpressions;

namespace LeadManagementSystem.Helpers;

/// <summary>
/// Shared helper for phone number normalization.
/// Standardizes phone formats across the application.
/// </summary>
public static class PhoneHelper
{
    /// <summary>
    /// Normalizes phone numbers for Indian format.
    /// Removes +91 prefix, handles leading zeros, extracts 10 digits.
    /// </summary>
    public static string Normalize(string phone)
    {
        var digits = Regex.Replace(phone, @"\D", "");
        if (digits.Length > 10 && digits.StartsWith("91")) digits = digits[2..];
        if (digits.Length > 10 && digits.StartsWith("0"))  digits = digits[1..];
        return digits.Length >= 10 ? digits : phone.Trim();
    }
}

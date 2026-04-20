namespace LeadManagementSystem.Helpers;

/// <summary>
/// Password reset token management and validation
/// </summary>
public static class ResetTokenHelper
{
    /// <summary>
    /// Generate a secure reset token valid for 24 hours
    /// </summary>
    public static (string token, DateTime expiry) GenerateResetToken()
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var expiry = DateTime.UtcNow.AddHours(24);
        return (token, expiry);
    }

    /// <summary>
    /// Hash the reset token for storage in database (similar to passwords)
    /// </summary>
    public static string HashToken(string token)
    {
        return PasswordHelper.Hash(token);
    }

    /// <summary>
    /// Verify reset token matches hash
    /// </summary>
    public static bool VerifyToken(string token, string hash)
    {
        return PasswordHelper.Verify(token, hash);
    }

    /// <summary>
    /// Check if token is still valid (not expired)
    /// </summary>
    public static bool IsTokenValid(DateTime expiry)
    {
        return DateTime.UtcNow <= expiry;
    }

    /// <summary>
    /// Generate reset URL (use this to construct email links)
    /// </summary>
    public static string GenerateResetUrl(string baseUrl, string token, int userId)
    {
        return $"{baseUrl}/Auth/ResetPassword?token={Uri.EscapeDataString(token)}&uid={userId}";
    }
}

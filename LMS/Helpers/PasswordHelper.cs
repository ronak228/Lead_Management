using System.Security.Cryptography;
using System.Text;

namespace LeadManagementSystem.Helpers;

public static class PasswordHelper
{
    private const int WorkFactor = 12;

    /// <summary>Produces a new BCrypt hash (used for new passwords).</summary>
    public static string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    /// <summary>
    /// Verifies a password against a stored hash.
    /// Supports both BCrypt (new) and legacy SHA-256 (old) hashes transparently.
    /// </summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;

        // BCrypt hashes always start with $2a$ or $2b$
        if (storedHash.StartsWith("$2a$") || storedHash.StartsWith("$2b$"))
            return BCrypt.Net.BCrypt.Verify(password, storedHash);

        // Legacy: SHA-256 hex (64 chars)
        return LegacySha256(password) == storedHash;
    }

    /// <summary>Returns true if the stored hash is a legacy SHA-256 hash (needs upgrade).</summary>
    public static bool IsLegacyHash(string storedHash)
        => !storedHash.StartsWith("$2a$") && !storedHash.StartsWith("$2b$");

    private static string LegacySha256(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLower();
    }
}


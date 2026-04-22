using LeadManagementSystem.Data;

namespace LeadManagementSystem.Helpers;

/// <summary>
/// Shared helper for city-related operations.
/// Prevents duplicate city resolution logic across controllers.
/// </summary>
public static class CityHelper
{
    /// <summary>
    /// Resolves a city ID from text input.
    /// If city doesn't exist, creates it atomically.
    /// Prevents race conditions on concurrent inserts.
    /// </summary>
    public static async Task<int?> ResolveCityIdAsync(DbHelper db, string? cityText)
    {
        if (string.IsNullOrWhiteSpace(cityText)) return null;
        var name = cityText.Trim();
        // Atomic upsert prevents race condition on concurrent inserts for the same city name
        var id = await db.ExecuteScalarAsync(
            "INSERT INTO cfg_city (name, is_active) VALUES (@n, TRUE) ON CONFLICT (name) DO NOTHING RETURNING id",
            new() { ["@n"] = name });
        if (id != null && id != DBNull.Value)
            return Convert.ToInt32(id);
        var rows = await db.QueryAsync(
            "SELECT id FROM cfg_city WHERE LOWER(name)=LOWER(@n) LIMIT 1",
            new() { ["@n"] = name });
        return rows.Count > 0 ? Convert.ToInt32(rows[0]["id"]) : null;
    }
}

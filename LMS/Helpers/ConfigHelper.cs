using LeadManagementSystem.Data;
using LeadManagementSystem.Models;

namespace LeadManagementSystem.Helpers;

/// <summary>
/// Shared helper for config-related operations.
/// Prevents duplicate dropdown loading logic across controllers.
/// </summary>
public static class ConfigHelper
{
    /// <summary>
    /// Loads active config items from a specified table.
    /// Used for dropdowns (cities, modules, statuses, etc).
    /// </summary>
    public static async Task<List<ConfigItem>> LoadActiveAsync(DbHelper db, string table)
    {
        var rows = await db.QueryAsync($"SELECT id, name FROM {table} WHERE is_active=TRUE ORDER BY name", new());
        return rows.Select(r => new ConfigItem
        {
            Id = Convert.ToInt32(r["id"]),
            Name = r["name"]?.ToString() ?? ""
        }).ToList();
    }
}

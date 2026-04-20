using LeadManagementSystem.Data;
using LeadManagementSystem.Filters;
using LeadManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace LeadManagementSystem.Controllers;

[SessionAuth("Admin")]
public class ConfigurationController : Controller
{
    private readonly DbHelper _db;

    public ConfigurationController(DbHelper db) => _db = db;

    // ── Helper: load items from a config table ──────────────
    private async Task<List<ConfigItem>> LoadTable(string table)
    {
        var rows = await _db.QueryAsync(
            $"SELECT id, name, is_active, created_at FROM {table} ORDER BY name",
            new());
        return rows.Select(r => new ConfigItem
        {
            Id        = Convert.ToInt32(r["id"]),
            Name      = r["name"]?.ToString() ?? "",
            IsActive  = Convert.ToBoolean(r["is_active"]),
            CreatedAt = Convert.ToDateTime(r["created_at"])
        }).ToList();
    }

    // ── INDEX ────────────────────────────────────────────────
    public async Task<IActionResult> Index(string tab = "status")
    {
        var vm = new ConfigurationViewModel
        {
            ActiveTab  = tab,
            Statuses   = await LoadTable("cfg_status"),
            Modules    = await LoadTable("cfg_module"),
            Products   = await LoadTable("cfg_product"),
            Categories = await LoadTable("cfg_category"),
            Cities     = await LoadTable("cfg_city")
        };
        return View(vm);
    }

    // ── ADD ──────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Add(string tab, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Name cannot be empty.";
            return RedirectToAction("Index", new { tab });
        }

        var table = TabToTable(tab);
        if (table == null) return BadRequest();

        var exists = Convert.ToInt32(await _db.ExecuteScalarAsync(
            $"SELECT COUNT(*) FROM {table} WHERE LOWER(name)=LOWER(@n)",
            new() { ["@n"] = name.Trim() }));

        if (exists > 0)
        {
            TempData["Error"] = $"'{name.Trim()}' already exists.";
            return RedirectToAction("Index", new { tab });
        }

        await _db.ExecuteNonQueryAsync(
            $"INSERT INTO {table} (name) VALUES (@n)",
            new() { ["@n"] = name.Trim() });

        TempData["Success"] = $"'{name.Trim()}' added successfully.";
        return RedirectToAction("Index", new { tab });
    }

    // ── EDIT (GET) ───────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Edit(string tab, int id)
    {
        var table = TabToTable(tab);
        if (table == null) return BadRequest();

        var rows = await _db.QueryAsync(
            $"SELECT id, name, is_active FROM {table} WHERE id=@id",
            new() { ["@id"] = id });

        if (rows.Count == 0) return NotFound();

        var item = new ConfigItem
        {
            Id       = id,
            Name     = rows[0]["name"]?.ToString() ?? "",
            IsActive = Convert.ToBoolean(rows[0]["is_active"])
        };

        ViewBag.Tab = tab;
        return View(item);
    }

    // ── EDIT (POST) ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Edit(string tab, int id, string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Name cannot be empty.";
            return RedirectToAction("Index", new { tab });
        }

        var table = TabToTable(tab);
        if (table == null) return BadRequest();

        // Check duplicate (exclude self)
        var exists = Convert.ToInt32(await _db.ExecuteScalarAsync(
            $"SELECT COUNT(*) FROM {table} WHERE LOWER(name)=LOWER(@n) AND id<>@id",
            new() { ["@n"] = name.Trim(), ["@id"] = id }));

        if (exists > 0)
        {
            TempData["Error"] = $"'{name.Trim()}' already exists.";
            return RedirectToAction("Index", new { tab });
        }

        await _db.ExecuteNonQueryAsync(
            $"UPDATE {table} SET name=@n, is_active=@a WHERE id=@id",
            new() { ["@n"] = name.Trim(), ["@a"] = isActive, ["@id"] = id });

        TempData["Success"] = "Updated successfully.";
        return RedirectToAction("Index", new { tab });
    }

    // ── TOGGLE ACTIVE ────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Toggle(string tab, int id)
    {
        var table = TabToTable(tab);
        if (table == null) return BadRequest();

        await _db.ExecuteNonQueryAsync(
            $"UPDATE {table} SET is_active = NOT is_active WHERE id=@id",
            new() { ["@id"] = id });

        return RedirectToAction("Index", new { tab });
    }

    // ── DELETE (soft deactivate) ────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Delete(string tab, int id)
    {
        var table = TabToTable(tab);
        if (table == null) return BadRequest();

        // Soft-deactivate instead of hard-delete to preserve FK integrity.
        // Deactivated items are hidden from creation dropdowns but existing linked records remain intact.
        await _db.ExecuteNonQueryAsync(
            $"UPDATE {table} SET is_active=FALSE WHERE id=@id",
            new() { ["@id"] = id });

        TempData["Success"] = "Item deactivated successfully.";
        return RedirectToAction("Index", new { tab });
    }

    // ── Helper: map tab name → table name (whitelist Dictionary — never interpolate user input) ──
    private static readonly Dictionary<string, string> TableMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["status"]   = "cfg_status",
        ["module"]   = "cfg_module",
        ["product"]  = "cfg_product",
        ["category"] = "cfg_category",
        ["city"]     = "cfg_city"
    };

    private static string? TabToTable(string? tab) =>
        tab != null && TableMap.TryGetValue(tab, out var t) ? t : null;
}

using LeadManagementSystem.Data;
using LeadManagementSystem.Filters;
using LeadManagementSystem.Helpers;
using LeadManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace LeadManagementSystem.Controllers;

[SessionAuth(SessionHelper.RoleAdmin)]
public class UserManagementController : Controller
{
    private readonly DbHelper _db;
    private readonly ILogger<UserManagementController> _logger;

    // Only these roles may be assigned via the admin panel
    private static readonly string[] AllowedRoles =
    {
        SessionHelper.RoleAdmin,
        SessionHelper.RoleEmployee,
        SessionHelper.RoleClient
    };

    public UserManagementController(DbHelper db, ILogger<UserManagementController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── LIST ─────────────────────────────────────────────────
    public async Task<IActionResult> Index(string? search, string? filterRole, int page = 1)
    {
        const int pageSize = 25;
        var p = new Dictionary<string, object?>();

        var whereClause = " WHERE (is_deleted IS NULL OR is_deleted = FALSE)";
        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND (LOWER(full_name) LIKE @s OR LOWER(email) LIKE @s)";
            p["@s"] = $"%{search.Trim().ToLower()}%";
        }
        if (!string.IsNullOrWhiteSpace(filterRole) && AllowedRoles.Contains(filterRole))
        {
            whereClause += " AND role=@r";
            p["@r"] = filterRole;
        }

        // Count total
        var countSql = @"SELECT COUNT(*) FROM users" + whereClause;
        var totalRecords = Convert.ToInt32(await _db.ExecuteScalarAsync(countSql, p));
        var (skip, take, totalPages) = PaginationHelper.GetPaginationParams(page, totalRecords, pageSize);

        var sql = @"SELECT id, full_name, email, role, is_active, created_at
                    FROM users" + whereClause +
            $" ORDER BY created_at DESC LIMIT {take} OFFSET {skip}";

        var rows = await _db.QueryAsync(sql, p);
        var users = rows.Select(r => new UserListItem
        {
            Id        = Convert.ToInt32(r["id"]),
            FullName  = r["full_name"]?.ToString() ?? "",
            Email     = r["email"]?.ToString() ?? "",
            Role      = r["role"]?.ToString() ?? SessionHelper.RoleClient,
            IsActive  = Convert.ToBoolean(r["is_active"]),
            CreatedAt = Convert.ToDateTime(r["created_at"])
        }).ToList();

        ViewBag.Search = search;
        ViewBag.FilterRole = filterRole;
        ViewBag.Pagination = new PaginationInfo
        {
            CurrentPage = page,
            PageSize = pageSize,
            TotalRecords = totalRecords,
            TotalPages = totalPages
        };
        return View(users);
    }

    // ── TOGGLE ACTIVE ────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Toggle(int id)
    {
        var sessionId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        if (id == sessionId)
        {
            TempData["Error"] = "You cannot deactivate your own account.";
            return RedirectToAction("Index");
        }

        await _db.ExecuteNonQueryAsync(
            "UPDATE users SET is_active = NOT is_active, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = id });

        TempData["Success"] = "Account status updated.";
        return RedirectToAction("Index");
    }

    // ── CHANGE ROLE ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ChangeRole(int id, string role)
    {
        if (!AllowedRoles.Contains(role))
            return BadRequest("Invalid role.");

        var sessionId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        if (id == sessionId)
        {
            TempData["Error"] = "You cannot change your own role.";
            return RedirectToAction("Index");
        }

        // Prevent demoting the last active Admin
        if (role != SessionHelper.RoleAdmin)
        {
            var existingRole = (await _db.QueryAsync(
                "SELECT role FROM users WHERE id=@id",
                new() { ["@id"] = id })).FirstOrDefault()?["role"]?.ToString();

            if (existingRole == SessionHelper.RoleAdmin)
            {
                var adminCount = Convert.ToInt32(await _db.ExecuteScalarAsync(
                    "SELECT COUNT(*) FROM users WHERE role='Admin' AND is_active=TRUE",
                    new()));
                if (adminCount <= 1)
                {
                    TempData["Error"] = "Cannot change role: this is the only active Admin.";
                    return RedirectToAction("Index");
                }
            }
        }

        await _db.ExecuteNonQueryAsync(
            "UPDATE users SET role=@r, updated_at=NOW() WHERE id=@id",
            new() { ["@r"] = role, ["@id"] = id });

        TempData["Success"] = "Role updated.";
        return RedirectToAction("Index");
    }

    // ── SOFT DELETE ───────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var sessionId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        if (id == sessionId)
        {
            TempData["Error"] = "You cannot delete your own account.";
            return RedirectToAction("Index");
        }

        // Prevent deleting the last active Admin
        var targetRole = (await _db.QueryAsync(
            "SELECT role FROM users WHERE id=@id",
            new() { ["@id"] = id })).FirstOrDefault()?["role"]?.ToString();

        if (targetRole == SessionHelper.RoleAdmin)
        {
            var adminCount = Convert.ToInt32(await _db.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM users WHERE role='Admin' AND is_active=TRUE",
                new()));
            if (adminCount <= 1)
            {
                TempData["Error"] = "Cannot delete the only active Admin account.";
                return RedirectToAction("Index");
            }
        }

        // Soft delete — try is_deleted column first; fall back to hard deactivate for legacy schema
        try
        {
            await _db.ExecuteNonQueryAsync(
                "UPDATE users SET is_deleted=TRUE, is_active=FALSE, updated_at=NOW() WHERE id=@id",
                new() { ["@id"] = id });
        }
        catch
        {
            // Column doesn't exist yet — just deactivate
            await _db.ExecuteNonQueryAsync(
                "UPDATE users SET is_active=FALSE, updated_at=NOW() WHERE id=@id",
                new() { ["@id"] = id });
        }

        TempData["Success"] = "User removed.";
        return RedirectToAction("Index");
    }
}

using LeadManagementSystem.Data;
using LeadManagementSystem.Filters;
using LeadManagementSystem.Helpers;
using LeadManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;

namespace LeadManagementSystem.Controllers;

// Admin and Employee manage clients; Client role can only view their own profile
[SessionAuth]
public class ClientController : Controller
{
    private readonly DbHelper _db;
    public ClientController(DbHelper db) => _db = db;

    // ── LIST ─────────────────────────────────────────────────
    public async Task<IActionResult> Index(string? search, string? filterStatus, int page = 1)
    {
        var isClient = HttpContext.Session.GetString(SessionHelper.UserRole) == "Client";
        var userId   = HttpContext.Session.GetInt32(SessionHelper.UserId);

        if (isClient && userId.HasValue)
        {
            // Client can only view their own profile
            return RedirectToAction("Details", new { id = userId.Value });
        }

        const int pageSize = 25;
        var countSql = @"
            SELECT COUNT(*) FROM users c
            WHERE c.is_deleted=FALSE AND c.role='Client'";
        var p = new Dictionary<string, object?>();
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            countSql += " AND (LOWER(c.company_name) LIKE @s OR LOWER(c.contact_person) LIKE @s OR c.phone LIKE @s OR c.client_ref LIKE @s)";
            p["@s"] = $"%{search.Trim().ToLower()}%";
        }
        if (filterStatus == "active")   countSql += " AND c.is_active=TRUE";
        if (filterStatus == "inactive") countSql += " AND c.is_active=FALSE";

        var totalRecords = Convert.ToInt32(await _db.ExecuteScalarAsync(countSql, p));
        var (skip, take, totalPages) = PaginationHelper.GetPaginationParams(page, totalRecords, pageSize);

        var sql = @"
            SELECT c.id, c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   c.city_id, c.module_id, c.id AS user_id, c.address, c.notes,
                   c.total_amount, c.source_inquiry_id,
                   c.is_active, c.created_by, c.updated_by, c.created_at, c.updated_at,
                   ci.name AS city_name, mo.name AS module_name,
                   u.full_name AS created_by_name,
                   COALESCE((SELECT SUM(pa.amount) FROM payments pa WHERE pa.client_id=c.id AND pa.is_deleted=FALSE),0) AS total_paid
            FROM users c
            LEFT JOIN cfg_city   ci ON ci.id=c.city_id
            LEFT JOIN cfg_module mo ON mo.id=c.module_id
            LEFT JOIN users       u ON u.id=c.created_by
            WHERE c.is_deleted=FALSE AND c.role='Client'";
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(c.company_name) LIKE @s OR LOWER(c.contact_person) LIKE @s OR c.phone LIKE @s OR c.client_ref LIKE @s)";
            p["@s"] = $"%{search.Trim().ToLower()}%";
        }
        if (filterStatus == "active")   sql += " AND c.is_active=TRUE";
        if (filterStatus == "inactive") sql += " AND c.is_active=FALSE";
        
        sql += $" ORDER BY c.created_at DESC LIMIT {take} OFFSET {skip}";

        var rows = await _db.QueryAsync(sql, p);
        var vm = new ClientListViewModel
        {
            Clients = rows.Select(MapRow).ToList(),
            Search = search,
            FilterStatus = filterStatus,
            Pagination = new PaginationInfo
            {
                CurrentPage = page,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                TotalPages = totalPages
            }
        };
        return View(vm);
    }

    // ── DETAILS ──────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var client = await GetById(id);
        if (client == null) return NotFound();

        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        if (role == "Client" && client.Id != userId)
            return RedirectToAction("AccessDenied", "Auth");

        // payments
        var payments = await _db.QueryAsync(@"
            SELECT p.id, p.amount, p.payment_mode, p.payment_date, p.note, p.proof_file,
                   u.full_name AS created_by_name
            FROM payments p
            LEFT JOIN users u ON u.id=p.created_by
            WHERE p.client_id=@cid AND p.is_deleted=FALSE
            ORDER BY p.payment_date DESC, p.created_at DESC",
            new() { ["@cid"] = id });

        ViewBag.Payments = payments;

        // inquiries linked to this client
        var inquiries = await _db.QueryAsync(@"
            SELECT i.id, i.hotel_name, i.client_name, i.client_number, i.module_id,
                   i.created_at, i.note,
                   st.id AS status_id, st.name AS status_name, ci.name AS city_name, mo.name AS module_name
            FROM inquiries i
            LEFT JOIN cfg_status st ON st.id=i.status_id
            LEFT JOIN cfg_city   ci ON ci.id=i.city_id
            LEFT JOIN cfg_module mo ON mo.id=i.module_id
            WHERE i.converted_client_id=@cid AND i.is_deleted=FALSE
            ORDER BY i.created_at DESC",
            new() { ["@cid"] = id });
        ViewBag.Inquiries = inquiries;

        return View(client);
    }

    // ── EDIT GET ─────────────────────────────────────────────
    [SessionAuth(SessionHelper.RoleClient)]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        var client = await GetById(id);
        if (client == null) return NotFound();

        // Clients can only edit their own record
        if (!userId.HasValue || client.Id != userId.Value)
            return RedirectToAction("AccessDenied", "Auth");

        return View(new ClientFormViewModel { Client = client, Cities = await LoadActive("cfg_city"), Modules = await LoadActive("cfg_module") });
    }

    // ── EDIT POST ────────────────────────────────────────────
    [HttpPost]
    [SessionAuth(SessionHelper.RoleClient)]
    public async Task<IActionResult> Edit(int id, ClientFormViewModel vm)
    {
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        var existing = await GetById(id);
        if (existing == null) return NotFound();

        // Clients can only edit their own record
        if (!userId.HasValue || existing.Id != userId.Value)
            return RedirectToAction("AccessDenied", "Auth");
        if (string.IsNullOrWhiteSpace(vm.Client.CompanyName) ||
            string.IsNullOrWhiteSpace(vm.Client.ContactPerson) ||
            string.IsNullOrWhiteSpace(vm.Client.Phone))
        {
            TempData["Error"] = "Company Name, Contact Person and Phone are required.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var phone = NormalizePhone(vm.Client.Phone.Trim());
        
        try
        {
            // Check for duplicate phone (excluding self)
            var dup = await _db.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM users WHERE phone=@p AND is_deleted=FALSE AND id<>@id",
                new() { ["@p"] = phone, ["@id"] = id });
            
            if (Convert.ToInt64(dup) > 0)
            {
                TempData["Error"] = $"Another account with phone {phone} already exists.";
                await ReloadDropdowns(vm);
                return View(vm);
            }

            var editUserId = HttpContext.Session.GetInt32(SessionHelper.UserId);
            var cityId = await ResolveCityIdAsync(vm.Client.CityText);

            // Clients update contact details and module
            await _db.ExecuteNonQueryAsync(@"
                UPDATE users SET
                  contact_person=@cp, phone=@ph, email=@em, city_id=@ci, module_id=@mo, address=@ad, notes=@no,
                  updated_by=@ub, updated_at=NOW()
                WHERE id=@id",
                new()
                {
                    ["@cp"] = vm.Client.ContactPerson.Trim(),
                    ["@ph"] = phone,
                    ["@em"] = (object?)(vm.Client.Email?.Trim()) ?? DBNull.Value,
                    ["@ci"] = (object?)cityId ?? DBNull.Value,
                    ["@mo"] = (object?)vm.Client.ModuleId ?? DBNull.Value,
                    ["@ad"] = (object?)(vm.Client.Address?.Trim()) ?? DBNull.Value,
                    ["@no"] = (object?)(vm.Client.Notes?.Trim()) ?? DBNull.Value,
                    ["@ub"] = (object?)editUserId ?? DBNull.Value,
                    ["@id"] = id
                });

            TempData["Success"] = "Profile updated successfully.";
            return RedirectToAction("Details", new { id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"An error occurred while updating: {ex.Message}";
            await ReloadDropdowns(vm);
            return View(vm);
        }
    }

    // ── TOGGLE ACTIVE ────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Toggle(int id)
    {
        if (!SessionHelper.IsAdmin(HttpContext.Session)) return RedirectToAction("AccessDenied", "Auth");
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(
            "UPDATE users SET is_active=NOT is_active, updated_by=@ub, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = id, ["@ub"] = (object?)userId ?? DBNull.Value });
        TempData["Success"] = "Client status updated.";
        return RedirectToAction("Index");
    }

    // ── TOGGLE ACTIVE STATUS ────────────────────────────────
    [HttpPost]
    [SessionAuth(SessionHelper.RoleAdmin)]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var client = await GetById(id);
        if (client == null) return NotFound();
        
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        var newStatus = !client.IsActive;
        
        await _db.ExecuteNonQueryAsync(
            "UPDATE users SET is_active=@status, updated_by=@ub, updated_at=NOW() WHERE id=@id",
            new() { ["@status"] = newStatus, ["@id"] = id, ["@ub"] = (object?)userId ?? DBNull.Value });
        
        TempData["Success"] = newStatus ? "Account activated." : "Account deactivated.";
        return RedirectToAction("Details", new { id });
    }



    // ── EXPORT EXCEL ─────────────────────────────────────────
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Export(string? search, string? filterStatus)
    {
        var sql = @"
            SELECT c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   ci.name AS city_name, mo.name AS module_name,
                   c.total_amount,
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE),0) AS total_paid,
                   c.is_active, u.full_name AS created_by_name, c.created_at
            FROM users c
            LEFT JOIN cfg_city ci ON ci.id=c.city_id
            LEFT JOIN cfg_module mo ON mo.id=c.module_id
            LEFT JOIN users u ON u.id=c.created_by
            WHERE c.is_deleted=FALSE AND c.role='Client'";
        var p = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(search)) { sql += " AND (LOWER(c.company_name) LIKE @s OR c.phone LIKE @s)"; p["@s"] = $"%{search.ToLower()}%"; }
        if (filterStatus == "active")   sql += " AND c.is_active=TRUE";
        if (filterStatus == "inactive") sql += " AND c.is_active=FALSE";
        sql += " ORDER BY c.created_at DESC";

        var rows = await _db.QueryAsync(sql, p);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Clients");
        string[] headers = { "#","Ref","Company","Contact","Phone","Email","City","Module","Total Amount","Paid","Remaining","Status","Created By","Date" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i+1).Value = headers[i];
        var hRow = ws.Row(1); hRow.Style.Font.Bold = true; hRow.Style.Fill.BackgroundColor = XLColor.FromArgb(79,70,229); hRow.Style.Font.FontColor = XLColor.White;
        int row = 2;
        foreach (var r in rows)
        {
            var total = Convert.ToDecimal(r["total_amount"]);
            var paid  = Convert.ToDecimal(r["total_paid"]);
            ws.Cell(row, 1).Value  = row - 1;
            ws.Cell(row, 2).Value  = r["client_ref"]?.ToString();
            ws.Cell(row, 3).Value  = r["company_name"]?.ToString();
            ws.Cell(row, 4).Value  = r["contact_person"]?.ToString();
            ws.Cell(row, 5).Value  = r["phone"]?.ToString();
            ws.Cell(row, 6).Value  = r["email"]?.ToString();
            ws.Cell(row, 7).Value  = r["city_name"]?.ToString();
            ws.Cell(row, 8).Value  = r["module_name"]?.ToString();
            ws.Cell(row, 9).Value  = total;
            ws.Cell(row, 10).Value = paid;
            ws.Cell(row, 11).Value = total - paid;
            ws.Cell(row, 12).Value = Convert.ToBoolean(r["is_active"]) ? "Active" : "Inactive";
            ws.Cell(row, 13).Value = r["created_by_name"]?.ToString();
            ws.Cell(row, 14).Value = Convert.ToDateTime(r["created_at"]).ToString("dd MMM yyyy");
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Clients_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ── private helpers ──────────────────────────────────────
    private async Task<List<ConfigItem>> LoadActive(string table)
    {
        var rows = await _db.QueryAsync($"SELECT id, name FROM {table} WHERE is_active=TRUE ORDER BY name", new());
        return rows.Select(r => new ConfigItem { Id = Convert.ToInt32(r["id"]), Name = r["name"]?.ToString() ?? "" }).ToList();
    }

    private async Task ReloadDropdowns(ClientFormViewModel vm)
    {
        vm.Cities  = await LoadActive("cfg_city");
        vm.Modules = await LoadActive("cfg_module");
    }

    private async Task<Client?> GetById(int id)
    {
        var rows = await _db.QueryAsync(@"
            SELECT c.id, c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   c.city_id, c.module_id, c.address, c.notes,
                   c.total_amount, c.source_inquiry_id,
                   c.is_active, c.is_deleted, c.created_by, c.updated_by, c.created_at, c.updated_at,
                   ci.name AS city_name, mo.name AS module_name,
                   u.full_name AS created_by_name,
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE),0) AS total_paid
            FROM users c
            LEFT JOIN cfg_city   ci ON ci.id=c.city_id
            LEFT JOIN cfg_module mo ON mo.id=c.module_id
            LEFT JOIN users       u ON u.id=c.created_by
            WHERE c.id=@id AND c.is_deleted=FALSE",
            new() { ["@id"] = id });
        return rows.Count == 0 ? null : MapRow(rows[0]);
    }

    private static Client MapRow(Dictionary<string, object?> r) => new()
    {
        Id               = Convert.ToInt32(r["id"]),
        ClientRef        = r["client_ref"]?.ToString() ?? "",
        CompanyName      = r["company_name"]?.ToString() ?? "",
        ContactPerson    = r["contact_person"]?.ToString() ?? "",
        Phone            = r["phone"]?.ToString() ?? "",
        Email            = r["email"]?.ToString(),
        CityId           = r["city_id"] is DBNull or null ? null : Convert.ToInt32(r["city_id"]),
        CityName         = r["city_name"]?.ToString(),
        ModuleId         = r["module_id"] is DBNull or null ? null : Convert.ToInt32(r["module_id"]),
        ModuleName       = r["module_name"]?.ToString(),
        RoomSize         = r.ContainsKey("room_size") ? r["room_size"]?.ToString() : null,
        Address          = r["address"]?.ToString(),
        Notes            = r["notes"]?.ToString(),
        TotalAmount      = r["total_amount"] is DBNull or null ? 0 : Convert.ToDecimal(r["total_amount"]),
        TotalPaid        = r["total_paid"] is DBNull or null ? 0 : Convert.ToDecimal(r["total_paid"]),
        SourceInquiryId  = r["source_inquiry_id"] is DBNull or null ? null : Convert.ToInt32(r["source_inquiry_id"]),
        IsActive         = Convert.ToBoolean(r["is_active"]),
        IsDeleted        = Convert.ToBoolean(r.ContainsKey("is_deleted") ? r["is_deleted"] : false),
        CreatedBy        = r["created_by"] is DBNull or null ? null : Convert.ToInt32(r["created_by"]),
        CreatedByName    = r["created_by_name"]?.ToString(),
        UpdatedBy        = r.ContainsKey("updated_by") && r["updated_by"] is not (DBNull or null) ? Convert.ToInt32(r["updated_by"]) : null,
        CreatedAt        = Convert.ToDateTime(r["created_at"]),
        UpdatedAt        = Convert.ToDateTime(r["updated_at"])
    };

    private async Task<int?> ResolveCityIdAsync(string? cityText)
    {
        if (string.IsNullOrWhiteSpace(cityText)) return null;
        var name = cityText.Trim();
        // Atomic upsert prevents race condition on concurrent inserts for the same city name
        var id = await _db.ExecuteScalarAsync(
            "INSERT INTO cfg_city (name, is_active) VALUES (@n, TRUE) ON CONFLICT (name) DO NOTHING RETURNING id",
            new() { ["@n"] = name });
        if (id != null && id != DBNull.Value)
            return Convert.ToInt32(id);
        var rows = await _db.QueryAsync(
            "SELECT id FROM cfg_city WHERE LOWER(name)=LOWER(@n) LIMIT 1",
            new() { ["@n"] = name });
        return rows.Count > 0 ? Convert.ToInt32(rows[0]["id"]) : null;
    }

    private static string NormalizePhone(string phone)
    {
        var digits = System.Text.RegularExpressions.Regex.Replace(phone, @"\D", "");
        if (digits.Length > 10 && digits.StartsWith("91")) digits = digits[2..];
        if (digits.Length > 10 && digits.StartsWith("0"))  digits = digits[1..];
        return digits.Length >= 10 ? digits : phone.Trim();
    }
}

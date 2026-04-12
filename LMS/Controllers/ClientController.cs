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
    public async Task<IActionResult> Index(string? search, string? filterStatus)
    {
        var isClient = HttpContext.Session.GetString(SessionHelper.UserRole) == "Client";
        var userId   = HttpContext.Session.GetInt32(SessionHelper.UserId);

        if (isClient && userId.HasValue)
        {
            var myRows = await _db.QueryAsync(
                "SELECT id FROM clients WHERE user_id=@uid AND is_deleted=FALSE LIMIT 1",
                new() { ["@uid"] = userId.Value });
            if (myRows.Count > 0)
                return RedirectToAction("Details", new { id = Convert.ToInt32(myRows[0]["id"]) });
            TempData["Error"] = "Your client profile was not found. Please contact admin.";
            return View(new ClientListViewModel());
        }

        var sql = @"
            SELECT c.id, c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   c.city_id, c.module_id, c.room_size, c.user_id, c.address, c.notes,
                   c.total_amount, c.source_inquiry_id,
                   c.is_active, c.created_by, c.updated_by, c.created_at, c.updated_at,
                   ci.name AS city_name, mo.name AS module_name,
                   u.full_name AS created_by_name,
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE),0) AS total_paid
            FROM clients c
            LEFT JOIN cfg_city   ci ON ci.id=c.city_id
            LEFT JOIN cfg_module mo ON mo.id=c.module_id
            LEFT JOIN users       u ON u.id=c.created_by
            WHERE c.is_deleted=FALSE";

        var p = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(c.company_name) LIKE @s OR LOWER(c.contact_person) LIKE @s OR c.phone LIKE @s OR c.client_ref LIKE @s)";
            p["@s"] = $"%{search.Trim().ToLower()}%";
        }
        if (filterStatus == "active")   sql += " AND c.is_active=TRUE";
        if (filterStatus == "inactive") sql += " AND c.is_active=FALSE";
        sql += " ORDER BY c.created_at DESC";

        var rows = await _db.QueryAsync(sql, p);
        return View(new ClientListViewModel { Clients = rows.Select(MapRow).ToList(), Search = search, FilterStatus = filterStatus });
    }

    // ── CREATE GET ───────────────────────────────────────────
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Create()
        => View(new ClientFormViewModel { Cities = await LoadActive("cfg_city"), Modules = await LoadActive("cfg_module") });

    // ── CREATE POST ──────────────────────────────────────────
    [HttpPost]
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Create(ClientFormViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Client.CompanyName) ||
            string.IsNullOrWhiteSpace(vm.Client.ContactPerson) ||
            string.IsNullOrWhiteSpace(vm.Client.Phone))
        {
            TempData["Error"] = "Company Name, Contact Person and Phone are required.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var phone = vm.Client.Phone.Trim();
        var dup = await _db.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM clients WHERE phone=@p AND is_deleted=FALSE",
            new() { ["@p"] = phone });
        if (Convert.ToInt64(dup) > 0)
        {
            TempData["Error"] = $"A client with phone {phone} already exists.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var userId  = HttpContext.Session.GetInt32(SessionHelper.UserId);
        var cityId  = await ResolveCityIdAsync(vm.Client.CityText);
        var refNum  = await NextClientRef();

        var newId = await _db.ExecuteScalarAsync(@"
            INSERT INTO clients
              (client_ref, company_name, contact_person, phone, email, city_id, module_id,
               room_size, address, notes, total_amount, source_inquiry_id, is_active, created_by, updated_by)
            VALUES
              (@ref, @cn, @cp, @ph, @em, @ci, @mo, @rs, @ad, @no, @ta, @si, TRUE, @cb, @cb)
            RETURNING id",
            new()
            {
                ["@ref"] = refNum,
                ["@cn"]  = vm.Client.CompanyName.Trim(),
                ["@cp"]  = vm.Client.ContactPerson.Trim(),
                ["@ph"]  = phone,
                ["@em"]  = (object?)(vm.Client.Email?.Trim()) ?? DBNull.Value,
                ["@ci"]  = (object?)cityId ?? DBNull.Value,
                ["@mo"]  = (object?)vm.Client.ModuleId ?? DBNull.Value,
                ["@rs"]  = (object?)(vm.Client.RoomSize?.Trim()) ?? DBNull.Value,
                ["@ad"]  = (object?)(vm.Client.Address?.Trim()) ?? DBNull.Value,
                ["@no"]  = (object?)(vm.Client.Notes?.Trim()) ?? DBNull.Value,
                ["@ta"]  = vm.Client.TotalAmount,
                ["@si"]  = (object?)vm.Client.SourceInquiryId ?? DBNull.Value,
                ["@cb"]  = (object?)userId ?? DBNull.Value
            });

        var clientId = Convert.ToInt32(newId);

        // ── Create login account via one-time setup link when email is provided ──
        if (!string.IsNullOrWhiteSpace(vm.Client.Email))
        {
            var email = vm.Client.Email.Trim().ToLower();
            var emailDup = await _db.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM users WHERE LOWER(email)=@em",
                new() { ["@em"] = email });
            if (Convert.ToInt64(emailDup) > 0)
            {
                TempData["Error"] = "A user account with that email already exists. Client was created but no setup link was generated.";
            }
            else
            {
                var token = Guid.NewGuid().ToString("N");
                var newUserId = await _db.ExecuteScalarAsync(@"
                    INSERT INTO users (full_name, email, password, role, is_active, setup_token, setup_token_expires)
                    VALUES (@fn, @em, '', 'Client', FALSE, @tok, NOW() + INTERVAL '7 days')
                    RETURNING id",
                    new()
                    {
                        ["@fn"]  = vm.Client.ContactPerson.Trim(),
                        ["@em"]  = email,
                        ["@tok"] = token
                    });
                await _db.ExecuteNonQueryAsync(
                    "UPDATE clients SET user_id=@uid WHERE id=@cid",
                    new() { ["@uid"] = Convert.ToInt32(newUserId), ["@cid"] = clientId });
                TempData["SetupLink"] = Url.Action("SetupPassword", "Auth", new { token }, Request.Scheme);
            }
        }

        // If converted from inquiry, mark inquiry as converted
        if (vm.Client.SourceInquiryId.HasValue)
        {
            await _db.ExecuteNonQueryAsync(@"
                UPDATE inquiries SET is_converted=TRUE, converted_client_id=@cid,
                  updated_by=@ub, updated_at=NOW() WHERE id=@id",
                new()
                {
                    ["@cid"] = clientId,
                    ["@ub"]  = (object?)userId ?? DBNull.Value,
                    ["@id"]  = vm.Client.SourceInquiryId.Value
                });

            // Set status to "Converted" if that status exists
            await _db.ExecuteNonQueryAsync(@"
                UPDATE inquiries SET status_id=(SELECT id FROM cfg_status WHERE LOWER(name)='converted' LIMIT 1)
                WHERE id=@id AND EXISTS(SELECT 1 FROM cfg_status WHERE LOWER(name)='converted')",
                new() { ["@id"] = vm.Client.SourceInquiryId.Value });

            if (TempData["Error"] == null)
                TempData["Success"] = "Inquiry converted to Client successfully.";
            return RedirectToAction("Details", new { id = clientId });
        }

        if (TempData["Error"] == null)
            TempData["Success"] = "Client added successfully.";
        return RedirectToAction("Details", new { id = clientId });
    }

    // ── DETAILS ──────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var client = await GetById(id);
        if (client == null) return NotFound();

        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        if (role == "Client" && client.UserId != userId)
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

        // Check whether the linked user account is still pending setup
        if (client.UserId.HasValue)
        {
            var setupRows = await _db.QueryAsync(
                "SELECT 1 FROM users WHERE id=@uid AND is_active=FALSE AND setup_token IS NOT NULL AND setup_token_expires > NOW()",
                new() { ["@uid"] = client.UserId.Value });
            ViewBag.SetupPending = setupRows.Count > 0;
        }
        else
        {
            ViewBag.SetupPending = false;
        }

        return View(client);
    }

    // ── EDIT GET ─────────────────────────────────────────────
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Edit(int id)
    {
        var client = await GetById(id);
        if (client == null) return NotFound();
        return View(new ClientFormViewModel { Client = client, Cities = await LoadActive("cfg_city"), Modules = await LoadActive("cfg_module") });
    }

    // ── EDIT POST ────────────────────────────────────────────
    [HttpPost]
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Edit(int id, ClientFormViewModel vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Client.CompanyName) ||
            string.IsNullOrWhiteSpace(vm.Client.ContactPerson) ||
            string.IsNullOrWhiteSpace(vm.Client.Phone))
        {
            TempData["Error"] = "Company Name, Contact Person and Phone are required.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var phone = vm.Client.Phone.Trim();
        var dup = await _db.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM clients WHERE phone=@p AND is_deleted=FALSE AND id<>@id",
            new() { ["@p"] = phone, ["@id"] = id });
        if (Convert.ToInt64(dup) > 0)
        {
            TempData["Error"] = $"Another client with phone {phone} already exists.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        var cityId = await ResolveCityIdAsync(vm.Client.CityText);

        await _db.ExecuteNonQueryAsync(@"
            UPDATE clients SET
              company_name=@cn, contact_person=@cp, phone=@ph, email=@em,
              city_id=@ci, module_id=@mo, room_size=@rs, address=@ad, notes=@no,
              total_amount=@ta, updated_by=@ub, updated_at=NOW()
            WHERE id=@id",
            new()
            {
                ["@cn"] = vm.Client.CompanyName.Trim(),
                ["@cp"] = vm.Client.ContactPerson.Trim(),
                ["@ph"] = phone,
                ["@em"] = (object?)(vm.Client.Email?.Trim()) ?? DBNull.Value,
                ["@ci"] = (object?)cityId ?? DBNull.Value,
                ["@mo"] = (object?)vm.Client.ModuleId ?? DBNull.Value,
                ["@rs"] = (object?)(vm.Client.RoomSize?.Trim()) ?? DBNull.Value,
                ["@ad"] = (object?)(vm.Client.Address?.Trim()) ?? DBNull.Value,
                ["@no"] = (object?)(vm.Client.Notes?.Trim()) ?? DBNull.Value,
                ["@ta"] = vm.Client.TotalAmount,
                ["@ub"] = (object?)userId ?? DBNull.Value,
                ["@id"] = id
            });

        TempData["Success"] = "Client updated successfully.";
        return RedirectToAction("Details", new { id });
    }

    // ── TOGGLE ACTIVE ────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Toggle(int id)
    {
        if (!SessionHelper.IsAdmin(HttpContext.Session)) return RedirectToAction("AccessDenied", "Auth");
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(
            "UPDATE clients SET is_active=NOT is_active, updated_by=@ub, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = id, ["@ub"] = (object?)userId ?? DBNull.Value });
        TempData["Success"] = "Client status updated.";
        return RedirectToAction("Index");
    }

    // ── SOFT DELETE ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        if (!SessionHelper.IsAdmin(HttpContext.Session)) return RedirectToAction("AccessDenied", "Auth");
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(
            "UPDATE clients SET is_deleted=TRUE, updated_by=@ub, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = id, ["@ub"] = (object?)userId ?? DBNull.Value });
        TempData["Success"] = "Client removed.";
        return RedirectToAction("Index");
    }

    // ── REGENERATE SETUP LINK ────────────────────────────────
    [HttpPost]
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> RegenerateSetupLink(int id)
    {
        var client = await GetById(id);
        if (client == null) return NotFound();

        if (!client.UserId.HasValue)
        {
            TempData["Error"] = "No linked user account found for this client.";
            return RedirectToAction("Details", new { id });
        }

        var token = Guid.NewGuid().ToString("N");
        var rows = await _db.ExecuteNonQueryAsync(
            "UPDATE users SET setup_token=@tok, setup_token_expires=NOW() + INTERVAL '7 days' WHERE id=@uid AND is_active=FALSE",
            new() { ["@tok"] = token, ["@uid"] = client.UserId.Value });

        if (rows == 0)
        {
            TempData["Error"] = "Account is already active — no new setup link needed.";
        }
        else
        {
            TempData["SetupLink"] = Url.Action("SetupPassword", "Auth", new { token }, Request.Scheme);
            TempData["Success"] = "New setup link generated (valid for 7 days).";
        }
        return RedirectToAction("Details", new { id });
    }

    // ── EXPORT EXCEL ─────────────────────────────────────────
    public async Task<IActionResult> Export(string? search, string? filterStatus)
    {
        var sql = @"
            SELECT c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   ci.name AS city_name, mo.name AS module_name, c.room_size,
                   c.total_amount,
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE),0) AS total_paid,
                   c.is_active, u.full_name AS created_by_name, c.created_at
            FROM clients c
            LEFT JOIN cfg_city ci ON ci.id=c.city_id
            LEFT JOIN cfg_module mo ON mo.id=c.module_id
            LEFT JOIN users u ON u.id=c.created_by
            WHERE c.is_deleted=FALSE";
        var p = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(search)) { sql += " AND (LOWER(c.company_name) LIKE @s OR c.phone LIKE @s)"; p["@s"] = $"%{search.ToLower()}%"; }
        if (filterStatus == "active")   sql += " AND c.is_active=TRUE";
        if (filterStatus == "inactive") sql += " AND c.is_active=FALSE";
        sql += " ORDER BY c.created_at DESC";

        var rows = await _db.QueryAsync(sql, p);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Clients");
        string[] headers = { "#","Ref","Company","Contact","Phone","Email","City","Module","Room Size","Total Amount","Paid","Remaining","Status","Created By","Date" };
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
            ws.Cell(row, 9).Value  = r["room_size"]?.ToString();
            ws.Cell(row, 10).Value = total;
            ws.Cell(row, 11).Value = paid;
            ws.Cell(row, 12).Value = total - paid;
            ws.Cell(row, 13).Value = Convert.ToBoolean(r["is_active"]) ? "Active" : "Inactive";
            ws.Cell(row, 14).Value = r["created_by_name"]?.ToString();
            ws.Cell(row, 15).Value = Convert.ToDateTime(r["created_at"]).ToString("dd MMM yyyy");
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

    private async Task<string> NextClientRef()
    {
        var val = await _db.ExecuteScalarAsync("SELECT nextval('client_ref_seq')", new());
        return $"LMS-{Convert.ToInt64(val):D4}";
    }

    private async Task<Client?> GetById(int id)
    {
        var rows = await _db.QueryAsync(@"
            SELECT c.id, c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   c.city_id, c.module_id, c.room_size, c.user_id, c.address, c.notes,
                   c.total_amount, c.source_inquiry_id,
                   c.is_active, c.is_deleted, c.created_by, c.updated_by, c.created_at, c.updated_at,
                   ci.name AS city_name, mo.name AS module_name,
                   u.full_name AS created_by_name,
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE),0) AS total_paid
            FROM clients c
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
        UserId           = r["user_id"] is DBNull or null ? null : Convert.ToInt32(r["user_id"]),
        CompanyName      = r["company_name"]?.ToString() ?? "",
        ContactPerson    = r["contact_person"]?.ToString() ?? "",
        Phone            = r["phone"]?.ToString() ?? "",
        Email            = r["email"]?.ToString(),
        CityId           = r["city_id"] is DBNull or null ? null : Convert.ToInt32(r["city_id"]),
        CityName         = r["city_name"]?.ToString(),
        ModuleId         = r["module_id"] is DBNull or null ? null : Convert.ToInt32(r["module_id"]),
        ModuleName       = r["module_name"]?.ToString(),
        RoomSize         = r["room_size"]?.ToString(),
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
        var rows = await _db.QueryAsync("SELECT id FROM cfg_city WHERE LOWER(name)=LOWER(@n) LIMIT 1", new() { ["@n"] = name });
        if (rows.Count > 0) return Convert.ToInt32(rows[0]["id"]);
        var newId = await _db.ExecuteScalarAsync("INSERT INTO cfg_city (name, is_active) VALUES (@n, TRUE) RETURNING id", new() { ["@n"] = name });
        return Convert.ToInt32(newId);
    }
}

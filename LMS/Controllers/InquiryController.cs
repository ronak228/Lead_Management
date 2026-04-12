using LeadManagementSystem.Data;
using LeadManagementSystem.Filters;
using LeadManagementSystem.Helpers;
using LeadManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;

namespace LeadManagementSystem.Controllers;

// All authenticated users can access inquiries — Clients are scoped to their own
[SessionAuth]
public class InquiryController : Controller
{
    private readonly DbHelper _db;

    public InquiryController(DbHelper db) => _db = db;

    // ── helpers ──────────────────────────────────────────────
    private async Task<List<ConfigItem>> LoadActive(string table)
    {
        var rows = await _db.QueryAsync(
            $"SELECT id, name FROM {table} WHERE is_active=TRUE ORDER BY name", new());
        return rows.Select(r => new ConfigItem
        {
            Id   = Convert.ToInt32(r["id"]),
            Name = r["name"]?.ToString() ?? ""
        }).ToList();
    }

    // ── LIST ─────────────────────────────────────────────────
    public async Task<IActionResult> Index(
        string? search, int? filterStatus, int? filterModule,
        string? filterPayment, string? filterCity, string? dateFrom, string? dateTo)
    {
        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);

        var sql = @"
            SELECT i.id, i.hotel_name, i.client_name, i.client_number,
                   i.city_id, i.module_id, i.status_id, i.created_by, i.updated_by,
                   i.payment_received, i.followup_date, i.note,
                   i.is_converted, i.converted_client_id,
                   i.created_at, i.updated_at,
                   ci.name  AS city_name,
                   mo.name  AS module_name,
                   st.name  AS status_name,
                   u.full_name AS created_by_name
            FROM inquiries i
            LEFT JOIN cfg_city    ci ON ci.id = i.city_id
            LEFT JOIN cfg_module  mo ON mo.id = i.module_id
            LEFT JOIN cfg_status  st ON st.id = i.status_id
            LEFT JOIN users        u ON u.id  = i.created_by
            WHERE i.is_deleted=FALSE";

        var p = new Dictionary<string, object?>();

        // Client: scope to their own inquiries
        if (role == SessionHelper.RoleClient && userId.HasValue)
        {
            var myClient = await GetClientByUserId(userId.Value);
            if (myClient != null)
            {
                sql += " AND (i.client_number=@cph OR i.created_by=@cuid)";
                p["@cph"]  = myClient.Phone;
                p["@cuid"] = userId.Value;
            }
            else
            {
                sql += " AND i.created_by=@cuid";
                p["@cuid"] = userId.Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(i.hotel_name) LIKE @s OR LOWER(i.client_name) LIKE @s OR i.client_number LIKE @s)";
            p["@s"] = $"%{search.Trim().ToLower()}%";
        }
        if (filterStatus.HasValue) { sql += " AND i.status_id=@st";  p["@st"] = filterStatus.Value; }
        if (filterModule.HasValue) { sql += " AND i.module_id=@mo";  p["@mo"] = filterModule.Value; }
        if (!string.IsNullOrWhiteSpace(filterCity)) { sql += " AND LOWER(ci.name) LIKE @cy"; p["@cy"] = $"%{filterCity.ToLower()}%"; }
        if (filterPayment == "yes") sql += " AND i.payment_received=TRUE";
        if (filterPayment == "no")  sql += " AND i.payment_received=FALSE";
        if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var df)) { sql += " AND i.created_at >= @df"; p["@df"] = df; }
        if (!string.IsNullOrWhiteSpace(dateTo)   && DateTime.TryParse(dateTo,   out var dt)) { sql += " AND i.created_at <  @dt"; p["@dt"] = dt.AddDays(1); }

        sql += " ORDER BY i.created_at DESC";

        var rows = await _db.QueryAsync(sql, p);
        var vm = new InquiryListViewModel
        {
            Inquiries     = rows.Select(MapRow).ToList(),
            Statuses      = await LoadActive("cfg_status"),
            Modules       = await LoadActive("cfg_module"),
            Cities        = await LoadActive("cfg_city"),
            Search        = search,
            FilterStatus  = filterStatus,
            FilterModule  = filterModule,
            FilterPayment = filterPayment,
            FilterCity    = filterCity,
            DateFrom      = dateFrom,
            DateTo        = dateTo
        };
        return View(vm);
    }

    // ── CREATE GET ───────────────────────────────────────────
    public async Task<IActionResult> Create()
    {
        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        var vm = new InquiryFormViewModel
        {
            Cities   = await LoadActive("cfg_city"),
            Modules  = await LoadActive("cfg_module"),
            Statuses = await LoadActive("cfg_status")
        };
        // Pre-fill client's own details
        if (role == SessionHelper.RoleClient && userId.HasValue)
        {
            var myClient = await GetClientByUserId(userId.Value);
            if (myClient != null)
            {
                vm.Inquiry.ClientName   = myClient.ContactPerson;
                vm.Inquiry.ClientNumber = myClient.Phone;
                vm.Inquiry.HotelName    = myClient.CompanyName;
            }
        }
        return View(vm);
    }

    // ── CREATE POST ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create(InquiryFormViewModel vm)
    {
        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);

        // Client: force their own phone number to prevent identity spoofing
        if (role == SessionHelper.RoleClient && userId.HasValue)
        {
            var myClient = await GetClientByUserId(userId.Value);
            if (myClient != null)
            {
                vm.Inquiry.ClientName   = myClient.ContactPerson;
                vm.Inquiry.ClientNumber = myClient.Phone;
            }
        }

        if (string.IsNullOrWhiteSpace(vm.Inquiry.HotelName) ||
            string.IsNullOrWhiteSpace(vm.Inquiry.ClientName) ||
            string.IsNullOrWhiteSpace(vm.Inquiry.ClientNumber))
        {
            TempData["Error"] = "Hotel Name, Client Name and Client Number are required.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var phone = vm.Inquiry.ClientNumber.Trim();
        // Duplicate phone check is staff-only — clients may have multiple service inquiries
        if (role != SessionHelper.RoleClient)
        {
            var dup = await _db.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM inquiries WHERE client_number=@p AND is_deleted=FALSE",
                new() { ["@p"] = phone });
            if (Convert.ToInt64(dup) > 0)
            {
                TempData["Error"] = $"An active inquiry already exists for phone {phone}.";
                await ReloadDropdowns(vm);
                return View(vm);
            }
        }

        var cityId = await ResolveCityIdAsync(vm.Inquiry.CityText);

        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO inquiries
              (hotel_name, client_name, client_number, city_id, module_id, status_id,
               payment_received, followup_date, note, created_by, updated_by)
            VALUES
              (@hn, @cn, @cnum, @ci, @mo, @st, @pr, @fd, @no, @cb, @cb)",
            new()
            {
                ["@hn"]   = vm.Inquiry.HotelName.Trim(),
                ["@cn"]   = vm.Inquiry.ClientName.Trim(),
                ["@cnum"] = phone,
                ["@ci"]   = (object?)cityId ?? DBNull.Value,
                ["@mo"]   = (object?)vm.Inquiry.ModuleId ?? DBNull.Value,
                ["@st"]   = (object?)vm.Inquiry.StatusId ?? DBNull.Value,
                ["@pr"]   = vm.Inquiry.PaymentReceived,
                ["@fd"]   = (object?)vm.Inquiry.FollowupDate ?? DBNull.Value,
                ["@no"]   = (object?)(vm.Inquiry.Note?.Trim()) ?? DBNull.Value,
                ["@cb"]   = (object?)userId ?? DBNull.Value
            });

        TempData["Success"] = "Inquiry added successfully.";
        return RedirectToAction("Index");
    }

    // ── DETAILS ──────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var inq = await GetById(id);
        if (inq == null) return NotFound();
        var clientPhone = await GetCurrentClientPhoneAsync();
        if (!CanAccess(inq, clientPhone)) return RedirectToAction("AccessDenied", "Auth");
        return View(inq);
    }

    // ── EDIT GET ─────────────────────────────────────────────
    public async Task<IActionResult> Edit(int id)
    {
        var inq = await GetById(id);
        if (inq == null) return NotFound();
        var clientPhone = await GetCurrentClientPhoneAsync();
        if (!CanAccess(inq, clientPhone)) return RedirectToAction("AccessDenied", "Auth");
        if (inq.IsConverted) { TempData["Error"] = "Converted inquiries cannot be edited."; return RedirectToAction("Details", new { id }); }

        var vm = new InquiryFormViewModel
        {
            Inquiry  = inq,
            Cities   = await LoadActive("cfg_city"),
            Modules  = await LoadActive("cfg_module"),
            Statuses = await LoadActive("cfg_status")
        };
        return View(vm);
    }

    // ── EDIT POST ────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Edit(int id, InquiryFormViewModel vm)
    {
        var existing = await GetById(id);
        if (existing == null) return NotFound();
        var clientPhone = await GetCurrentClientPhoneAsync();
        if (!CanAccess(existing, clientPhone)) return RedirectToAction("AccessDenied", "Auth");
        if (existing.IsConverted) { TempData["Error"] = "Converted inquiries cannot be edited."; return RedirectToAction("Details", new { id }); }

        if (string.IsNullOrWhiteSpace(vm.Inquiry.HotelName) ||
            string.IsNullOrWhiteSpace(vm.Inquiry.ClientName) ||
            string.IsNullOrWhiteSpace(vm.Inquiry.ClientNumber))
        {
            TempData["Error"] = "Hotel Name, Client Name and Client Number are required.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var phone = vm.Inquiry.ClientNumber.Trim();
        // Duplicate check — exclude self
        var dup = await _db.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM inquiries WHERE client_number=@p AND is_deleted=FALSE AND id<>@id",
            new() { ["@p"] = phone, ["@id"] = id });
        if (Convert.ToInt64(dup) > 0)
        {
            TempData["Error"] = $"Another active inquiry already exists for phone {phone}.";
            await ReloadDropdowns(vm);
            return View(vm);
        }

        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        var cityId = await ResolveCityIdAsync(vm.Inquiry.CityText);

        await _db.ExecuteNonQueryAsync(@"
            UPDATE inquiries SET
              hotel_name=@hn, client_name=@cn, client_number=@cnum,
              city_id=@ci, module_id=@mo, status_id=@st,
              payment_received=@pr, followup_date=@fd, note=@no,
              updated_by=@ub, updated_at=NOW()
            WHERE id=@id",
            new()
            {
                ["@hn"]   = vm.Inquiry.HotelName.Trim(),
                ["@cn"]   = vm.Inquiry.ClientName.Trim(),
                ["@cnum"] = phone,
                ["@ci"]   = (object?)cityId ?? DBNull.Value,
                ["@mo"]   = (object?)vm.Inquiry.ModuleId ?? DBNull.Value,
                ["@st"]   = (object?)vm.Inquiry.StatusId ?? DBNull.Value,
                ["@pr"]   = vm.Inquiry.PaymentReceived,
                ["@fd"]   = (object?)vm.Inquiry.FollowupDate ?? DBNull.Value,
                ["@no"]   = (object?)(vm.Inquiry.Note?.Trim()) ?? DBNull.Value,
                ["@ub"]   = (object?)userId ?? DBNull.Value,
                ["@id"]   = id
            });

        TempData["Success"] = "Inquiry updated successfully.";
        return RedirectToAction("Details", new { id });
    }

    // ── SOFT DELETE ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var inq = await GetById(id);
        if (inq == null) return NotFound();
        if (!SessionHelper.IsAdmin(HttpContext.Session)) return RedirectToAction("AccessDenied", "Auth");
        if (inq.IsConverted) { TempData["Error"] = "Converted inquiries cannot be deleted."; return RedirectToAction("Index"); }

        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(
            "UPDATE inquiries SET is_deleted=TRUE, updated_by=@ub, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = id, ["@ub"] = (object?)userId ?? DBNull.Value });
        TempData["Success"] = "Inquiry deleted.";
        return RedirectToAction("Index");
    }

    // ── CONVERT → CLIENT (Admin/Employee only) ─────────────
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> ConvertToClient(int id)
    {
        var inq = await GetById(id);
        if (inq == null) return NotFound();
        if (!CanAccess(inq)) return RedirectToAction("AccessDenied", "Auth");
        if (inq.IsConverted)
        {
            TempData["Error"] = "Already converted.";
            return RedirectToAction("Details", new { id });
        }

        // Pre-fill client form from inquiry
        var vm = new ClientFormViewModel
        {
            Client = new Client
            {
                CompanyName     = inq.HotelName,
                ContactPerson   = inq.ClientName,
                Phone           = inq.ClientNumber,
                CityId          = inq.CityId,
                CityName        = inq.CityName,
                ModuleId        = inq.ModuleId,
                SourceInquiryId = inq.Id,
                Notes           = inq.Note
            },
            Cities  = await LoadActive("cfg_city"),
            Modules = await LoadActive("cfg_module")
        };
        ViewBag.InquiryId = id;
        return View("~/Views/Client/Create.cshtml", vm);
    }

    // ── EXPORT EXCEL (Admin/Employee only) ───────────────────
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Export(
        string? search, int? filterStatus, int? filterModule,
        string? filterPayment, string? filterCity, string? dateFrom, string? dateTo)
    {
        var userId  = HttpContext.Session.GetInt32(SessionHelper.UserId);

        var sql = @"
            SELECT i.id, i.hotel_name, i.client_name, i.client_number,
                   ci.name AS city_name, mo.name AS module_name, st.name AS status_name,
                   i.payment_received, i.followup_date, i.is_converted, i.note,
                   u.full_name AS created_by_name, i.created_at
            FROM inquiries i
            LEFT JOIN cfg_city ci ON ci.id=i.city_id
            LEFT JOIN cfg_module mo ON mo.id=i.module_id
            LEFT JOIN cfg_status st ON st.id=i.status_id
            LEFT JOIN users u ON u.id=i.created_by
            WHERE i.is_deleted=FALSE";

        var p = new Dictionary<string, object?>();
        // Admin and Employee both export all inquiries
        if (!string.IsNullOrWhiteSpace(search))   { sql += " AND (LOWER(i.hotel_name) LIKE @s OR LOWER(i.client_name) LIKE @s OR i.client_number LIKE @s)"; p["@s"] = $"%{search.ToLower()}%"; }
        if (filterStatus.HasValue) { sql += " AND i.status_id=@st"; p["@st"] = filterStatus.Value; }
        if (filterModule.HasValue) { sql += " AND i.module_id=@mo"; p["@mo"] = filterModule.Value; }
        if (!string.IsNullOrWhiteSpace(filterCity)) { sql += " AND LOWER(ci.name) LIKE @cy"; p["@cy"] = $"%{filterCity.ToLower()}%"; }
        if (filterPayment == "yes") sql += " AND i.payment_received=TRUE";
        if (filterPayment == "no")  sql += " AND i.payment_received=FALSE";
        if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var xdf)) { sql += " AND i.created_at >= @df"; p["@df"] = xdf; }
        if (!string.IsNullOrWhiteSpace(dateTo)   && DateTime.TryParse(dateTo,   out var xdt)) { sql += " AND i.created_at <  @dt"; p["@dt"] = xdt.AddDays(1); }
        sql += " ORDER BY i.created_at DESC";

        var rows = await _db.QueryAsync(sql, p);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Inquiries");
        string[] headers = { "#","Hotel Name","Client Name","Phone","City","Module","Status","Payment Received","Follow-up Date","Converted","Note","Created By","Created At" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i+1).Value = headers[i];
        var hRow = ws.Row(1); hRow.Style.Font.Bold = true; hRow.Style.Fill.BackgroundColor = XLColor.FromArgb(79,70,229); hRow.Style.Font.FontColor = XLColor.White;

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value  = row - 1;
            ws.Cell(row, 2).Value  = r["hotel_name"]?.ToString();
            ws.Cell(row, 3).Value  = r["client_name"]?.ToString();
            ws.Cell(row, 4).Value  = r["client_number"]?.ToString();
            ws.Cell(row, 5).Value  = r["city_name"]?.ToString();
            ws.Cell(row, 6).Value  = r["module_name"]?.ToString();
            ws.Cell(row, 7).Value  = r["status_name"]?.ToString();
            ws.Cell(row, 8).Value  = Convert.ToBoolean(r["payment_received"]) ? "Yes" : "No";
            ws.Cell(row, 9).Value  = r["followup_date"] is DBNull or null ? "" : Convert.ToDateTime(r["followup_date"]).ToString("dd MMM yyyy");
            ws.Cell(row, 10).Value = Convert.ToBoolean(r["is_converted"]) ? "Yes" : "No";
            ws.Cell(row, 11).Value = r["note"]?.ToString();
            ws.Cell(row, 12).Value = r["created_by_name"]?.ToString();
            ws.Cell(row, 13).Value = Convert.ToDateTime(r["created_at"]).ToString("dd MMM yyyy HH:mm");
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Inquiries_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ── private helpers ──────────────────────────────────────
    private bool CanAccess(Inquiry inq, string? clientPhone = null)
    {
        if (SessionHelper.IsAdminOrEmployee(HttpContext.Session)) return true;
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        // Client can access inquiries they created OR that match their phone
        return inq.CreatedBy == userId ||
               (!string.IsNullOrEmpty(clientPhone) && inq.ClientNumber == clientPhone);
    }

    private async Task<string?> GetCurrentClientPhoneAsync()
    {
        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        if (role != SessionHelper.RoleClient || !userId.HasValue) return null;
        var client = await GetClientByUserId(userId.Value);
        return client?.Phone;
    }

    private async Task<Models.Client?> GetClientByUserId(int userId)
    {
        var rows = await _db.QueryAsync(
            "SELECT id, contact_person, phone, company_name FROM clients WHERE user_id=@uid AND is_deleted=FALSE LIMIT 1",
            new() { ["@uid"] = userId });
        if (rows.Count == 0) return null;
        return new Models.Client
        {
            Id            = Convert.ToInt32(rows[0]["id"]),
            ContactPerson = rows[0]["contact_person"]?.ToString() ?? "",
            Phone         = rows[0]["phone"]?.ToString() ?? "",
            CompanyName   = rows[0]["company_name"]?.ToString() ?? ""
        };
    }

    private async Task ReloadDropdowns(InquiryFormViewModel vm)
    {
        vm.Cities   = await LoadActive("cfg_city");
        vm.Modules  = await LoadActive("cfg_module");
        vm.Statuses = await LoadActive("cfg_status");
    }

    private async Task<Inquiry?> GetById(int id)
    {
        var rows = await _db.QueryAsync(@"
            SELECT i.id, i.hotel_name, i.client_name, i.client_number,
                   i.city_id, i.module_id, i.status_id, i.created_by, i.updated_by,
                   i.payment_received, i.followup_date, i.note,
                   i.is_converted, i.converted_client_id, i.is_deleted,
                   i.created_at, i.updated_at,
                   ci.name AS city_name, mo.name AS module_name, st.name AS status_name,
                   u.full_name AS created_by_name
            FROM inquiries i
            LEFT JOIN cfg_city   ci ON ci.id=i.city_id
            LEFT JOIN cfg_module mo ON mo.id=i.module_id
            LEFT JOIN cfg_status st ON st.id=i.status_id
            LEFT JOIN users       u ON u.id=i.created_by
            WHERE i.id=@id AND i.is_deleted=FALSE",
            new() { ["@id"] = id });
        return rows.Count == 0 ? null : MapRow(rows[0]);
    }

    private static Inquiry MapRow(Dictionary<string, object?> r) => new()
    {
        Id                = Convert.ToInt32(r["id"]),
        HotelName         = r["hotel_name"]?.ToString() ?? "",
        ClientName        = r["client_name"]?.ToString() ?? "",
        ClientNumber      = r["client_number"]?.ToString() ?? "",
        CityId            = r["city_id"] is DBNull or null ? null : Convert.ToInt32(r["city_id"]),
        CityName          = r.TryGetValue("city_name", out var cn) ? cn?.ToString() : null,
        ModuleId          = r["module_id"] is DBNull or null ? null : Convert.ToInt32(r["module_id"]),
        ModuleName        = r.TryGetValue("module_name", out var mn) ? mn?.ToString() : null,
        StatusId          = r["status_id"] is DBNull or null ? null : Convert.ToInt32(r["status_id"]),
        StatusName        = r.TryGetValue("status_name", out var sn) ? sn?.ToString() : null,
        PaymentReceived   = Convert.ToBoolean(r["payment_received"]),
        FollowupDate      = r["followup_date"] is DBNull or null ? null : Convert.ToDateTime(r["followup_date"]),
        Note              = r["note"]?.ToString(),
        IsConverted       = Convert.ToBoolean(r["is_converted"]),
        ConvertedClientId = r["converted_client_id"] is DBNull or null ? null : Convert.ToInt32(r["converted_client_id"]),
        IsDeleted         = r.TryGetValue("is_deleted", out var del) && del is not (DBNull or null) && Convert.ToBoolean(del),
        CreatedBy         = r["created_by"] is DBNull or null ? null : Convert.ToInt32(r["created_by"]),
        CreatedByName     = r.TryGetValue("created_by_name", out var cbn) ? cbn?.ToString() : null,
        UpdatedBy         = r.TryGetValue("updated_by", out var ub) && ub is not (DBNull or null) ? Convert.ToInt32(ub) : null,
        CreatedAt         = Convert.ToDateTime(r["created_at"]),
        UpdatedAt         = Convert.ToDateTime(r["updated_at"])
    };

    private async Task<int?> ResolveCityIdAsync(string? cityText)
    {
        if (string.IsNullOrWhiteSpace(cityText)) return null;
        var name = cityText.Trim();
        var rows = await _db.QueryAsync(
            "SELECT id FROM cfg_city WHERE LOWER(name)=LOWER(@n) LIMIT 1",
            new() { ["@n"] = name });
        if (rows.Count > 0) return Convert.ToInt32(rows[0]["id"]);
        var newId = await _db.ExecuteScalarAsync(
            "INSERT INTO cfg_city (name, is_active) VALUES (@n, TRUE) RETURNING id",
            new() { ["@n"] = name });
        return Convert.ToInt32(newId);
    }
}



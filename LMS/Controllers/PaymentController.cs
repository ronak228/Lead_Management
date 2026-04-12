using ClosedXML.Excel;
using LeadManagementSystem.Data;
using LeadManagementSystem.Filters;
using LeadManagementSystem.Helpers;
using LeadManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace LeadManagementSystem.Controllers;

// All authenticated users can see payments — Clients are scoped to their own
[SessionAuth]
public class PaymentController : Controller
{
    private readonly DbHelper _db;
    private readonly IWebHostEnvironment _env;

    public PaymentController(DbHelper db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    // ── LIST ─────────────────────────────────────────────────
    public async Task<IActionResult> Index(int? clientId, string? dateFrom, string? dateTo, string? search)
    {
        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);

        // Client: auto-scope to their own client record
        if (role == SessionHelper.RoleClient && userId.HasValue)
        {
            var myClient = await GetClientByUserId(userId.Value);
            if (myClient == null)
            {
                TempData["Error"] = "Your client profile was not found. Contact admin.";
                return View(new PaymentListViewModel());
            }
            clientId = myClient.Id;
        }

        var sql = @"
            SELECT p.id, p.client_id, p.amount, p.payment_mode, p.cheque_no,
                   p.bank_name, p.transaction_id, p.payment_date, p.note, p.proof_file,
                   p.is_deleted, p.created_by, p.updated_by, p.created_at, p.updated_at,
                   c.client_ref, c.company_name, c.contact_person,
                   u.full_name AS created_by_name
            FROM payments p
            JOIN clients c ON c.id=p.client_id
            LEFT JOIN users u ON u.id=p.created_by
            WHERE p.is_deleted=FALSE AND c.is_deleted=FALSE";

        var par = new Dictionary<string, object?>();
        if (clientId.HasValue)    { sql += " AND p.client_id=@cid";  par["@cid"] = clientId.Value; }
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(c.company_name) LIKE @s OR c.client_ref LIKE @s)";
            par["@s"] = $"%{search.Trim().ToLower()}%";
        }
        if (!string.IsNullOrWhiteSpace(dateFrom)) { sql += " AND p.payment_date >= @df"; par["@df"] = dateFrom; }
        if (!string.IsNullOrWhiteSpace(dateTo))   { sql += " AND p.payment_date <= @dt"; par["@dt"] = dateTo; }
        sql += " ORDER BY p.payment_date DESC, p.created_at DESC";

        var rows = await _db.QueryAsync(sql, par);
        decimal total = 0;

        var payments = rows.Select(r =>
        {
            var pay = MapRow(r);
            total += pay.Amount;
            return pay;
        }).ToList();

        Client? filterClient = null;
        if (clientId.HasValue) filterClient = await GetClientById(clientId.Value);

        return View(new PaymentListViewModel
        {
            Payments     = payments,
            TotalAmount  = total,
            ClientId     = clientId,
            ClientRef    = filterClient?.ClientRef,
            CompanyName  = filterClient?.CompanyName,
            DateFrom     = dateFrom,
            DateTo       = dateTo,
            Search       = search
        });
    }

    // ── ADD PAYMENT GET (Admin/Employee only) ──────────────────
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Create(int? clientId)
    {
        var vm = new PaymentFormViewModel
        {
            Payment = new Payment
            {
                ClientId    = clientId ?? 0,
                PaymentDate = DateTime.Today,
                PaymentMode = "Cash"
            }
        };
        if (clientId.HasValue)
            vm.Client = await GetClientById(clientId.Value);
        else
            // Populate dropdown so staff can pick a client when navigating directly
            vm.Clients = await GetActiveClients();
        return View(vm);
    }

    // ── ADD PAYMENT POST (Admin/Employee only) ─────────────────
    [HttpPost]
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Create(PaymentFormViewModel vm, IFormFile? proofFile)
    {
        if (vm.Payment.ClientId <= 0 || vm.Payment.Amount <= 0)
        {
            TempData["Error"] = "Client and a positive Amount are required.";
            if (vm.Payment.ClientId > 0) vm.Client = await GetClientById(vm.Payment.ClientId);
            return View(vm);
        }

        string? savedFile = null;
        if (proofFile != null && proofFile.Length > 0)
        {
            const long maxBytes = 2 * 1024 * 1024;
            if (proofFile.Length > maxBytes)
            {
                TempData["Error"] = "Proof file must be 2 MB or smaller.";
                vm.Client = await GetClientById(vm.Payment.ClientId);
                return View(vm);
            }
            var ext = Path.GetExtension(proofFile.FileName).ToLower();
            if (!new[] { ".jpg", ".jpeg", ".png", ".pdf" }.Contains(ext))
            {
                TempData["Error"] = "Only JPG, PNG, or PDF files are allowed.";
                vm.Client = await GetClientById(vm.Payment.ClientId);
                return View(vm);
            }
            var dir = Path.Combine(_env.WebRootPath, "uploads", "payments");
            Directory.CreateDirectory(dir);
            savedFile = $"{Guid.NewGuid()}{ext}";
            using var fs = System.IO.File.Create(Path.Combine(dir, savedFile));
            await proofFile.CopyToAsync(fs);
        }

        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO payments
              (client_id, amount, payment_mode, cheque_no, bank_name, transaction_id,
               payment_date, note, proof_file, created_by, updated_by)
            VALUES
              (@cid, @am, @pm, @cn, @bn, @ti, @pd, @no, @pf, @cb, @cb)",
            new()
            {
                ["@cid"] = vm.Payment.ClientId,
                ["@am"]  = vm.Payment.Amount,
                ["@pm"]  = vm.Payment.PaymentMode ?? "Cash",
                ["@cn"]  = (object?)(vm.Payment.ChequeNo?.Trim()) ?? DBNull.Value,
                ["@bn"]  = (object?)(vm.Payment.BankName?.Trim()) ?? DBNull.Value,
                ["@ti"]  = (object?)(vm.Payment.TransactionId?.Trim()) ?? DBNull.Value,
                ["@pd"]  = vm.Payment.PaymentDate.Date,
                ["@no"]  = (object?)(vm.Payment.Note?.Trim()) ?? DBNull.Value,
                ["@pf"]  = (object?)savedFile ?? DBNull.Value,
                ["@cb"]  = (object?)userId ?? DBNull.Value
            });

        TempData["Success"] = "Payment recorded successfully.";
        return RedirectToAction("Details", "Client", new { id = vm.Payment.ClientId });
    }

    // ── DETAILS ──────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var p = await GetPaymentById(id);
        if (p == null) return NotFound();
        // Client: can only view their own payments
        var role   = HttpContext.Session.GetString(SessionHelper.UserRole);
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        if (role == SessionHelper.RoleClient && userId.HasValue)
        {
            var myClient = await GetClientByUserId(userId.Value);
            if (myClient == null || myClient.Id != p.ClientId)
                return RedirectToAction("AccessDenied", "Auth");
        }
        return View(p);
    }

    // ── SOFT DELETE ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        if (!SessionHelper.IsAdmin(HttpContext.Session)) return RedirectToAction("AccessDenied", "Auth");
        var payment = await GetPaymentById(id);
        var userId  = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(
            "UPDATE payments SET is_deleted=TRUE, updated_by=@ub, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = id, ["@ub"] = (object?)userId ?? DBNull.Value });
        TempData["Success"] = "Payment removed.";
        return RedirectToAction(payment?.ClientId > 0 ? "Details" : "Index",
            payment?.ClientId > 0 ? "Client" : "Payment",
            payment?.ClientId > 0 ? (object)new { id = payment.ClientId } : null);
    }

    // ── EXPORT EXCEL (Admin/Employee only) ───────────────────
    [SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
    public async Task<IActionResult> Export(int? clientId, string? dateFrom, string? dateTo)
    {
        var sql = @"
            SELECT p.id, p.amount, p.payment_mode, p.cheque_no, p.bank_name,
                   p.transaction_id, p.payment_date, p.note,
                   c.client_ref, c.company_name, c.contact_person,
                   u.full_name AS created_by_name, p.created_at
            FROM payments p
            JOIN clients c ON c.id=p.client_id
            LEFT JOIN users u ON u.id=p.created_by
            WHERE p.is_deleted=FALSE AND c.is_deleted=FALSE";
        var par = new Dictionary<string, object?>();
        if (clientId.HasValue)                       { sql += " AND p.client_id=@cid"; par["@cid"] = clientId.Value; }
        if (!string.IsNullOrWhiteSpace(dateFrom))    { sql += " AND p.payment_date >= @df"; par["@df"] = dateFrom; }
        if (!string.IsNullOrWhiteSpace(dateTo))      { sql += " AND p.payment_date <= @dt"; par["@dt"] = dateTo; }
        sql += " ORDER BY p.payment_date DESC";

        var rows = await _db.QueryAsync(sql, par);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Payments");
        string[] hdr = { "#", "ClientRef", "Company", "Contact", "Amount", "Mode", "Cheque/Txn", "Bank", "Date", "Note", "Recorded By" };
        for (int i = 0; i < hdr.Length; i++) ws.Cell(1, i+1).Value = hdr[i];
        var hr = ws.Row(1); hr.Style.Font.Bold = true; hr.Style.Fill.BackgroundColor = XLColor.FromArgb(79,70,229); hr.Style.Font.FontColor = XLColor.White;
        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value  = row - 1;
            ws.Cell(row, 2).Value  = r["client_ref"]?.ToString();
            ws.Cell(row, 3).Value  = r["company_name"]?.ToString();
            ws.Cell(row, 4).Value  = r["contact_person"]?.ToString();
            ws.Cell(row, 5).Value  = Convert.ToDecimal(r["amount"]);
            ws.Cell(row, 6).Value  = r["payment_mode"]?.ToString();
            var txn = r["transaction_id"]?.ToString() ?? r["cheque_no"]?.ToString() ?? "";
            ws.Cell(row, 7).Value  = txn;
            ws.Cell(row, 8).Value  = r["bank_name"]?.ToString();
            ws.Cell(row, 9).Value  = Convert.ToDateTime(r["payment_date"]).ToString("dd MMM yyyy");
            ws.Cell(row, 10).Value = r["note"]?.ToString();
            ws.Cell(row, 11).Value = r["created_by_name"]?.ToString();
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Payments_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ── private helpers ──────────────────────────────────────
    private async Task<Client?> GetClientByUserId(int userId)
    {
        var rows = await _db.QueryAsync(
            "SELECT id, client_ref, company_name, contact_person, phone, total_amount FROM clients WHERE user_id=@uid AND is_deleted=FALSE LIMIT 1",
            new() { ["@uid"] = userId });
        if (rows.Count == 0) return null;
        var r = rows[0];
        return new Client
        {
            Id            = Convert.ToInt32(r["id"]),
            ClientRef     = r["client_ref"]?.ToString() ?? "",
            CompanyName   = r["company_name"]?.ToString() ?? "",
            ContactPerson = r["contact_person"]?.ToString() ?? "",
            Phone         = r["phone"]?.ToString() ?? "",
            TotalAmount   = Convert.ToDecimal(r["total_amount"])
        };
    }

    private async Task<Payment?> GetPaymentById(int id)
    {
        var rows = await _db.QueryAsync(@"
            SELECT p.id, p.client_id, p.amount, p.payment_mode, p.cheque_no,
                   p.bank_name, p.transaction_id, p.payment_date, p.note, p.proof_file,
                   p.is_deleted, p.created_by, p.updated_by, p.created_at, p.updated_at,
                   c.client_ref, c.company_name, c.contact_person,
                   u.full_name AS created_by_name
            FROM payments p
            JOIN clients c ON c.id=p.client_id
            LEFT JOIN users u ON u.id=p.created_by
            WHERE p.id=@id", new() { ["@id"] = id });
        return rows.Count == 0 ? null : MapRow(rows[0]);
    }

    private async Task<Client?> GetClientById(int id)
    {
        var rows = await _db.QueryAsync(
            "SELECT id, client_ref, company_name, contact_person, phone, total_amount FROM clients WHERE id=@id AND is_deleted=FALSE",
            new() { ["@id"] = id });
        if (rows.Count == 0) return null;
        var r = rows[0];
        return new Client
        {
            Id            = Convert.ToInt32(r["id"]),
            ClientRef     = r["client_ref"]?.ToString() ?? "",
            CompanyName   = r["company_name"]?.ToString() ?? "",
            ContactPerson = r["contact_person"]?.ToString() ?? "",
            Phone         = r["phone"]?.ToString() ?? "",
            TotalAmount   = Convert.ToDecimal(r["total_amount"])
        };
    }

    private async Task<List<Client>> GetActiveClients()
    {
        var rows = await _db.QueryAsync(
            "SELECT id, client_ref, company_name, contact_person, phone, total_amount FROM clients WHERE is_deleted=FALSE AND is_active=TRUE ORDER BY company_name",
            new());
        return rows.Select(r => new Client
        {
            Id            = Convert.ToInt32(r["id"]),
            ClientRef     = r["client_ref"]?.ToString() ?? "",
            CompanyName   = r["company_name"]?.ToString() ?? "",
            ContactPerson = r["contact_person"]?.ToString() ?? "",
            Phone         = r["phone"]?.ToString() ?? "",
            TotalAmount   = Convert.ToDecimal(r["total_amount"])
        }).ToList();
    }

    private static Payment MapRow(Dictionary<string, object?> r) => new()
    {
        Id            = Convert.ToInt32(r["id"]),
        ClientId      = Convert.ToInt32(r["client_id"]),
        ClientRef     = r["client_ref"]?.ToString(),
        CompanyName   = r["company_name"]?.ToString(),
        ContactPerson = r["contact_person"]?.ToString(),
        Amount        = Convert.ToDecimal(r["amount"]),
        PaymentMode   = r["payment_mode"]?.ToString() ?? "Cash",
        ChequeNo      = r["cheque_no"]?.ToString(),
        BankName      = r["bank_name"]?.ToString(),
        TransactionId = r["transaction_id"]?.ToString(),
        PaymentDate   = Convert.ToDateTime(r["payment_date"]),
        Note          = r["note"]?.ToString(),
        ProofFile     = r["proof_file"]?.ToString(),
        IsDeleted     = Convert.ToBoolean(r["is_deleted"]),
        CreatedBy     = r["created_by"] is DBNull or null ? null : Convert.ToInt32(r["created_by"]),
        CreatedByName = r["created_by_name"]?.ToString(),
        UpdatedBy     = r["updated_by"] is DBNull or null ? null : Convert.ToInt32(r["updated_by"]),
        CreatedAt     = Convert.ToDateTime(r["created_at"]),
        UpdatedAt     = Convert.ToDateTime(r["updated_at"])
    };
}

using ClosedXML.Excel;
using LeadManagementSystem.Data;
using LeadManagementSystem.Filters;
using LeadManagementSystem.Helpers;
using LeadManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace LeadManagementSystem.Controllers;

// Expenses are internal — only staff roles
[SessionAuth(SessionHelper.RoleAdmin, SessionHelper.RoleEmployee)]
public class ExpenseController : Controller
{
    private readonly DbHelper _db;
    private readonly IWebHostEnvironment _env;

    public ExpenseController(DbHelper db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    // ── LIST ─────────────────────────────────────────────────
    public async Task<IActionResult> Index(int? categoryId, string? dateFrom, string? dateTo, string? search)
    {
        var sql = @"
            SELECT e.id, e.expense_date, e.category_id, e.from_name, e.to_name,
                   e.amount, e.payment_mode, e.cheque_no, e.bank_name, e.transaction_id,
                   e.note, e.attachment, e.is_deleted, e.created_by, e.updated_by,
                   e.created_at, e.updated_at,
                   cat.name AS category_name, u.full_name AS created_by_name
            FROM expenses e
            LEFT JOIN cfg_category cat ON cat.id=e.category_id
            LEFT JOIN users u ON u.id=e.created_by
            WHERE e.is_deleted=FALSE";

        var par = new Dictionary<string, object?>();
        if (categoryId.HasValue)              { sql += " AND e.category_id=@cat"; par["@cat"] = categoryId.Value; }
        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(e.from_name) LIKE @s OR LOWER(e.to_name) LIKE @s)";
            par["@s"] = $"%{search.Trim().ToLower()}%";
        }
        if (!string.IsNullOrWhiteSpace(dateFrom)) { sql += " AND e.expense_date >= @df"; par["@df"] = dateFrom; }
        if (!string.IsNullOrWhiteSpace(dateTo))   { sql += " AND e.expense_date <= @dt"; par["@dt"] = dateTo; }
        sql += " ORDER BY e.expense_date DESC, e.created_at DESC";

        var rows       = await _db.QueryAsync(sql, par);
        var categories = await LoadCategories();

        decimal total = 0;
        var expenses = rows.Select(r => { var e = MapRow(r); total += e.Amount; return e; }).ToList();

        return View(new ExpenseListViewModel
        {
            Expenses    = expenses,
            Categories  = categories,
            TotalAmount = total,
            CategoryId  = categoryId,
            DateFrom    = dateFrom,
            DateTo      = dateTo,
            Search      = search
        });
    }

    // ── CREATE GET ───────────────────────────────────────────
    public async Task<IActionResult> Create()
        => View(new ExpenseFormViewModel
        {
            Expense    = new Expense { ExpenseDate = DateTime.Today, PaymentMode = "Cash" },
            Categories = await LoadCategories()
        });

    // ── CREATE POST ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create(ExpenseFormViewModel vm, IFormFile? attachment)
    {
        if (vm.Expense.Amount <= 0)
        {
            TempData["Error"] = "Amount must be greater than zero.";
            vm.Categories = await LoadCategories();
            return View(vm);
        }

        string? savedFile = null;
        if (attachment != null && attachment.Length > 0)
        {
            const long maxBytes = 2 * 1024 * 1024;
            if (attachment.Length > maxBytes)
            {
                TempData["Error"] = "Attachment must be 2 MB or smaller.";
                vm.Categories = await LoadCategories();
                return View(vm);
            }
            var ext = Path.GetExtension(attachment.FileName).ToLower();
            if (!new[] { ".jpg", ".jpeg", ".png", ".pdf" }.Contains(ext))
            {
                TempData["Error"] = "Only JPG, PNG, or PDF files are allowed.";
                vm.Categories = await LoadCategories();
                return View(vm);
            }
            var dir = Path.Combine(_env.WebRootPath, "uploads", "expenses");
            Directory.CreateDirectory(dir);
            savedFile = $"{Guid.NewGuid()}{ext}";
            using var fs = System.IO.File.Create(Path.Combine(dir, savedFile));
            await attachment.CopyToAsync(fs);
        }

        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO expenses
              (expense_date, category_id, from_name, to_name, amount, payment_mode,
               cheque_no, bank_name, transaction_id, note, attachment, created_by, updated_by)
            VALUES
              (@ed, @cat, @fn, @tn, @am, @pm, @cn, @bn, @ti, @no, @att, @cb, @cb)",
            new()
            {
                ["@ed"]  = vm.Expense.ExpenseDate.Date,
                ["@cat"] = (object?)vm.Expense.CategoryId ?? DBNull.Value,
                ["@fn"]  = (object?)(vm.Expense.FromName?.Trim()) ?? DBNull.Value,
                ["@tn"]  = (object?)(vm.Expense.ToName?.Trim()) ?? DBNull.Value,
                ["@am"]  = vm.Expense.Amount,
                ["@pm"]  = vm.Expense.PaymentMode ?? "Cash",
                ["@cn"]  = (object?)(vm.Expense.ChequeNo?.Trim()) ?? DBNull.Value,
                ["@bn"]  = (object?)(vm.Expense.BankName?.Trim()) ?? DBNull.Value,
                ["@ti"]  = (object?)(vm.Expense.TransactionId?.Trim()) ?? DBNull.Value,
                ["@no"]  = (object?)(vm.Expense.Note?.Trim()) ?? DBNull.Value,
                ["@att"] = (object?)savedFile ?? DBNull.Value,
                ["@cb"]  = (object?)userId ?? DBNull.Value
            });

        TempData["Success"] = "Expense recorded successfully.";
        return RedirectToAction("Index");
    }

    // ── DETAILS ──────────────────────────────────────────────
    public async Task<IActionResult> Details(int id)
    {
        var e = await GetById(id);
        if (e == null) return NotFound();
        return View(e);
    }

    // ── EDIT GET ─────────────────────────────────────────────
    public async Task<IActionResult> Edit(int id)
    {
        var e = await GetById(id);
        if (e == null) return NotFound();
        return View(new ExpenseFormViewModel { Expense = e, Categories = await LoadCategories() });
    }

    // ── EDIT POST ────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Edit(int id, ExpenseFormViewModel vm, IFormFile? attachment)
    {
        if (vm.Expense.Amount <= 0)
        {
            TempData["Error"] = "Amount must be greater than zero.";
            vm.Categories = await LoadCategories();
            return View(vm);
        }

        var existing = await GetById(id);
        string? savedFile = existing?.Attachment;

        if (attachment != null && attachment.Length > 0)
        {
            const long maxBytes = 2 * 1024 * 1024;
            var ext = Path.GetExtension(attachment.FileName).ToLower();
            if (attachment.Length > maxBytes || !new[] { ".jpg", ".jpeg", ".png", ".pdf" }.Contains(ext))
            {
                TempData["Error"] = "Attachment must be JPG/PNG/PDF and max 2 MB.";
                vm.Categories = await LoadCategories();
                return View(vm);
            }
            var dir = Path.Combine(_env.WebRootPath, "uploads", "expenses");
            Directory.CreateDirectory(dir);
            savedFile = $"{Guid.NewGuid()}{ext}";
            using var fs = System.IO.File.Create(Path.Combine(dir, savedFile));
            await attachment.CopyToAsync(fs);
        }

        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(@"
            UPDATE expenses SET
              expense_date=@ed, category_id=@cat, from_name=@fn, to_name=@tn, amount=@am,
              payment_mode=@pm, cheque_no=@cn, bank_name=@bn, transaction_id=@ti,
              note=@no, attachment=@att, updated_by=@ub, updated_at=NOW()
            WHERE id=@id",
            new()
            {
                ["@ed"]  = vm.Expense.ExpenseDate.Date,
                ["@cat"] = (object?)vm.Expense.CategoryId ?? DBNull.Value,
                ["@fn"]  = (object?)(vm.Expense.FromName?.Trim()) ?? DBNull.Value,
                ["@tn"]  = (object?)(vm.Expense.ToName?.Trim()) ?? DBNull.Value,
                ["@am"]  = vm.Expense.Amount,
                ["@pm"]  = vm.Expense.PaymentMode ?? "Cash",
                ["@cn"]  = (object?)(vm.Expense.ChequeNo?.Trim()) ?? DBNull.Value,
                ["@bn"]  = (object?)(vm.Expense.BankName?.Trim()) ?? DBNull.Value,
                ["@ti"]  = (object?)(vm.Expense.TransactionId?.Trim()) ?? DBNull.Value,
                ["@no"]  = (object?)(vm.Expense.Note?.Trim()) ?? DBNull.Value,
                ["@att"] = (object?)savedFile ?? DBNull.Value,
                ["@ub"]  = (object?)userId ?? DBNull.Value,
                ["@id"]  = id
            });

        TempData["Success"] = "Expense updated successfully.";
        return RedirectToAction("Details", new { id });
    }

    // ── SOFT DELETE ──────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        if (!SessionHelper.IsAdmin(HttpContext.Session)) return RedirectToAction("AccessDenied", "Auth");
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);
        await _db.ExecuteNonQueryAsync(
            "UPDATE expenses SET is_deleted=TRUE, updated_by=@ub, updated_at=NOW() WHERE id=@id",
            new() { ["@id"] = id, ["@ub"] = (object?)userId ?? DBNull.Value });
        TempData["Success"] = "Expense removed.";
        return RedirectToAction("Index");
    }

    // ── EXPORT EXCEL ─────────────────────────────────────────
    public async Task<IActionResult> Export(int? categoryId, string? dateFrom, string? dateTo)
    {
        var sql = @"
            SELECT e.expense_date, cat.name AS category_name, e.from_name, e.to_name,
                   e.amount, e.payment_mode, e.cheque_no, e.bank_name, e.transaction_id,
                   e.note, u.full_name AS created_by_name
            FROM expenses e
            LEFT JOIN cfg_category cat ON cat.id=e.category_id
            LEFT JOIN users u ON u.id=e.created_by
            WHERE e.is_deleted=FALSE";
        var par = new Dictionary<string, object?>();
        if (categoryId.HasValue)                 { sql += " AND e.category_id=@cat"; par["@cat"] = categoryId.Value; }
        if (!string.IsNullOrWhiteSpace(dateFrom)) { sql += " AND e.expense_date >= @df"; par["@df"] = dateFrom; }
        if (!string.IsNullOrWhiteSpace(dateTo))   { sql += " AND e.expense_date <= @dt"; par["@dt"] = dateTo; }
        sql += " ORDER BY e.expense_date DESC";

        var rows = await _db.QueryAsync(sql, par);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Expenses");
        string[] hdr = { "#", "Date", "Category", "From", "To", "Amount", "Mode", "Cheque/Txn", "Bank", "Note", "Recorded By" };
        for (int i = 0; i < hdr.Length; i++) ws.Cell(1, i+1).Value = hdr[i];
        var hr = ws.Row(1); hr.Style.Font.Bold = true; hr.Style.Fill.BackgroundColor = XLColor.FromArgb(220,53,69); hr.Style.Font.FontColor = XLColor.White;
        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value  = row - 1;
            ws.Cell(row, 2).Value  = Convert.ToDateTime(r["expense_date"]).ToString("dd MMM yyyy");
            ws.Cell(row, 3).Value  = r["category_name"]?.ToString();
            ws.Cell(row, 4).Value  = r["from_name"]?.ToString();
            ws.Cell(row, 5).Value  = r["to_name"]?.ToString();
            ws.Cell(row, 6).Value  = Convert.ToDecimal(r["amount"]);
            ws.Cell(row, 7).Value  = r["payment_mode"]?.ToString();
            var txn = r["transaction_id"]?.ToString() ?? r["cheque_no"]?.ToString() ?? "";
            ws.Cell(row, 8).Value  = txn;
            ws.Cell(row, 9).Value  = r["bank_name"]?.ToString();
            ws.Cell(row, 10).Value = r["note"]?.ToString();
            ws.Cell(row, 11).Value = r["created_by_name"]?.ToString();
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Expenses_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ── private helpers ──────────────────────────────────────
    private async Task<List<ConfigItem>> LoadCategories()
    {
        var rows = await _db.QueryAsync("SELECT id, name FROM cfg_category WHERE is_active=TRUE ORDER BY name", new());
        return rows.Select(r => new ConfigItem { Id = Convert.ToInt32(r["id"]), Name = r["name"]?.ToString() ?? "" }).ToList();
    }

    private async Task<Expense?> GetById(int id)
    {
        var rows = await _db.QueryAsync(@"
            SELECT e.id, e.expense_date, e.category_id, e.from_name, e.to_name,
                   e.amount, e.payment_mode, e.cheque_no, e.bank_name, e.transaction_id,
                   e.note, e.attachment, e.is_deleted, e.created_by, e.updated_by,
                   e.created_at, e.updated_at,
                   cat.name AS category_name, u.full_name AS created_by_name
            FROM expenses e
            LEFT JOIN cfg_category cat ON cat.id=e.category_id
            LEFT JOIN users u ON u.id=e.created_by
            WHERE e.id=@id", new() { ["@id"] = id });
        return rows.Count == 0 ? null : MapRow(rows[0]);
    }

    private static Expense MapRow(Dictionary<string, object?> r) => new()
    {
        Id            = Convert.ToInt32(r["id"]),
        ExpenseDate   = Convert.ToDateTime(r["expense_date"]),
        CategoryId    = r["category_id"] is DBNull or null ? null : Convert.ToInt32(r["category_id"]),
        CategoryName  = r["category_name"]?.ToString(),
        FromName      = r["from_name"]?.ToString(),
        ToName        = r["to_name"]?.ToString(),
        Amount        = Convert.ToDecimal(r["amount"]),
        PaymentMode   = r["payment_mode"]?.ToString() ?? "Cash",
        ChequeNo      = r["cheque_no"]?.ToString(),
        BankName      = r["bank_name"]?.ToString(),
        TransactionId = r["transaction_id"]?.ToString(),
        Note          = r["note"]?.ToString(),
        Attachment    = r["attachment"]?.ToString(),
        IsDeleted     = Convert.ToBoolean(r["is_deleted"]),
        CreatedBy     = r["created_by"] is DBNull or null ? null : Convert.ToInt32(r["created_by"]),
        CreatedByName = r["created_by_name"]?.ToString(),
        UpdatedBy     = r["updated_by"] is DBNull or null ? null : Convert.ToInt32(r["updated_by"]),
        CreatedAt     = Convert.ToDateTime(r["created_at"]),
        UpdatedAt     = Convert.ToDateTime(r["updated_at"])
    };
}

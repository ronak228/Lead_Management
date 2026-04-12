using LeadManagementSystem.Data;
using LeadManagementSystem.Filters;
using LeadManagementSystem.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace LeadManagementSystem.Controllers;

[SessionAuth]
public class DashboardController : Controller
{
    private readonly DbHelper _db;
    public DashboardController(DbHelper db) => _db = db;

    public async Task<IActionResult> Index()
    {
        async Task<long> SafeCount(string sql, Dictionary<string, object?>? p = null)
        {
            try { return Convert.ToInt64(await _db.ExecuteScalarAsync(sql, p ?? new())); }
            catch { return 0; }
        }
        async Task<decimal> SafeSum(string sql, Dictionary<string, object?>? p = null)
        {
            try
            {
                var v = await _db.ExecuteScalarAsync(sql, p ?? new());
                return v == null || v is DBNull ? 0m : Convert.ToDecimal(v);
            }
            catch { return 0m; }
        }

        var role   = HttpContext.Session.GetString(SessionHelper.UserRole) ?? "User";
        var userId = HttpContext.Session.GetInt32(SessionHelper.UserId);

        ViewBag.UserName = HttpContext.Session.GetString(SessionHelper.UserName);
        ViewBag.UserRole = role;

        if (role == "Client" && userId.HasValue)
        {
            var clientRows = await _db.QueryAsync(
                "SELECT id, client_ref, company_name, total_amount FROM clients WHERE user_id=@uid AND is_deleted=FALSE LIMIT 1",
                new() { ["@uid"] = userId.Value });

            if (clientRows.Count > 0)
            {
                var cid   = Convert.ToInt32(clientRows[0]["id"]);
                var total = Convert.ToDecimal(clientRows[0]["total_amount"]);
                var paid  = await SafeSum("SELECT COALESCE(SUM(amount),0) FROM payments WHERE client_id=@cid AND is_deleted=FALSE", new() { ["@cid"] = cid });
                ViewBag.ClientRef   = clientRows[0]["client_ref"]?.ToString();
                ViewBag.CompanyName = clientRows[0]["company_name"]?.ToString();
                ViewBag.TotalAmount = total;
                ViewBag.TotalPaid   = paid;
                ViewBag.Remaining   = total - paid;
                try
                {
                    ViewBag.RecentPayments = await _db.QueryAsync(@"
                        SELECT amount, payment_mode, payment_date, note
                        FROM payments WHERE client_id=@cid AND is_deleted=FALSE
                        ORDER BY payment_date DESC LIMIT 5", new() { ["@cid"] = cid });
                }
                catch { ViewBag.RecentPayments = null; }
            }
            return View();
        }

        // Admin / Employee operational stats
        ViewBag.TotalInquiries   = await SafeCount("SELECT COUNT(*) FROM inquiries WHERE is_deleted=FALSE");
        ViewBag.TotalClients     = await SafeCount("SELECT COUNT(*) FROM clients WHERE is_deleted=FALSE");
        ViewBag.TotalUsers       = await SafeCount("SELECT COUNT(*) FROM users WHERE is_active=TRUE");
        ViewBag.ConvertedCount   = await SafeCount("SELECT COUNT(*) FROM inquiries WHERE is_converted=TRUE AND is_deleted=FALSE");

        // Financial totals: visible to Admin only
        if (role == SessionHelper.RoleAdmin)
        {
            ViewBag.TotalIncome    = await SafeSum("SELECT COALESCE(SUM(amount),0) FROM payments WHERE is_deleted=FALSE");
            ViewBag.TotalExpenses  = await SafeSum("SELECT COALESCE(SUM(amount),0) FROM expenses WHERE is_deleted=FALSE");
        }

        // Pending payment clients (total_amount > total_paid)
        ViewBag.PendingClients   = await SafeCount(@"
            SELECT COUNT(*) FROM clients c
            WHERE c.is_deleted=FALSE AND c.total_amount > 0
            AND (SELECT COALESCE(SUM(p.amount),0) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE) < c.total_amount");

        // Conversion rate
        var totalInq = await SafeCount("SELECT COUNT(*) FROM inquiries WHERE is_deleted=FALSE");
        var converted = await SafeCount("SELECT COUNT(*) FROM inquiries WHERE is_converted=TRUE AND is_deleted=FALSE");
        ViewBag.ConversionRate = totalInq > 0 ? Math.Round((double)converted / totalInq * 100, 1) : 0.0;

        // Today's follow-ups
        ViewBag.FollowupToday = await SafeCount(
            "SELECT COUNT(*) FROM inquiries WHERE followup_date=CURRENT_DATE AND is_deleted=FALSE AND is_converted=FALSE");

        // Recent inquiries
        try
        {
            ViewBag.RecentInquiries = await _db.QueryAsync(@"
                SELECT i.id, i.hotel_name, i.client_name, i.client_number,
                       st.name AS status_name, i.followup_date, i.is_converted, i.created_at
                FROM inquiries i
                LEFT JOIN cfg_status st ON st.id=i.status_id
                WHERE i.is_deleted=FALSE
                ORDER BY i.created_at DESC LIMIT 6", new());
        }
        catch { ViewBag.RecentInquiries = null; }

        // Recent payments
        try
        {
            ViewBag.RecentPayments = await _db.QueryAsync(@"
                SELECT p.amount, p.payment_mode, p.payment_date,
                       c.client_ref, c.company_name
                FROM payments p
                JOIN clients c ON c.id=p.client_id
                WHERE p.is_deleted=FALSE
                ORDER BY p.created_at DESC LIMIT 5", new());
        }
        catch { ViewBag.RecentPayments = null; }

        return View();
    }
}

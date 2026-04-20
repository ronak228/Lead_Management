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

        // Check if client account is deactivated
        if (role == "Client" && userId.HasValue)
        {
            var clientRow = await _db.QueryAsync(
                "SELECT is_active, client_ref, company_name, total_amount FROM users WHERE id=@uid AND is_deleted=FALSE",
                new() { ["@uid"] = userId.Value });
            
            if (clientRow.Count > 0)
            {
                if (!(bool)(clientRow[0]["is_active"] ?? true))
                {
                    TempData["ClientDeactivated"] = true;
                }
                
                // Client accessing dashboard
                var cid   = userId.Value;
                var paid  = await SafeSum("SELECT COALESCE(SUM(amount),0) FROM payments WHERE client_id=@cid AND is_deleted=FALSE", new() { ["@cid"] = cid });
                // Get total_amount from user record (but validate it's not negative)
                var total = Convert.ToDecimal(clientRow[0]["total_amount"] ?? 0m);
                if (total < 0) total = 0;
                
                ViewBag.ClientRef   = clientRow[0]["client_ref"]?.ToString();
                ViewBag.CompanyName = clientRow[0]["company_name"]?.ToString();
                ViewBag.TotalAmount = total;
                ViewBag.TotalPaid   = paid;
                ViewBag.Remaining   = Math.Max(0, total - paid);  // Never show negative remaining
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
        ViewBag.TotalClients     = await SafeCount("SELECT COUNT(*) FROM users WHERE is_deleted=FALSE AND company_name IS NOT NULL AND role='Client'");
        ViewBag.TotalUsers       = await SafeCount("SELECT COUNT(*) FROM users WHERE is_active=TRUE");
        ViewBag.NewInquiries     = await SafeCount("SELECT COUNT(*) FROM inquiries WHERE is_deleted=FALSE AND status_id=(SELECT id FROM cfg_status WHERE LOWER(name)='received' LIMIT 1)");

        // Financial totals: visible to Admin only
        if (role == SessionHelper.RoleAdmin)
        {
            ViewBag.TotalIncome    = await SafeSum("SELECT COALESCE(SUM(amount),0) FROM payments WHERE is_deleted=FALSE");
            ViewBag.TotalExpenses  = await SafeSum("SELECT COALESCE(SUM(amount),0) FROM expenses WHERE is_deleted=FALSE");
        }

        // Pending payment clients (total_amount > total_paid)
        ViewBag.PendingClients   = await SafeCount(@"
            SELECT COUNT(*) FROM users c
            WHERE c.is_deleted=FALSE AND c.total_amount > 0 AND c.role='Client'
            AND (SELECT COALESCE(SUM(p.amount),0) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE) < c.total_amount");

        // Today's follow-ups
        ViewBag.FollowupToday = await SafeCount(
            "SELECT COUNT(*) FROM inquiries WHERE followup_date=CURRENT_DATE AND is_deleted=FALSE");

        // Linked client inquiries count (inquiries from registered clients)
        ViewBag.ClientInquiries = await SafeCount("SELECT COUNT(*) FROM inquiries WHERE client_id IS NOT NULL AND is_deleted=FALSE");

        // Recent inquiries
        try
        {
            ViewBag.RecentInquiries = await _db.QueryAsync(@"
                SELECT i.id, i.hotel_name, i.client_name, i.client_number,
                       st.name AS status_name, i.followup_date, i.created_at,
                       c.client_ref, c.company_name AS client_company
                FROM inquiries i
                LEFT JOIN cfg_status st ON st.id=i.status_id
                LEFT JOIN users c ON c.id=i.converted_client_id
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
                JOIN users c ON c.id=p.client_id
                WHERE p.is_deleted=FALSE
                ORDER BY p.created_at DESC LIMIT 5", new());
        }
        catch { ViewBag.RecentPayments = null; }

        return View();
    }
}

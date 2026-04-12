using LeadManagementSystem.Data;
using LeadManagementSystem.Helpers;
using LeadManagementSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace LeadManagementSystem.Controllers;

public class AuthController : Controller
{
    private readonly DbHelper _db;

    public AuthController(DbHelper db) => _db = db;

    // ─── LOGIN ───────────────────────────────────────────
    [HttpGet]
    public IActionResult Login()
    {
        if (SessionHelper.IsLoggedIn(HttpContext.Session))
            return RedirectToAction("Index", "Dashboard");
        return View(new LoginViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
        {
            model.ErrorMessage = "Email and password are required.";
            return View(model);
        }

        var rows = await _db.QueryAsync(
            "SELECT id, full_name, email, password, role, is_active, setup_token FROM users WHERE email = @email",
            new() { ["@email"] = model.Email.Trim().ToLower() });

        if (rows.Count == 0)
        {
            model.ErrorMessage = "Invalid email or password.";
            return View(model);
        }

        var row = rows[0];

        if (!(bool)row["is_active"]!)
        {
            var hasSetupToken = row.TryGetValue("setup_token", out var st) && st != null && st != DBNull.Value && !string.IsNullOrEmpty(st.ToString());
            model.ErrorMessage = hasSetupToken
                ? "Your account is not yet activated. Please use the setup link that was shared with you."
                : "Your account has been deactivated. Please contact support.";
            return View(model);
        }

        var storedHash = row["password"]?.ToString() ?? "";
        if (!PasswordHelper.Verify(model.Password, storedHash))
        {
            model.ErrorMessage = "Invalid email or password.";
            return View(model);
        }

        // Silently upgrade legacy SHA-256 passwords to BCrypt on first login
        if (PasswordHelper.IsLegacyHash(storedHash))
        {
            var newHash = PasswordHelper.Hash(model.Password);
            await _db.ExecuteNonQueryAsync(
                "UPDATE users SET password=@pwd, updated_at=NOW() WHERE id=@id",
                new() { ["@pwd"] = newHash, ["@id"] = Convert.ToInt32(row["id"]) });
        }

        var user = new User
        {
            Id       = Convert.ToInt32(row["id"]),
            FullName = row["full_name"]?.ToString() ?? "",
            Email    = row["email"]?.ToString() ?? "",
            Role     = row["role"]?.ToString() ?? "User"
        };

        SessionHelper.SetUser(HttpContext.Session, user);
        return RedirectToAction("Index", "Dashboard");
    }

    // ─── REGISTER ────────────────────────────────────────
    [HttpGet]
    public IActionResult Register()
    {
        if (SessionHelper.IsLoggedIn(HttpContext.Session))
            return RedirectToAction("Index", "Dashboard");
        return View(new RegisterViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.FullName) ||
            string.IsNullOrWhiteSpace(model.Email) ||
            string.IsNullOrWhiteSpace(model.Password))
        {
            model.ErrorMessage = "All fields are required.";
            return View(model);
        }

        // Email format validation
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                model.Email.Trim(),
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            model.ErrorMessage = "Please enter a valid email address.";
            return View(model);
        }

        if (model.Password != model.ConfirmPassword)
        {
            model.ErrorMessage = "Passwords do not match.";
            return View(model);
        }

        if (model.Password.Length < 8)
        {
            model.ErrorMessage = "Password must be at least 8 characters.";
            return View(model);
        }

        // Validate client-only fields when role is Client
        if (model.Role == SessionHelper.RoleClient &&
            (string.IsNullOrWhiteSpace(model.CompanyName) || string.IsNullOrWhiteSpace(model.Phone)))
        {
            model.ErrorMessage = "Company Name and Phone are required for Client accounts.";
            return View(model);
        }

        // Restrict to allowed self-registration roles
        if (model.Role != SessionHelper.RoleEmployee && model.Role != SessionHelper.RoleClient)
            model.Role = SessionHelper.RoleClient;

        // Check duplicate email
        var existing = await _db.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM users WHERE email = @email",
            new() { ["@email"] = model.Email.Trim().ToLower() });

        if (Convert.ToInt32(existing) > 0)
        {
            model.ErrorMessage = "An account with this email already exists.";
            return View(model);
        }

        var hash = PasswordHelper.Hash(model.Password);

        // Insert user and get new ID
        var newUserId = Convert.ToInt32(await _db.ExecuteScalarAsync(
            "INSERT INTO users (full_name, email, password, role, is_active) VALUES (@name, @email, @pwd, @role, TRUE) RETURNING id",
            new()
            {
                ["@name"]  = model.FullName.Trim(),
                ["@email"] = model.Email.Trim().ToLower(),
                ["@pwd"]   = hash,
                ["@role"]  = model.Role
            }));

        // Auto-create the linked client record only for Client role
        if (model.Role == SessionHelper.RoleClient)
        {
            await _db.ExecuteNonQueryAsync(
                @"INSERT INTO clients (client_ref, company_name, contact_person, phone, email, user_id, created_by)
                  VALUES (CONCAT('LMS-', LPAD(nextval('client_ref_seq')::text, 4, '0')), @cn, @cp, @ph, @em, @uid, @uid)",
                new()
                {
                    ["@cn"]  = model.CompanyName!.Trim(),
                    ["@cp"]  = model.FullName.Trim(),
                    ["@ph"]  = model.Phone!.Trim(),
                    ["@em"]  = (object?)model.Email.Trim().ToLower(),
                    ["@uid"] = newUserId
                });
        }

        TempData["RegisterSuccess"] = "Account created! Please log in.";
        return RedirectToAction("Login");
    }

    // ─── LOGOUT ──────────────────────────────────────────
    public IActionResult Logout()
    {
        SessionHelper.Clear(HttpContext.Session);
        return RedirectToAction("Login");
    }
    // ─── SETUP PASSWORD ─────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> SetupPassword(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData["Error"] = "Invalid or missing setup link.";
            return RedirectToAction("Login");
        }

        var rows = await _db.QueryAsync(
            "SELECT id, full_name, email FROM users WHERE setup_token=@t AND setup_token_expires > NOW() AND is_active=FALSE",
            new() { ["@t"] = token });

        if (rows.Count == 0)
        {
            TempData["Error"] = "This setup link has expired or has already been used. Please contact your account manager.";
            return RedirectToAction("Login");
        }

        ViewBag.Token = token;
        ViewBag.Email = rows[0]["email"]?.ToString();
        ViewBag.FullName = rows[0]["full_name"]?.ToString();
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SetupPassword(string token, string password, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData["Error"] = "Invalid setup link.";
            return RedirectToAction("Login");
        }

        var rows = await _db.QueryAsync(
            "SELECT id, email FROM users WHERE setup_token=@t AND setup_token_expires > NOW() AND is_active=FALSE",
            new() { ["@t"] = token });

        if (rows.Count == 0)
        {
            ViewBag.Token = token;
            ViewBag.ErrorMessage = "This setup link has expired or has already been used.";
            return View();
        }

        if (string.IsNullOrWhiteSpace(password) || password != confirmPassword)
        {
            ViewBag.Token = token;
            ViewBag.Email = rows[0]["email"]?.ToString();
            ViewBag.ErrorMessage = "Passwords do not match.";
            return View();
        }

        if (password.Length < 8)
        {
            ViewBag.Token = token;
            ViewBag.Email = rows[0]["email"]?.ToString();
            ViewBag.ErrorMessage = "Password must be at least 8 characters.";
            return View();
        }

        var hashed = PasswordHelper.Hash(password);
        await _db.ExecuteNonQueryAsync(
            "UPDATE users SET password=@pw, is_active=TRUE, setup_token=NULL, setup_token_expires=NULL, updated_at=NOW() WHERE id=@id",
            new() { ["@pw"] = hashed, ["@id"] = Convert.ToInt32(rows[0]["id"]) });

        TempData["RegisterSuccess"] = "Password set! You can now log in.";
        return RedirectToAction("Login");
    }
    // ─── ACCESS DENIED ───────────────────────────────────
    public IActionResult AccessDenied()
    {
        return View();
    }
}

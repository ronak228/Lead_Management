using LeadManagementSystem.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LeadManagementSystem.Filters;

/// <summary>
/// Requires the user to be logged in. Optionally restricts to one or more roles.
/// Usage: [SessionAuth] — any authenticated user
///        [SessionAuth("Admin")] — Admin only
///        [SessionAuth("Admin", "Employee")] — Admin or Employee
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SessionAuthAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _allowedRoles;

    public SessionAuthAttribute(params string[] allowedRoles)
    {
        _allowedRoles = allowedRoles;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var session = context.HttpContext.Session;

        if (!SessionHelper.IsLoggedIn(session))
        {
            context.Result = new RedirectToActionResult("Login", "Auth", null);
            return;
        }

        if (_allowedRoles.Length > 0)
        {
            var role = session.GetString(SessionHelper.UserRole) ?? string.Empty;
            // Treat legacy 'User' role as equivalent to 'Employee' for backward compatibility
            var effectiveRole = role == "User" ? SessionHelper.RoleEmployee : role;
            if (!_allowedRoles.Contains(effectiveRole))
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Auth", null);
            }
        }
    }
}

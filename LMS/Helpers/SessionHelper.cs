namespace LeadManagementSystem.Helpers;

public static class SessionHelper
{
    public const string UserId    = "UserId";
    public const string UserName  = "UserName";
    public const string UserEmail = "UserEmail";
    public const string UserRole  = "UserRole";

    // Valid role constants — single source of truth
    public const string RoleAdmin    = "Admin";
    public const string RoleEmployee = "Employee";
    public const string RoleClient   = "Client";

    public static bool IsLoggedIn(ISession session)
        => session.GetInt32(UserId).HasValue;

    public static bool IsAdmin(ISession session)
        => session.GetString(UserRole) == RoleAdmin;

    public static bool IsEmployee(ISession session)
        => session.GetString(UserRole) == RoleEmployee;

    public static bool IsClient(ISession session)
        => session.GetString(UserRole) == RoleClient;

    /// <summary>Returns true for Admin OR Employee (staff roles). Also accepts legacy 'User' role.</summary>
    public static bool IsAdminOrEmployee(ISession session)
    {
        var role = session.GetString(UserRole);
        // 'User' is the legacy name for what is now called 'Employee'
        return role == RoleAdmin || role == RoleEmployee || role == "User";
    }

    public static void SetUser(ISession session, Models.User user)
    {
        session.SetInt32(UserId,    user.Id);
        session.SetString(UserName, user.FullName);
        session.SetString(UserEmail,user.Email);
        session.SetString(UserRole, user.Role);
    }

    public static void Clear(ISession session) => session.Clear();
}

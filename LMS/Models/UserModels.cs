namespace LeadManagementSystem.Models;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class LoginViewModel
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

public class RegisterViewModel
{
    public string  FullName        { get; set; } = "";
    public string  Email           { get; set; } = "";
    public string  Password        { get; set; } = "";
    public string  ConfirmPassword { get; set; } = "";
    public string  Role            { get; set; } = "User";  // User | Client
    // Extra fields for Client role
    public string? CompanyName     { get; set; }
    public string? Phone           { get; set; }
    public string? ErrorMessage    { get; set; }
}

public class ForgotPasswordViewModel
{
    public string Email { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

public class ResetPasswordViewModel
{
    public int UserId { get; set; }
    public string Token { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public string? ErrorMessage { get; set; }
}

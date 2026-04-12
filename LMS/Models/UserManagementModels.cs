namespace LeadManagementSystem.Models;

public class UserListItem
{
    public int      Id        { get; set; }
    public string   FullName  { get; set; } = "";
    public string   Email     { get; set; } = "";
    public string   Role      { get; set; } = "User";
    public bool     IsActive  { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

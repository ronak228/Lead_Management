namespace LeadManagementSystem.Models;

public class ConfigItem
{
    public int    Id        { get; set; }
    public string Name      { get; set; } = "";
    public bool   IsActive  { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class ConfigurationViewModel
{
    public List<ConfigItem> Statuses    { get; set; } = new();
    public List<ConfigItem> Modules     { get; set; } = new();
    public List<ConfigItem> Products    { get; set; } = new();
    public List<ConfigItem> Categories  { get; set; } = new();
    public List<ConfigItem> Cities      { get; set; } = new();
    public string? ActiveTab            { get; set; } = "status";
}

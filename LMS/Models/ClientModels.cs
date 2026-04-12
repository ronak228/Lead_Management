namespace LeadManagementSystem.Models;

public class Client
{
    public int      Id               { get; set; }
    public string   ClientRef        { get; set; } = "";
    public int?     UserId           { get; set; }
    public string   CompanyName      { get; set; } = "";
    public string   ContactPerson    { get; set; } = "";
    public string   Phone            { get; set; } = "";
    public string?  Email            { get; set; }
    public int?     CityId           { get; set; }
    public string?  CityName         { get; set; }
    public string?  CityText         { get; set; }
    public int?     ModuleId         { get; set; }
    public string?  ModuleName       { get; set; }
    public string?  RoomSize         { get; set; }
    public string?  Address          { get; set; }
    public string?  Notes            { get; set; }
    public decimal  TotalAmount      { get; set; }
    public int?     SourceInquiryId  { get; set; }
    public bool     IsActive         { get; set; } = true;
    public bool     IsDeleted        { get; set; }
    public int?     CreatedBy        { get; set; }
    public string?  CreatedByName    { get; set; }
    public int?     UpdatedBy        { get; set; }
    public DateTime CreatedAt        { get; set; }
    public DateTime UpdatedAt        { get; set; }
    // computed
    public decimal  TotalPaid        { get; set; }
    public decimal  TotalRemaining   => TotalAmount - TotalPaid;
    public string   PaymentStatus    => TotalAmount <= 0 ? "Pending"
                                      : TotalPaid >= TotalAmount ? "Paid"
                                      : TotalPaid > 0 ? "Partial" : "Pending";
}

public class ClientFormViewModel
{
    public Client           Client          { get; set; } = new();
    public List<ConfigItem> Cities          { get; set; } = new();
    public List<ConfigItem> Modules         { get; set; } = new();
    // Login account — only used when Admin/Employee manually creates a new client
    public bool             CreateLoginAccount { get; set; } = true;
    public string?          InitialPassword    { get; set; }
    public string?          ConfirmPassword    { get; set; }
}

public class ClientListViewModel
{
    public List<Client> Clients      { get; set; } = new();
    public string?      Search       { get; set; }
    public string?      FilterStatus { get; set; }
}

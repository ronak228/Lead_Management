namespace LeadManagementSystem.Models;

public class Inquiry
{
    public int     Id               { get; set; }
    public string  HotelName        { get; set; } = "";
    public string  ClientName       { get; set; } = "";
    public string  ClientNumber     { get; set; } = "";
    public int?    CityId           { get; set; }
    public string? CityName         { get; set; }
    public string? CityText         { get; set; }
    public int?    ModuleId         { get; set; }
    public string? ModuleName       { get; set; }
    public int?    StatusId         { get; set; }
    public string? StatusName       { get; set; }
    public bool    PaymentReceived  { get; set; }
    public DateTime? FollowupDate   { get; set; }
    public string? Note             { get; set; }
    public bool    IsConverted      { get; set; }
    public int?    ConvertedClientId { get; set; }
    public bool    IsDeleted        { get; set; }
    public int?    CreatedBy        { get; set; }
    public string? CreatedByName    { get; set; }
    public int?    UpdatedBy        { get; set; }
    public DateTime CreatedAt       { get; set; }
    public DateTime UpdatedAt       { get; set; }
}

public class InquiryFormViewModel
{
    public Inquiry              Inquiry    { get; set; } = new();
    public List<ConfigItem>     Cities     { get; set; } = new();
    public List<ConfigItem>     Modules    { get; set; } = new();
    public List<ConfigItem>     Statuses   { get; set; } = new();
}

public class InquiryListViewModel
{
    public List<Inquiry>    Inquiries     { get; set; } = new();
    public List<ConfigItem> Statuses      { get; set; } = new();
    public List<ConfigItem> Modules       { get; set; } = new();
    public List<ConfigItem> Cities        { get; set; } = new();
    public string?          Search        { get; set; }
    public int?             FilterStatus  { get; set; }
    public int?             FilterModule  { get; set; }
    public string?          FilterPayment { get; set; }
    public string?          FilterCity    { get; set; }
    public string?          DateFrom      { get; set; }
    public string?          DateTo        { get; set; }
}

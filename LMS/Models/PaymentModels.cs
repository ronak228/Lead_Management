using LeadManagementSystem.Helpers;

namespace LeadManagementSystem.Models;

public class Payment
{
    public int      Id            { get; set; }
    public int      ClientId        { get; set; }
    public string?  ClientRef       { get; set; }
    public string?  CompanyName     { get; set; }
    public string?  ContactPerson   { get; set; }
    public decimal  TotalAmount    { get; set; }
    public decimal  Amount         { get; set; }
    public string   PaymentMode   { get; set; } = "Cash";
    public string?  ChequeNo      { get; set; }
    public string?  BankName      { get; set; }
    public string?  TransactionId { get; set; }
    public DateTime PaymentDate   { get; set; }
    public string?  Note          { get; set; }
    public string?  ProofFile     { get; set; }
    public bool     IsDeleted     { get; set; }
    public int?     CreatedBy     { get; set; }
    public string?  CreatedByName { get; set; }
    public int?     UpdatedBy     { get; set; }
    public DateTime CreatedAt     { get; set; }
    public DateTime UpdatedAt     { get; set; }
}

public class PaymentFormViewModel
{
    public Payment         Payment { get; set; } = new();
    public Client?         Client  { get; set; }
    public List<Client>    Clients { get; set; } = new();
}

public class PaymentListViewModel
{
    public List<Payment> Payments    { get; set; } = new();
    public decimal       TotalAmount { get; set; }
    public int?          ClientId    { get; set; }
    public string?       ClientRef   { get; set; }
    public string?       CompanyName { get; set; }
    public string?       Search      { get; set; }
    public string?       FilterMode  { get; set; }
    public string?       DateFrom    { get; set; }
    public string?       DateTo      { get; set; }
    public PaginationInfo? Pagination { get; set; }
}

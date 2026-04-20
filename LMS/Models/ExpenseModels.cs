using LeadManagementSystem.Helpers;

namespace LeadManagementSystem.Models;

public class Expense
{
    public int      Id            { get; set; }
    public DateTime ExpenseDate   { get; set; }
    public int?     CategoryId    { get; set; }
    public string?  CategoryName  { get; set; }
    public string?  FromName      { get; set; }
    public string?  ToName        { get; set; }
    public decimal  Amount        { get; set; }
    public string   PaymentMode   { get; set; } = "Cash";
    public string?  ChequeNo      { get; set; }
    public string?  BankName      { get; set; }
    public string?  TransactionId { get; set; }
    public string?  Note          { get; set; }
    public string?  Attachment    { get; set; }
    public bool     IsDeleted     { get; set; }
    public int?     CreatedBy     { get; set; }
    public string?  CreatedByName { get; set; }
    public int?     UpdatedBy     { get; set; }
    public DateTime CreatedAt     { get; set; }
    public DateTime UpdatedAt     { get; set; }
}

public class ExpenseFormViewModel
{
    public Expense         Expense    { get; set; } = new();
    public List<ConfigItem> Categories { get; set; } = new();
}

public class ExpenseListViewModel
{
    public List<Expense>    Expenses       { get; set; } = new();
    public List<ConfigItem> Categories     { get; set; } = new();
    public decimal          TotalAmount    { get; set; }
    public int?             CategoryId     { get; set; }
    public int?             FilterCategory { get; set; }
    public string?          Search         { get; set; }
    public string?          DateFrom       { get; set; }
    public string?          DateTo         { get; set; }
    public PaginationInfo?  Pagination     { get; set; }
}

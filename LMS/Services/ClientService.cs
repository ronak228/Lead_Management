using LeadManagementSystem.Data;
using LeadManagementSystem.Models;

namespace LeadManagementSystem.Services;

/// <summary>
/// Service layer for client-related operations.
/// Centralizes client data access and business logic.
/// </summary>
public class ClientService
{
    private readonly DbHelper _db;

    public ClientService(DbHelper db) => _db = db;

    /// <summary>
    /// Gets a client by user ID.
    /// Fetches full client details including totals and relationships.
    /// </summary>
    public async Task<Client?> GetByUserIdAsync(int userId)
    {
        var rows = await _db.QueryAsync(@"
            SELECT c.id, c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   c.city_id, c.module_id, c.address, c.notes,
                   c.total_amount, c.source_inquiry_id,
                   c.is_active, c.is_deleted, c.created_by, c.updated_by, c.created_at, c.updated_at,
                   ci.name AS city_name, mo.name AS module_name,
                   u.full_name AS created_by_name,
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE),0) AS total_paid
            FROM users c
            LEFT JOIN cfg_city   ci ON ci.id=c.city_id
            LEFT JOIN cfg_module mo ON mo.id=c.module_id
            LEFT JOIN users       u ON u.id=c.created_by
            WHERE c.id=@id AND c.is_deleted=FALSE AND c.role='Client'",
            new() { ["@id"] = userId });
        
        return rows.Count == 0 ? null : MapRow(rows[0]);
    }

    /// <summary>
    /// Gets a client by client ID.
    /// Full details including payment totals.
    /// </summary>
    public async Task<Client?> GetByIdAsync(int id)
    {
        var rows = await _db.QueryAsync(@"
            SELECT c.id, c.client_ref, c.company_name, c.contact_person, c.phone, c.email,
                   c.city_id, c.module_id, c.address, c.notes,
                   c.total_amount, c.source_inquiry_id,
                   c.is_active, c.is_deleted, c.created_by, c.updated_by, c.created_at, c.updated_at,
                   ci.name AS city_name, mo.name AS module_name,
                   u.full_name AS created_by_name,
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.client_id=c.id AND p.is_deleted=FALSE),0) AS total_paid
            FROM users c
            LEFT JOIN cfg_city   ci ON ci.id=c.city_id
            LEFT JOIN cfg_module mo ON mo.id=c.module_id
            LEFT JOIN users       u ON u.id=c.created_by
            WHERE c.id=@id AND c.is_deleted=FALSE",
            new() { ["@id"] = id });
        return rows.Count == 0 ? null : MapRow(rows[0]);
    }

    private static Client MapRow(Dictionary<string, object?> r) => new()
    {
        Id               = Convert.ToInt32(r["id"]),
        ClientRef        = r["client_ref"]?.ToString() ?? "",
        CompanyName      = r["company_name"]?.ToString() ?? "",
        ContactPerson    = r["contact_person"]?.ToString() ?? "",
        Phone            = r["phone"]?.ToString() ?? "",
        Email            = r["email"]?.ToString(),
        CityId           = r["city_id"] is DBNull or null ? null : Convert.ToInt32(r["city_id"]),
        CityName         = r["city_name"]?.ToString(),
        ModuleId         = r["module_id"] is DBNull or null ? null : Convert.ToInt32(r["module_id"]),
        ModuleName       = r["module_name"]?.ToString(),
        Address          = r["address"]?.ToString(),
        Notes            = r["notes"]?.ToString(),
        TotalAmount      = r["total_amount"] is DBNull or null ? 0 : Convert.ToDecimal(r["total_amount"]),
        TotalPaid        = r["total_paid"] is DBNull or null ? 0 : Convert.ToDecimal(r["total_paid"]),
        SourceInquiryId  = r["source_inquiry_id"] is DBNull or null ? null : Convert.ToInt32(r["source_inquiry_id"]),
        IsActive         = Convert.ToBoolean(r["is_active"]),
        IsDeleted        = Convert.ToBoolean(r.ContainsKey("is_deleted") ? r["is_deleted"] : false),
        CreatedBy        = r["created_by"] is DBNull or null ? null : Convert.ToInt32(r["created_by"]),
        CreatedByName    = r["created_by_name"]?.ToString(),
        UpdatedBy        = r.ContainsKey("updated_by") && r["updated_by"] is not (DBNull or null) ? Convert.ToInt32(r["updated_by"]) : null,
        CreatedAt        = Convert.ToDateTime(r["created_at"]),
        UpdatedAt        = Convert.ToDateTime(r["updated_at"])
    };
}

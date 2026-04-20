namespace LeadManagementSystem.Helpers;

/// <summary>
/// Pagination support for list views
/// </summary>
public class PaginationHelper
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Calculate pagination parameters: skip, take, total pages
    /// </summary>
    public static (int skip, int take, int totalPages) GetPaginationParams(int pageNumber, int totalRecords, int pageSize = DefaultPageSize)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        
        int skip = (pageNumber - 1) * pageSize;
        int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
        int take = pageSize;

        return (skip, take, totalPages);
    }

    /// <summary>
    /// Generate SQL LIMIT/OFFSET clause
    /// </summary>
    public static string GetLimitClause(int pageNumber, int pageSize = DefaultPageSize)
    {
        var (skip, take, _) = GetPaginationParams(pageNumber, int.MaxValue, pageSize);
        return $" LIMIT {take} OFFSET {skip}";
    }
}

/// <summary>
/// Pagination info for views
/// </summary>
public class PaginationInfo
{
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = PaginationHelper.DefaultPageSize;
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
}

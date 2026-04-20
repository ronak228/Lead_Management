using Npgsql;

namespace LeadManagementSystem.Data;

/// <summary>
/// Database helper with connection pooling via NpgsqlDataSource.
/// Uses singleton NpgsqlDataSource to manage connection pool (~20 connections by default),
/// reducing overhead of creating new connections for each operation.
/// </summary>
public class DbHelper
{
    private readonly NpgsqlDataSource _dataSource;

    public DbHelper(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object?> parameters)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<object?> ExecuteScalarAsync(string sql, Dictionary<string, object?> parameters)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, Dictionary<string, object?> parameters)
    {
        var results = new List<Dictionary<string, object?>>();
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            results.Add(row);
        }
        return results;
    }

    /// <summary>
    /// Execute multiple operations within a single transaction to ensure atomicity.
    /// If any operation fails, the entire transaction is rolled back.
    /// </summary>
    public async Task<T> ExecuteTransactionAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> operations)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            var result = await operations(conn, transaction);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Execute a non-query command within an existing transaction context
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, 
        string sql, Dictionary<string, object?> parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, transaction);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Execute a scalar query within an existing transaction context
    /// </summary>
    public async Task<object?> ExecuteScalarAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, 
        string sql, Dictionary<string, object?> parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, transaction);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Execute a query within an existing transaction context
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> QueryAsync(NpgsqlConnection conn, NpgsqlTransaction transaction,
        string sql, Dictionary<string, object?> parameters)
    {
        var results = new List<Dictionary<string, object?>>();
        await using var cmd = new NpgsqlCommand(sql, conn, transaction);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            results.Add(row);
        }
        return results;
    }
}

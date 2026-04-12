using Npgsql;

namespace LeadManagementSystem.Data;

public class DbHelper
{
    private readonly string _connectionString;

    public DbHelper(string connectionString)
    {
        _connectionString = connectionString;
    }

    public NpgsqlConnection GetConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object?> parameters)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<object?> ExecuteScalarAsync(string sql, Dictionary<string, object?> parameters)
    {
        await using var conn = GetConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
            cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, Dictionary<string, object?> parameters)
    {
        var results = new List<Dictionary<string, object?>>();
        await using var conn = GetConnection();
        await conn.OpenAsync();
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
}

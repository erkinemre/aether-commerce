using Npgsql;

internal static class Db
{
    internal static async Task<T?> ExecuteScalarAsync<T>(NpgsqlConnection c,
        NpgsqlTransaction tx, string sql, CancellationToken ct,
        params (string Key, object Value)[] values)
    {
        await using var cmd = NewCommand(c, tx, sql, values);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar is null or DBNull ? default : (T)scalar;
    }

    internal static async Task ExecuteAsync(NpgsqlConnection c, NpgsqlTransaction tx,
        string sql, CancellationToken ct, params (string Key, object Value)[] values)
    {
        await using var cmd = NewCommand(c, tx, sql, values);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static NpgsqlCommand NewCommand(NpgsqlConnection c, NpgsqlTransaction tx,
        string sql, params (string Key, object Value)[] values)
    {
        var cmd = new NpgsqlCommand(sql, c, tx);
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value);
        return cmd;
    }

    internal static async Task<(Guid OrderId, string Hash)?> FetchExistingAsync(
        NpgsqlConnection c, NpgsqlTransaction tx, string tenant,
        string key, CancellationToken ct)
    {
        await using var cmd = NewCommand(c, tx,
            """
            SELECT order_id, request_hash FROM idempotency_keys
            WHERE tenant_id=@tenant AND operation='orders.create' AND idempotency_key=@key
            """, ("tenant", tenant), ("key", key));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? (reader.GetGuid(0), reader.GetString(1)) : null;
    }
}

using Aether.Contracts;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Inventory") ??
    throw new InvalidOperationException("ConnectionStrings:Inventory missing")));
builder.AddApiDefaults("Security:ServiceKey");
var app = builder.Build();
app.UseApiDefaults("Security:ServiceKey");
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

app.MapPost("/internal/order-placed", async (OrderPlacedV1 evt,
    NpgsqlDataSource db, CancellationToken ct) =>
{
    if (evt.EventId == Guid.Empty || evt.OrderId == Guid.Empty ||
        evt.SkuId == Guid.Empty || evt.Quantity is <= 0 or > 1000 ||
        string.IsNullOrWhiteSpace(evt.TenantId) || evt.TenantId.Length > 80)
        return Results.BadRequest();

    await using var con = await db.OpenConnectionAsync(ct);
    await using var tx = (NpgsqlTransaction)await con.BeginTransactionAsync(ct);
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO inbox_messages(consumer_name, message_id)
        VALUES ('inventory', @event_id) ON CONFLICT DO NOTHING
        """, con, tx);
    insert.Parameters.AddWithValue("event_id", evt.EventId);
    var inserted = await insert.ExecuteNonQueryAsync(ct);
    if (inserted == 0)
    {
        await tx.CommitAsync(ct);
        return await ReadOutcomeAsync(con, evt, ct);
    }

    // Reserve the order id FIRST; otherwise two distinct eventIds for the same
    // order could decrement stock twice despite the unique reservations key.
    await using var reservationGuard = new NpgsqlCommand(
        """
        INSERT INTO reservations(order_id, sku_id, quantity, status)
        VALUES (@order_id, @sku, @qty, 'Processing')
        ON CONFLICT (order_id) DO NOTHING
        """, con, tx);
    reservationGuard.Parameters.AddWithValue("order_id", evt.OrderId);
    reservationGuard.Parameters.AddWithValue("sku", evt.SkuId);
    reservationGuard.Parameters.AddWithValue("qty", evt.Quantity);
    var firstForOrder = await reservationGuard.ExecuteNonQueryAsync(ct);
    if (firstForOrder == 0)
    {
        await tx.CommitAsync(ct);
        return await ReadOutcomeAsync(con, evt, ct);
    }

    await using var reserve = new NpgsqlCommand(
        """
        UPDATE stock SET available=available-@qty, reserved=reserved+@qty
        WHERE sku_id=@sku AND available>=@qty
        """, con, tx);
    reserve.Parameters.AddWithValue("qty", evt.Quantity);
    reserve.Parameters.AddWithValue("sku", evt.SkuId);
    var affected = await reserve.ExecuteNonQueryAsync(ct);

    await using var outcome = new NpgsqlCommand(
        """
        UPDATE reservations SET status=@status WHERE order_id=@order_id
        """, con, tx);
    outcome.Parameters.AddWithValue("order_id", evt.OrderId);
    outcome.Parameters.AddWithValue("status", affected == 1 ? "Reserved" : "Rejected");
    await outcome.ExecuteNonQueryAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Ok(new InventoryOutcome(evt.OrderId,
        affected == 1 ? "Reserved" : "Rejected"));
}).RequireRateLimiting("api");
app.MapGet("/internal/stock/{sku:guid}", async (Guid sku, NpgsqlDataSource db, CancellationToken ct) =>
{
    await using var command = db.CreateCommand("SELECT available, reserved FROM stock WHERE sku_id=@sku");
    command.Parameters.AddWithValue("sku", sku);
    await using var reader = await command.ExecuteReaderAsync(ct);
    return await reader.ReadAsync(ct)
        ? Results.Ok(new { skuId = sku, available = reader.GetInt32(0), reserved = reader.GetInt32(1) })
        : Results.NotFound();
}).RequireRateLimiting("api");
app.Run();

static async Task<IResult> ReadOutcomeAsync(NpgsqlConnection connection, OrderPlacedV1 evt, CancellationToken ct)
{
    await using var command = new NpgsqlCommand(
        "SELECT status FROM reservations WHERE order_id=@id AND sku_id=@sku AND quantity=@qty", connection);
    command.Parameters.AddWithValue("id", evt.OrderId);
    command.Parameters.AddWithValue("sku", evt.SkuId);
    command.Parameters.AddWithValue("qty", evt.Quantity);
    var status = await command.ExecuteScalarAsync(ct) as string;
    return status is "Reserved" or "Rejected"
        ? Results.Ok(new InventoryOutcome(evt.OrderId, status))
        : Results.Problem(statusCode: 409, title: "Event conflicts with a prior delivery");
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aether.Contracts;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Orders") ??
    throw new InvalidOperationException("ConnectionStrings:Orders missing")));
builder.Services.AddHttpClient("inventory", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["InventoryBaseUrl"] ?? "http://localhost:5102");
    client.Timeout = TimeSpan.FromSeconds(3);
    client.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["Security:ServiceKey"]);
});
builder.Services.AddHostedService<OutboxDispatcher>();
builder.AddApiDefaults("Security:ClientKey");
if (string.IsNullOrWhiteSpace(builder.Configuration["Security:TenantId"]))
    throw new InvalidOperationException("Security:TenantId required");
if (builder.Configuration["Security:ServiceKey"] is not { Length: >= 32 })
    throw new InvalidOperationException("Security:ServiceKey must contain at least 32 characters");
if (builder.Configuration["Security:ClientKey"] == builder.Configuration["Security:ServiceKey"])
    throw new InvalidOperationException("Client and service keys must differ");
var app = builder.Build();
app.UseApiDefaults("Security:ClientKey");

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapPost("/orders", async (CreateOrderRequest req, HttpRequest http, IConfiguration configuration,
    NpgsqlDataSource db, CancellationToken ct) =>
{
    if (req.TenantId != configuration["Security:TenantId"])
        return Results.Problem(statusCode: 403, title: "Tenant access denied");
    if (string.IsNullOrWhiteSpace(req.TenantId) || req.TenantId.Length > 80 ||
        req.SkuId == Guid.Empty || req.Quantity is <= 0 or > 1000)
        return Results.ValidationProblem(new Dictionary<string, string[]>
        { ["request"] = ["Invalid tenant, SKU or quantity"] });
    if (!http.Headers.TryGetValue("Idempotency-Key", out var idKey) ||
        idKey.Count != 1 || string.IsNullOrWhiteSpace(idKey) || idKey.ToString().Length > 128)
        return Results.Problem(statusCode: 400, title: "Valid Idempotency-Key required");

    var requestHash = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req))));
    var orderId = Guid.NewGuid();
    await using var con = await db.OpenConnectionAsync(ct);
    await using var tx = (NpgsqlTransaction)await con.BeginTransactionAsync(ct);

    var accepted = await Db.ExecuteScalarAsync<Guid?>(con, tx,
        """
        INSERT INTO idempotency_keys(tenant_id, operation, idempotency_key, request_hash, order_id)
        VALUES (@tenant, 'orders.create', @key, @hash, @order_id)
        ON CONFLICT DO NOTHING RETURNING order_id
        """, ct, ("tenant", req.TenantId), ("key", idKey.ToString()),
        ("hash", requestHash), ("order_id", orderId));

    if (accepted is null)
    {
        var existing = await Db.FetchExistingAsync(con, tx,
            req.TenantId, idKey.ToString(), ct);
        await tx.CommitAsync(ct);
        if (existing is null) return Results.Problem(statusCode: 503, title: "Retry request");
        if (!string.Equals(existing.Value.Hash, requestHash, StringComparison.Ordinal))
            return Results.Problem(statusCode: 409, title: "Idempotency key reused with different payload");
        return Results.Ok(new CreateOrderResponse(existing.Value.OrderId, "Pending"));
    }

    await Db.ExecuteAsync(con, tx,
        """
        INSERT INTO orders(id, tenant_id, sku_id, quantity, status)
        VALUES (@id, @tenant, @sku, @qty, 'Pending')
        """, ct, ("id", orderId), ("tenant", req.TenantId),
        ("sku", req.SkuId), ("qty", req.Quantity));

    var evt = new OrderPlacedV1(Guid.NewGuid(), orderId,
        req.TenantId, req.SkuId, req.Quantity, DateTimeOffset.UtcNow);
    await Db.ExecuteAsync(con, tx,
        """
        INSERT INTO outbox_messages(id, event_type, event_version, payload)
        VALUES (@id, 'OrderPlaced', 1, CAST(@payload AS jsonb))
        """, ct, ("id", evt.EventId), ("payload", JsonSerializer.Serialize(evt)));
    await tx.CommitAsync(ct);
    return Results.Created($"/orders/{orderId}?tenantId={Uri.EscapeDataString(req.TenantId)}", new CreateOrderResponse(orderId, "Pending"));
}).RequireRateLimiting("api");

app.MapGet("/orders/{id:guid}", async (Guid id, string tenantId, IConfiguration configuration,
    NpgsqlDataSource db, CancellationToken ct) =>
{
    if (tenantId != configuration["Security:TenantId"])
        return Results.Problem(statusCode: 403, title: "Tenant access denied");
    await using var conn = await db.OpenConnectionAsync(ct);
    await using var cmd = new NpgsqlCommand(
        "SELECT status FROM orders WHERE id=@id AND tenant_id=@tenant", conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("tenant", tenantId);
    var status = await cmd.ExecuteScalarAsync(ct) as string;
    return status is null ? Results.NotFound() : Results.Ok(new CreateOrderResponse(id, status));
}).RequireRateLimiting("api");
app.Run();

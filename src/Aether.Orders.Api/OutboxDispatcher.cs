using System.Net.Http.Json;
using System.Text.Json;
using Aether.Contracts;
using Npgsql;

public sealed class OutboxDispatcher(
    NpgsqlDataSource db, IHttpClientFactory clients,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try { await DispatchBatchAsync(stop); }
                catch (Exception e) when (!stop.IsCancellationRequested)
                { logger.LogError(e, "Outbox dispatch iteration failed"); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        var leaseId = Guid.NewGuid();
        var messages = new List<(Guid Id, string Payload)>();
        await using (var con = await db.OpenConnectionAsync(ct))
        {
            await using var cmd = new NpgsqlCommand(
                """
                WITH candidate AS (
                    SELECT id FROM outbox_messages
                    WHERE published_at IS NULL
                      AND (locked_until IS NULL OR locked_until < now())
                      AND next_attempt_at <= now()
                    ORDER BY created_at, id
                    FOR UPDATE SKIP LOCKED LIMIT 1
                )
                UPDATE outbox_messages o
                SET lease_id=@lease, locked_until=now()+interval '30 seconds',
                    attempts=attempts+1
                FROM candidate c WHERE o.id=c.id
                RETURNING o.id, o.payload::text
                """, con);
            cmd.Parameters.AddWithValue("lease", leaseId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                messages.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        var client = clients.CreateClient("inventory");
        foreach (var (id, payload) in messages)
        {
            try
            {
                var evt = JsonSerializer.Deserialize<OrderPlacedV1>(payload)
                    ?? throw new InvalidOperationException("Empty event");
                using var response = await client.PostAsJsonAsync("/internal/order-placed", evt, ct);
                response.EnsureSuccessStatusCode();
                var outcome = await response.Content.ReadFromJsonAsync<InventoryOutcome>(ct)
                    ?? throw new InvalidOperationException("Missing inventory outcome");
                if (outcome.OrderId != evt.OrderId || outcome.ReservationStatus is not ("Reserved" or "Rejected"))
                    throw new InvalidOperationException("Invalid inventory outcome");
                await CompleteAsync(id, leaseId, outcome, ct);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Delivery will retry for event {EventId}", id);
                await ScheduleRetryAsync(id, leaseId, e.GetType().Name, ct);
            }
        }
    }

    private async Task CompleteAsync(Guid id, Guid lease, InventoryOutcome outcome, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE outbox_messages SET published_at=now(), locked_until=NULL,
                lease_id=NULL, last_error=NULL
            WHERE id=@id AND lease_id=@lease AND published_at IS NULL
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("lease", lease);
        if (await command.ExecuteNonQueryAsync(ct) == 1)
        {
            await using var order = new NpgsqlCommand("""
                UPDATE orders SET status=@status WHERE id=@order AND status='Pending'
                """, connection, transaction);
            order.Parameters.AddWithValue("order", outcome.OrderId);
            order.Parameters.AddWithValue("status", outcome.ReservationStatus == "Reserved" ? "Confirmed" : "Rejected");
            await order.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
    private async Task ScheduleRetryAsync(Guid id, Guid lease, string error, CancellationToken ct)
    {
        await using var command = db.CreateCommand("""
            UPDATE outbox_messages
            SET locked_until=NULL, lease_id=NULL, last_error=@error,
                next_attempt_at=now() + (LEAST(attempts::bigint * attempts, 300) * interval '1 second')
            WHERE id=@id AND lease_id=@lease AND published_at IS NULL
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("lease", lease);
        command.Parameters.AddWithValue("error", error);
        await command.ExecuteNonQueryAsync(ct);
    }
}

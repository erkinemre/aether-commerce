namespace Aether.Contracts;

public sealed record OrderPlacedV1(
    Guid EventId,
    Guid OrderId,
    string TenantId,
    Guid SkuId,
    int Quantity,
    DateTimeOffset OccurredAt);

public sealed record CreateOrderRequest(string TenantId, Guid SkuId, int Quantity);
public sealed record CreateOrderResponse(Guid OrderId, string Status);
public sealed record InventoryOutcome(Guid OrderId, string ReservationStatus);

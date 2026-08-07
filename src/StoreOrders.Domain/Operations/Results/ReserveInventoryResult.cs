using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum ReserveInventoryOutcome
{
    Reserved,
    AlreadyReserved,
    InsufficientInventory
}

public sealed record ReserveInventoryResult(
    Guid OrderId,
    OrderStatus Status,
    ReserveInventoryOutcome Outcome,
    int? InsufficientProductId);

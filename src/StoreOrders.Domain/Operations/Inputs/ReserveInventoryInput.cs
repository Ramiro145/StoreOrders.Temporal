namespace StoreOrders.Domain.Operations.Inputs;

public sealed record ReserveInventoryInput(
    Guid OrderId);

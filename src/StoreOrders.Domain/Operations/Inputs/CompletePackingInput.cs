namespace StoreOrders.Domain.Operations.Inputs;

public sealed record CompletePackingInput(
    Guid OrderId,
    Guid EventId,
    string PackedBy,
    DateTime PackedAtUtc);

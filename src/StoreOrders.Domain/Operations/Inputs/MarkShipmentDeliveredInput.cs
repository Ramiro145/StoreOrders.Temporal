namespace StoreOrders.Domain.Operations.Inputs;

public sealed record MarkShipmentDeliveredInput(
    Guid OrderId,
    Guid EventId,
    DateTime DeliveredAtUtc);

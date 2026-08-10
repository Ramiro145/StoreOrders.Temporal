namespace StoreOrders.Domain.Operations.Inputs;

public sealed record MarkShipmentShippedInput(
    Guid OrderId,
    Guid EventId,
    string Carrier,
    string TrackingNumber,
    DateTime ShippedAtUtc);

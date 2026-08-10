using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum MarkShipmentDeliveredOutcome
{
    Delivered,
    AlreadyDelivered,
    OrderNotReady
}

public sealed record MarkShipmentDeliveredResult(
    Guid OrderId,
    OrderStatus OrderStatus,
    ShipmentStatus ShipmentStatus,
    MarkShipmentDeliveredOutcome Outcome);

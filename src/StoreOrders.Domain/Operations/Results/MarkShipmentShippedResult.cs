using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum MarkShipmentShippedOutcome
{
    Shipped,
    AlreadyShipped,
    OrderNotReady,
    TrackingNumberInUse
}

public sealed record MarkShipmentShippedResult(
    Guid OrderId,
    OrderStatus OrderStatus,
    ShipmentStatus ShipmentStatus,
    MarkShipmentShippedOutcome Outcome);

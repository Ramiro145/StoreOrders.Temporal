using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum CreateShipmentOutcome
{
    Created,
    AlreadyExists,
    OrderNotReady
}

public sealed record CreateShipmentResult(
    Guid OrderId,
    OrderStatus OrderStatus,
    ShipmentStatus? ShipmentStatus,
    CreateShipmentOutcome Outcome);

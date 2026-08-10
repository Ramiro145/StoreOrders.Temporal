using StoreOrders.Domain.Enums;

namespace StoreOrders.Workflows.Orders.Contracts;

public sealed record DeliveryProgressSignal(
    Guid OrderId,
    ShipmentStatus Status);

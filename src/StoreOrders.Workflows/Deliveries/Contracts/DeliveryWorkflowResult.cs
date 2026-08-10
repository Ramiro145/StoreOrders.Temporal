using StoreOrders.Domain.Enums;

namespace StoreOrders.Workflows.Deliveries.Contracts;

public sealed record DeliveryWorkflowResult(
    Guid OrderId,
    ShipmentStatus Status,
    string Message);

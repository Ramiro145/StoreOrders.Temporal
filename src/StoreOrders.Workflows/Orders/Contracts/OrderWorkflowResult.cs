using StoreOrders.Domain.Enums;

namespace StoreOrders.Workflows.Orders.Contracts;

public sealed record OrderWorkflowResult(
    Guid OrderId,
    OrderStatus Status,
    string Message);

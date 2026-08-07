using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum CreateOrderOutcome
{
    Created,
    AlreadyExists
}

public sealed record CreateOrderResult(
    Guid OrderId,
    string OrderNumber,
    decimal TotalAmount,
    OrderStatus Status,
    CreateOrderOutcome Outcome);

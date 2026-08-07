using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum StartFulfillmentOutcome
{
    Started,
    AlreadyStarted,
    OrderNotReady
}

public sealed record StartFulfillmentResult(
    Guid OrderId,
    OrderStatus Status,
    StartFulfillmentOutcome Outcome);

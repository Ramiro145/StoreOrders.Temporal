using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum CancelOrderOutcome
{
    Cancelled,
    AlreadyCancelled,
    NotAllowed
}

public sealed record CancelOrderResult(
    Guid OrderId,
    OrderStatus PreviousStatus,
    OrderStatus CurrentStatus,
    int ReleasedReservationCount,
    CancelOrderOutcome Outcome,
    string Message);

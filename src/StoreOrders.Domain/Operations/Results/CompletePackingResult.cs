using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum CompletePackingOutcome
{
    Packed,
    AlreadyPacked,
    OrderNotReady
}

public sealed record CompletePackingResult(
    Guid OrderId,
    OrderStatus Status,
    CompletePackingOutcome Outcome);

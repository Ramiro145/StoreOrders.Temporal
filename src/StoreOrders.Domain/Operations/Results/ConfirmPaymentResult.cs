using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Operations.Results;

public enum ConfirmPaymentOutcome
{
    Confirmed,
    AlreadyConfirmed,
    RejectedAmount,
    RejectedCurrency,
    OrderNotPayable
}

public sealed record ConfirmPaymentResult(
    Guid OrderId,
    OrderStatus Status,
    ConfirmPaymentOutcome Outcome);

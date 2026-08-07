using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Orders.Contracts;

public sealed record PaymentConfirmedSignal(
    Guid EventId,
    string ExternalPaymentReference,
    decimal Amount,
    string Currency,
    DateTime ConfirmedAtUtc)
{
    public ConfirmPaymentInput ToInput(Guid orderId)
    {
        return new ConfirmPaymentInput(
            orderId,
            EventId,
            ExternalPaymentReference,
            Amount,
            Currency,
            ConfirmedAtUtc);
    }
}

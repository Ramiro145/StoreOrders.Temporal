namespace StoreOrders.Domain.Operations.Inputs;

public sealed record ConfirmPaymentInput(
    Guid OrderId,
    Guid EventId,
    string ExternalPaymentReference,
    decimal Amount,
    string Currency,
    DateTime ConfirmedAtUtc);

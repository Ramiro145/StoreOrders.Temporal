using System.ComponentModel.DataAnnotations;

namespace StoreOrders.Api.Contracts.Orders;

public sealed record OrderRuntimeStatusResponse(
    Guid OrderId,
    string WorkflowId,
    string Stage,
    string WaitingFor,
    bool PaymentReceived,
    bool PackingCompleted,
    bool DeliveryStarted,
    bool CanChangeAddress,
    bool CanCancel);

public sealed record PaymentConfirmedRequest(
    Guid EventId,

    [Required, StringLength(100, MinimumLength = 1)]
    string ExternalPaymentReference,

    [Range(
        typeof(decimal),
        "0.01",
        "79228162514264337593543950335")]
    decimal Amount,

    [Required, RegularExpression("^[A-Za-z]{3}$")]
    string Currency,

    DateTimeOffset ConfirmedAtUtc);

public sealed record PackingCompletedRequest(
    Guid EventId,

    [Required, StringLength(100, MinimumLength = 1)]
    string PackedBy,

    DateTimeOffset PackedAtUtc);

public sealed record WorkflowEventAcceptedResponse(
    Guid OrderId,
    Guid EventId,
    string EventStatus,
    string Message);

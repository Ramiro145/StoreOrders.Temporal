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

public sealed record ChangeDeliveryAddressRequest(
    Guid OperationId,

    [Required, StringLength(150, MinimumLength = 1)]
    string RecipientName,

    [Required, StringLength(200, MinimumLength = 1)]
    string Line1,

    [StringLength(200)]
    string? Line2,

    [Required, StringLength(100, MinimumLength = 1)]
    string City,

    [Required, StringLength(100, MinimumLength = 1)]
    string State,

    [Required, StringLength(20, MinimumLength = 1)]
    string PostalCode,

    [Required, RegularExpression("^[A-Za-z]{2}$")]
    string CountryCode);

public sealed record ChangeDeliveryAddressResponse(
    Guid OperationId,
    Guid OrderId,
    bool Accepted,
    int AddressVersion,
    string Message);

public sealed record WorkflowEventAcceptedResponse(
    Guid OrderId,
    Guid EventId,
    string EventStatus,
    string Message);

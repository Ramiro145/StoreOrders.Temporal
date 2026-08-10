using System.ComponentModel.DataAnnotations;

namespace StoreOrders.Api.Contracts.Deliveries;

public sealed record ShipmentShippedRequest(
    Guid EventId,

    [Required, StringLength(100, MinimumLength = 1)]
    string Carrier,

    [Required, StringLength(100, MinimumLength = 1)]
    string TrackingNumber,

    DateTimeOffset ShippedAtUtc);

public sealed record ShipmentDeliveredRequest(
    Guid EventId,
    DateTimeOffset DeliveredAtUtc);

public sealed record DeliveryEventAcceptedResponse(
    Guid OrderId,
    string DeliveryWorkflowId,
    Guid EventId,
    string EventStatus);

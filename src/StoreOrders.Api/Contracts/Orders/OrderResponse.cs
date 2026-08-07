using StoreOrders.Domain.Enums;

namespace StoreOrders.Api.Contracts.Orders;

public sealed record OrderResponse(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    OrderCustomerResponse Customer,
    string Currency,
    decimal TotalAmount,
    OrderAddressResponse DeliveryAddress,
    IReadOnlyCollection<OrderItemResponse> Items,
    OrderPaymentResponse? Payment,
    OrderFulfillmentResponse? Fulfillment,
    OrderShipmentResponse? Shipment,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record OrderCustomerResponse(
    string Name,
    string Email);

public sealed record OrderAddressResponse(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    int AddressVersion);

public sealed record OrderItemResponse(
    int ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderPaymentResponse(
    string ExternalPaymentReference,
    decimal Amount,
    string Currency,
    string Status,
    DateTime ConfirmedAtUtc);

public sealed record OrderFulfillmentResponse(
    string Status,
    string? PackedBy,
    DateTime? PackedAtUtc);

public sealed record OrderShipmentResponse(
    string Status,
    string? Carrier,
    string? TrackingNumber,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc);

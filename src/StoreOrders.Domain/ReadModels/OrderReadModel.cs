using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.ReadModels;

public sealed record OrderReadModel(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    string CustomerName,
    string CustomerEmail,
    string Currency,
    decimal TotalAmount,
    OrderAddressReadModel DeliveryAddress,
    IReadOnlyCollection<OrderItemReadModel> Items,
    OrderPaymentReadModel? Payment,
    OrderFulfillmentReadModel? Fulfillment,
    OrderShipmentReadModel? Shipment,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record OrderAddressReadModel(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    int AddressVersion);

public sealed record OrderItemReadModel(
    int ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderPaymentReadModel(
    string ExternalPaymentReference,
    decimal Amount,
    string Currency,
    string Status,
    DateTime ConfirmedAtUtc);

public sealed record OrderFulfillmentReadModel(
    string Status,
    string? PackedBy,
    DateTime? PackedAtUtc);

public sealed record OrderShipmentReadModel(
    string Status,
    string? Carrier,
    string? TrackingNumber,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc);

using System.ComponentModel.DataAnnotations;

namespace StoreOrders.Api.Contracts.Orders;

public sealed record CreateOrderRequest(
    [Required]
    CreateOrderCustomerRequest Customer,

    [Required]
    CreateOrderAddressRequest DeliveryAddress,

    [Required, MinLength(1)]
    CreateOrderItemRequest[] Items);

public sealed record CreateOrderCustomerRequest(
    [Required, StringLength(200, MinimumLength = 1)]
    string Name,

    [Required, EmailAddress, StringLength(320)]
    string Email);

public sealed record CreateOrderAddressRequest(
    [Required, StringLength(200, MinimumLength = 1)]
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

    [Required, StringLength(2, MinimumLength = 2)]
    string CountryCode);

public sealed record CreateOrderItemRequest(
    [Range(1, int.MaxValue)]
    int ProductId,

    [Range(1, int.MaxValue)]
    int Quantity);

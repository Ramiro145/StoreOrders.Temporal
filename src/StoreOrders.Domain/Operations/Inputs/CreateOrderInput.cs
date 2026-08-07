namespace StoreOrders.Domain.Operations.Inputs;

public sealed record CreateOrderInput(
    Guid OrderId,
    string ClientRequestId,
    string TemporalWorkflowId,
    string CustomerName,
    string CustomerEmail,
    CreateOrderAddressInput Address,
    CreateOrderItemInput[] Items);

public sealed record CreateOrderAddressInput(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record CreateOrderItemInput(
    int ProductId,
    int Quantity);

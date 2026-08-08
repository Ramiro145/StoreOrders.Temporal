namespace StoreOrders.Domain.Operations.Inputs;

public sealed record ChangeDeliveryAddressInput(
    Guid OrderId,
    Guid OperationId,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

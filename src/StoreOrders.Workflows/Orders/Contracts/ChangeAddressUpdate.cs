using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Orders.Contracts;

public sealed record ChangeAddressUpdate(
    Guid OperationId,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string CountryCode)
{
    public ChangeDeliveryAddressInput ToInput(Guid orderId)
    {
        return new ChangeDeliveryAddressInput(
            orderId,
            OperationId,
            RecipientName,
            Line1,
            Line2,
            City,
            State,
            PostalCode,
            CountryCode);
    }
}

public sealed record ChangeAddressUpdateResult(
    Guid OperationId,
    Guid OrderId,
    bool Accepted,
    int AddressVersion,
    string Message);

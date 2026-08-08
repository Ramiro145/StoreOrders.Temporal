namespace StoreOrders.Domain.Operations.Results;

public enum ChangeDeliveryAddressOutcome
{
    Changed,
    AlreadyChanged,
    NotAllowed
}

public sealed record ChangeDeliveryAddressResult(
    Guid OrderId,
    int AddressVersion,
    ChangeDeliveryAddressOutcome Outcome,
    string Message);

namespace StoreOrders.Domain.Operations.Inputs;

public sealed record StartFulfillmentInput(
    Guid OrderId);

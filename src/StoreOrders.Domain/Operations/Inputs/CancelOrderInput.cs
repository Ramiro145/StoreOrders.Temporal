namespace StoreOrders.Domain.Operations.Inputs;

public sealed record CancelOrderInput(
    Guid OrderId,
    Guid OperationId,
    string Reason,
    string RequestedBy);

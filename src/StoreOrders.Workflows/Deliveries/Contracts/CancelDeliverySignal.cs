namespace StoreOrders.Workflows.Deliveries.Contracts;

public sealed record CancelDeliverySignal(
    Guid OperationId);

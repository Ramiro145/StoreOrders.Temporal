namespace StoreOrders.Domain.Operations.Inputs;

public sealed record CreateShipmentInput(
    Guid OrderId,
    string DeliveryWorkflowId);

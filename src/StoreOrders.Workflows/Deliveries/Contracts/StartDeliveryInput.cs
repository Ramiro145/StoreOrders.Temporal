using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Deliveries.Contracts;

public sealed record StartDeliveryInput(
    Guid OrderId,
    string ParentWorkflowId,
    string DeliveryWorkflowId)
{
    public CreateShipmentInput ToCreateShipmentInput()
    {
        return new CreateShipmentInput(
            OrderId,
            DeliveryWorkflowId);
    }
}

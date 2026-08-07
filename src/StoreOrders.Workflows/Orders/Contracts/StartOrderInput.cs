using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Orders.Contracts;

public sealed record StartOrderInput(
    Guid OrderId,
    string ClientRequestId,
    string TemporalWorkflowId,
    string CustomerName,
    string CustomerEmail,
    CreateOrderAddressInput Address,
    CreateOrderItemInput[] Items)
{
    public CreateOrderInput ToCreateOrderInput()
    {
        return new CreateOrderInput(
            OrderId,
            ClientRequestId,
            TemporalWorkflowId,
            CustomerName,
            CustomerEmail,
            Address,
            Items);
    }
}

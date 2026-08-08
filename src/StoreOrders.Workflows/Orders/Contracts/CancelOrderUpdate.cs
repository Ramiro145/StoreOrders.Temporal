using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Orders.Contracts;

public sealed record CancelOrderUpdate(
    Guid OperationId,
    string Reason,
    string RequestedBy)
{
    public CancelOrderInput ToInput(Guid orderId)
    {
        return new CancelOrderInput(
            orderId,
            OperationId,
            Reason,
            RequestedBy);
    }
}

public sealed record CancelOrderUpdateResult(
    Guid OperationId,
    Guid OrderId,
    bool Accepted,
    OrderStatus PreviousStatus,
    OrderStatus CurrentStatus,
    int ReleasedReservationCount,
    string Message);

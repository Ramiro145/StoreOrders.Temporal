using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Orders.Contracts;

public sealed record PackingCompletedSignal(
    Guid EventId,
    string PackedBy,
    DateTime PackedAtUtc)
{
    public CompletePackingInput ToInput(Guid orderId)
    {
        return new CompletePackingInput(
            orderId,
            EventId,
            PackedBy,
            PackedAtUtc);
    }
}

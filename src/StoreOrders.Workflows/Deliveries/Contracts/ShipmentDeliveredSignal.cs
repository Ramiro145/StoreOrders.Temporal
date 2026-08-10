using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Deliveries.Contracts;

public sealed record ShipmentDeliveredSignal(
    Guid EventId,
    DateTime DeliveredAtUtc)
{
    public MarkShipmentDeliveredInput ToInput(Guid orderId)
    {
        return new MarkShipmentDeliveredInput(
            orderId,
            EventId,
            DeliveredAtUtc);
    }
}

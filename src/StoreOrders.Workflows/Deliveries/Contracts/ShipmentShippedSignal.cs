using StoreOrders.Domain.Operations.Inputs;

namespace StoreOrders.Workflows.Deliveries.Contracts;

public sealed record ShipmentShippedSignal(
    Guid EventId,
    string Carrier,
    string TrackingNumber,
    DateTime ShippedAtUtc)
{
    public MarkShipmentShippedInput ToInput(Guid orderId)
    {
        return new MarkShipmentShippedInput(
            orderId,
            EventId,
            Carrier,
            TrackingNumber,
            ShippedAtUtc);
    }
}

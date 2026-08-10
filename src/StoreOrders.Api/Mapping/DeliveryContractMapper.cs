using StoreOrders.Api.Contracts.Deliveries;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Deliveries.Contracts;

namespace StoreOrders.Api.Mapping;

public static class DeliveryContractMapper
{
    public static ShipmentShippedSignal ToSignal(
        ShipmentShippedRequest request)
    {
        return new ShipmentShippedSignal(
            request.EventId,
            request.Carrier,
            request.TrackingNumber,
            request.ShippedAtUtc.UtcDateTime);
    }

    public static ShipmentDeliveredSignal ToSignal(
        ShipmentDeliveredRequest request)
    {
        return new ShipmentDeliveredSignal(
            request.EventId,
            request.DeliveredAtUtc.UtcDateTime);
    }

    public static DeliveryEventAcceptedResponse ToAcceptedResponse(
        Guid orderId,
        Guid eventId)
    {
        return new DeliveryEventAcceptedResponse(
            orderId,
            TemporalNames.DeliveryWorkflowId(orderId),
            eventId,
            "Accepted");
    }
}

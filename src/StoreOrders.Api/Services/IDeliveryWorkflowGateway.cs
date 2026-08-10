using StoreOrders.Workflows.Deliveries.Contracts;

namespace StoreOrders.Api.Services;

public interface IDeliveryWorkflowGateway
{
    Task SignalShippedAsync(
        Guid orderId,
        ShipmentShippedSignal signal,
        CancellationToken cancellationToken = default);

    Task SignalDeliveredAsync(
        Guid orderId,
        ShipmentDeliveredSignal signal,
        CancellationToken cancellationToken = default);
}

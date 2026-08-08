using StoreOrders.Workflows.Orders.Contracts;

namespace StoreOrders.Api.Services;

public interface IOrderWorkflowGateway
{
    Task<StartOrderWorkflowResult> StartAsync(
        StartOrderInput input,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<OrderRuntimeStatus> GetRuntimeStatusAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task SignalPaymentConfirmedAsync(
        Guid orderId,
        PaymentConfirmedSignal signal,
        CancellationToken cancellationToken = default);

    Task SignalPackingCompletedAsync(
        Guid orderId,
        PackingCompletedSignal signal,
        CancellationToken cancellationToken = default);

    Task<ChangeAddressUpdateResult> ChangeDeliveryAddressAsync(
        Guid orderId,
        ChangeAddressUpdate update,
        CancellationToken cancellationToken = default);
}

public sealed record StartOrderWorkflowResult(
    string WorkflowId,
    bool AlreadyExisted);

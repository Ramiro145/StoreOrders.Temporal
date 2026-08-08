using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;

namespace StoreOrders.Domain.Abstractions;

public interface IOrderOperations
{
    Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderInput input,
        CancellationToken cancellationToken = default);

    Task<ReserveInventoryResult> ReserveInventoryAsync(
        ReserveInventoryInput input,
        CancellationToken cancellationToken = default);

    Task<ConfirmPaymentResult> ConfirmPaymentAsync(
        ConfirmPaymentInput input,
        CancellationToken cancellationToken = default);

    Task<StartFulfillmentResult> StartFulfillmentAsync(
        StartFulfillmentInput input,
        CancellationToken cancellationToken = default);

    Task<CompletePackingResult> CompletePackingAsync(
        CompletePackingInput input,
        CancellationToken cancellationToken = default);

    Task<ChangeDeliveryAddressResult> ChangeDeliveryAddressAsync(
        ChangeDeliveryAddressInput input,
        CancellationToken cancellationToken = default);

    Task<CancelOrderResult> CancelOrderAsync(
        CancelOrderInput input,
        CancellationToken cancellationToken = default);
}

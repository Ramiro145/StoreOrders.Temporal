using StoreOrders.Domain.Abstractions;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using Temporalio.Activities;

namespace StoreOrders.Workflows.Activities;

public sealed class OrderActivities(
    IOrderOperations orderOperations)
{
    [Activity]
    public async Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderInput input)
    {
        return await orderOperations.CreateOrderAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<ReserveInventoryResult> ReserveInventoryAsync(
        ReserveInventoryInput input)
    {
        return await orderOperations.ReserveInventoryAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<ConfirmPaymentResult> ConfirmPaymentAsync(
        ConfirmPaymentInput input)
    {
        return await orderOperations.ConfirmPaymentAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<StartFulfillmentResult> StartFulfillmentAsync(
        StartFulfillmentInput input)
    {
        return await orderOperations.StartFulfillmentAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<CompletePackingResult> CompletePackingAsync(
        CompletePackingInput input)
    {
        return await orderOperations.CompletePackingAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<ChangeDeliveryAddressResult>
        ChangeDeliveryAddressAsync(
            ChangeDeliveryAddressInput input)
    {
        return await orderOperations.ChangeDeliveryAddressAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<CancelOrderResult> CancelOrderAsync(
        CancelOrderInput input)
    {
        return await orderOperations.CancelOrderAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<CreateShipmentResult> CreateShipmentAsync(
        CreateShipmentInput input)
    {
        return await orderOperations.CreateShipmentAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<MarkShipmentShippedResult>
        MarkShipmentShippedAsync(
            MarkShipmentShippedInput input)
    {
        return await orderOperations.MarkShipmentShippedAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }

    [Activity]
    public async Task<MarkShipmentDeliveredResult>
        MarkShipmentDeliveredAsync(
            MarkShipmentDeliveredInput input)
    {
        return await orderOperations.MarkShipmentDeliveredAsync(
            input,
            ActivityExecutionContext.Current.CancellationToken);
    }
}

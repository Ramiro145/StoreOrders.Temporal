using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Deliveries;
using StoreOrders.Workflows.Deliveries.Contracts;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace StoreOrders.Api.Services;

public sealed class TemporalDeliveryWorkflowGateway(
    ITemporalClient temporalClient)
    : IDeliveryWorkflowGateway
{
    public async Task SignalShippedAsync(
        Guid orderId,
        ShipmentShippedSignal signal,
        CancellationToken cancellationToken = default)
    {
        await SignalAsync(
            orderId,
            workflow => workflow.ShipmentShippedAsync(signal),
            cancellationToken);
    }

    public async Task SignalDeliveredAsync(
        Guid orderId,
        ShipmentDeliveredSignal signal,
        CancellationToken cancellationToken = default)
    {
        await SignalAsync(
            orderId,
            workflow => workflow.ShipmentDeliveredAsync(signal),
            cancellationToken);
    }

    private async Task SignalAsync(
        Guid orderId,
        System.Linq.Expressions.Expression<
            Func<DeliveryWorkflow, Task>> signal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId =
            TemporalNames.DeliveryWorkflowId(orderId);

        try
        {
            var handle =
                temporalClient.GetWorkflowHandle<DeliveryWorkflow>(
                    workflowId);

            await handle.SignalAsync(signal);
        }
        catch (RpcException exception)
            when (exception.Code is
                  RpcException.StatusCode.NotFound or
                  RpcException.StatusCode.FailedPrecondition)
        {
            throw new DeliveryWorkflowNotReadyException(
                "El proceso de entrega todavía no está disponible " +
                "o ya terminó.",
                exception);
        }
        catch (RpcException exception)
        {
            throw new DeliveryWorkflowUnavailableException(
                "No fue posible enviar el evento de entrega a Temporal.",
                exception);
        }
    }
}

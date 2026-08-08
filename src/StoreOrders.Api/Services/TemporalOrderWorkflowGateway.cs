using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Orders;
using StoreOrders.Workflows.Orders.Contracts;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace StoreOrders.Api.Services;

public sealed class TemporalOrderWorkflowGateway(
    ITemporalClient temporalClient)
    : IOrderWorkflowGateway
{
    public async Task<StartOrderWorkflowResult> StartAsync(
        StartOrderInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId =
            TemporalNames.OrderWorkflowId(input.OrderId);

        try
        {
            await temporalClient.StartWorkflowAsync(
                (OrderWorkflow workflow) =>
                    workflow.RunAsync(input),
                new(
                    id: workflowId,
                    taskQueue: TemporalNames.TaskQueue)
                {
                    IdReusePolicy =
                        WorkflowIdReusePolicy.RejectDuplicate
                });

            return new StartOrderWorkflowResult(
                workflowId,
                AlreadyExisted: false);
        }
        catch (WorkflowAlreadyStartedException exception)
            when (string.Equals(
                    exception.WorkflowId,
                    workflowId,
                    StringComparison.Ordinal) &&
                  string.Equals(
                    exception.WorkflowType,
                    nameof(OrderWorkflow),
                    StringComparison.Ordinal))
        {
            return new StartOrderWorkflowResult(
                workflowId,
                AlreadyExisted: true);
        }
        catch (WorkflowAlreadyStartedException exception)
        {
            throw new OrderWorkflowConflictException(
                "El identificador ya pertenece a otro proceso.",
                exception);
        }
        catch (RpcException exception)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible comunicarse con Temporal.",
                exception);
        }
    }

    public async Task<bool> ExistsAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId =
            TemporalNames.OrderWorkflowId(orderId);

        try
        {
            var handle =
                temporalClient.GetWorkflowHandle(workflowId);

            await handle.DescribeAsync();

            return true;
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.NotFound)
        {
            return false;
        }
        catch (RpcException exception)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible consultar Temporal.",
                exception);
        }
    }

    public async Task<OrderRuntimeStatus> GetRuntimeStatusAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId =
            TemporalNames.OrderWorkflowId(orderId);

        try
        {
            var handle =
                temporalClient.GetWorkflowHandle<OrderWorkflow>(
                    workflowId);

            return await handle.QueryAsync(
                workflow => workflow.GetRuntimeStatus());
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.NotFound)
        {
            throw new OrderWorkflowNotFoundException(
                "Temporal no conoce un pedido con ese identificador.",
                exception);
        }
        catch (Exception exception)
            when (exception is RpcException or
                  WorkflowQueryFailedException)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible consultar el estado interno del pedido.",
                exception);
        }
    }

    public async Task SignalPaymentConfirmedAsync(
        Guid orderId,
        PaymentConfirmedSignal signal,
        CancellationToken cancellationToken = default)
    {
        await SignalAsync(
            orderId,
            workflow =>
                workflow.PaymentConfirmedAsync(signal),
            cancellationToken);
    }

    public async Task SignalPackingCompletedAsync(
        Guid orderId,
        PackingCompletedSignal signal,
        CancellationToken cancellationToken = default)
    {
        await SignalAsync(
            orderId,
            workflow =>
                workflow.PackingCompletedAsync(signal),
            cancellationToken);
    }

    public async Task<ChangeAddressUpdateResult>
        ChangeDeliveryAddressAsync(
            Guid orderId,
            ChangeAddressUpdate update,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId =
            TemporalNames.OrderWorkflowId(orderId);

        try
        {
            var handle =
                temporalClient.GetWorkflowHandle<OrderWorkflow>(
                    workflowId);

            return await handle.ExecuteUpdateAsync(
                workflow =>
                    workflow.ChangeDeliveryAddressAsync(update),
                new WorkflowUpdateOptions
                {
                    Id = update.OperationId.ToString("D"),
                    Rpc = new()
                    {
                        CancellationToken = cancellationToken
                    }
                });
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.NotFound)
        {
            throw new OrderWorkflowNotFoundException(
                "Temporal no conoce un pedido activo con ese identificador.",
                exception);
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.FailedPrecondition)
        {
            throw new OrderWorkflowConflictException(
                "El pedido ya terminó y no admite cambiar la dirección.",
                exception);
        }
        catch (WorkflowUpdateFailedException exception)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible completar el cambio de dirección.",
                exception);
        }
        catch (RpcException exception)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible enviar el cambio de dirección a Temporal.",
                exception);
        }
    }

    public async Task<CancelOrderUpdateResult> CancelOrderAsync(
        Guid orderId,
        CancelOrderUpdate update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId =
            TemporalNames.OrderWorkflowId(orderId);

        var handle =
            temporalClient.GetWorkflowHandle<OrderWorkflow>(
                workflowId);

        try
        {
            return await handle.ExecuteUpdateAsync(
                workflow => workflow.CancelOrderAsync(update),
                new WorkflowUpdateOptions
                {
                    Id = update.OperationId.ToString("D"),
                    Rpc = new()
                    {
                        CancellationToken = cancellationToken
                    }
                });
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.NotFound)
        {
            throw new OrderWorkflowNotFoundException(
                "Temporal no conoce un pedido activo con ese identificador.",
                exception);
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.FailedPrecondition)
        {
            try
            {
                return await handle
                    .GetUpdateHandle<CancelOrderUpdateResult>(
                        update.OperationId.ToString("D"))
                    .GetResultAsync(new()
                    {
                        CancellationToken = cancellationToken
                    });
            }
            catch (RpcException retrievalException)
                when (retrievalException.Code is
                      RpcException.StatusCode.NotFound or
                      RpcException.StatusCode.FailedPrecondition)
            {
                throw new OrderWorkflowConflictException(
                    "El pedido ya terminó y no admite cancelación.",
                    exception);
            }
        }
        catch (WorkflowUpdateFailedException exception)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible completar la cancelación.",
                exception);
        }
        catch (RpcException exception)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible enviar la cancelación a Temporal.",
                exception);
        }
    }

    private async Task SignalAsync(
        Guid orderId,
        System.Linq.Expressions.Expression<
            Func<OrderWorkflow, Task>> signal,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId =
            TemporalNames.OrderWorkflowId(orderId);

        try
        {
            var handle =
                temporalClient.GetWorkflowHandle<OrderWorkflow>(
                    workflowId);

            await handle.SignalAsync(signal);
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.NotFound)
        {
            throw new OrderWorkflowNotFoundException(
                "Temporal no conoce un pedido activo con ese identificador.",
                exception);
        }
        catch (RpcException exception)
            when (exception.Code ==
                  RpcException.StatusCode.FailedPrecondition)
        {
            throw new OrderWorkflowConflictException(
                "El pedido ya terminó y no admite este evento.",
                exception);
        }
        catch (RpcException exception)
        {
            throw new OrderWorkflowUnavailableException(
                "No fue posible enviar el evento a Temporal.",
                exception);
        }
    }
}

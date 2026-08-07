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
}

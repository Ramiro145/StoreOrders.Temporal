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
}

public sealed record StartOrderWorkflowResult(
    string WorkflowId,
    bool AlreadyExisted);

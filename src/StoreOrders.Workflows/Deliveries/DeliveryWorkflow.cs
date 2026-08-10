using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Workflows.Activities;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Deliveries.Contracts;
using StoreOrders.Workflows.Orders;
using StoreOrders.Workflows.Orders.Contracts;
using Temporalio.Workflows;

namespace StoreOrders.Workflows.Deliveries;

[Workflow]
public sealed class DeliveryWorkflow
{
    private readonly Guid orderId;
    private readonly string parentWorkflowId;
    private readonly Queue<ShipmentShippedSignal> pendingShipments = [];
    private readonly Queue<ShipmentDeliveredSignal> pendingDeliveries = [];
    private readonly HashSet<Guid> receivedShipmentEventIds = [];
    private readonly HashSet<Guid> receivedDeliveryEventIds = [];

    private bool cancelRequested;
    private bool shipped;
    private bool isTerminal;

    [WorkflowInit]
    public DeliveryWorkflow(StartDeliveryInput input)
    {
        orderId = input.OrderId;
        parentWorkflowId = input.ParentWorkflowId;

        if (input.OrderId == Guid.Empty ||
            !string.Equals(
                input.ParentWorkflowId,
                TemporalNames.OrderWorkflowId(input.OrderId),
                StringComparison.Ordinal) ||
            !string.Equals(
                input.DeliveryWorkflowId,
                TemporalNames.DeliveryWorkflowId(input.OrderId),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La identidad inicial del Workflow de entrega " +
                "no es válida.");
        }
    }

    [WorkflowRun]
    public async Task<DeliveryWorkflowResult> RunAsync(
        StartDeliveryInput input)
    {
        var creationResult = await Workflow.ExecuteActivityAsync(
            (OrderActivities activities) =>
                activities.CreateShipmentAsync(
                    input.ToCreateShipmentInput()),
            ActivityOptionsFactory.CreateDefault());

        if (creationResult.Outcome ==
                CreateShipmentOutcome.OrderNotReady)
        {
            if (creationResult.OrderStatus == OrderStatus.Cancelled)
            {
                await Workflow.WaitConditionAsync(
                    () => cancelRequested);

                return CompleteCancelled();
            }

            throw new InvalidOperationException(
                $"El pedido está en estado " +
                $"{creationResult.OrderStatus} y no puede iniciar " +
                "la entrega.");
        }

        while (!shipped)
        {
            await Workflow.WaitConditionAsync(
                () => pendingShipments.Count > 0 ||
                      cancelRequested);

            if (cancelRequested)
            {
                return CompleteCancelled();
            }

            var signal = pendingShipments.Dequeue();

            var result = await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.MarkShipmentShippedAsync(
                        signal.ToInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

            if (result.Outcome is
                MarkShipmentShippedOutcome.Shipped or
                MarkShipmentShippedOutcome.AlreadyShipped)
            {
                shipped = true;

                await NotifyParentAsync(ShipmentStatus.Shipped);
                break;
            }

            if (result.OrderStatus == OrderStatus.Cancelled)
            {
                await Workflow.WaitConditionAsync(
                    () => cancelRequested);

                return CompleteCancelled();
            }
        }

        while (true)
        {
            await Workflow.WaitConditionAsync(
                () => pendingDeliveries.Count > 0);

            var signal = pendingDeliveries.Dequeue();

            var result = await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.MarkShipmentDeliveredAsync(
                        signal.ToInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

            if (result.Outcome is
                MarkShipmentDeliveredOutcome.Delivered or
                MarkShipmentDeliveredOutcome.AlreadyDelivered)
            {
                isTerminal = true;

                await NotifyParentAsync(ShipmentStatus.Delivered);
                await Workflow.WaitConditionAsync(
                    () => Workflow.AllHandlersFinished);

                return new DeliveryWorkflowResult(
                    orderId,
                    ShipmentStatus.Delivered,
                    "El paquete fue entregado.");
            }
        }
    }

    [WorkflowSignal(TemporalNames.ShipmentShippedSignal)]
    public Task ShipmentShippedAsync(
        ShipmentShippedSignal signal)
    {
        if (!isTerminal &&
            signal.EventId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(signal.Carrier) &&
            !string.IsNullOrWhiteSpace(signal.TrackingNumber) &&
            signal.ShippedAtUtc != default &&
            receivedShipmentEventIds.Add(signal.EventId))
        {
            pendingShipments.Enqueue(signal);
        }

        return Task.CompletedTask;
    }

    [WorkflowSignal(TemporalNames.ShipmentDeliveredSignal)]
    public Task ShipmentDeliveredAsync(
        ShipmentDeliveredSignal signal)
    {
        if (!isTerminal &&
            signal.EventId != Guid.Empty &&
            signal.DeliveredAtUtc != default &&
            receivedDeliveryEventIds.Add(signal.EventId))
        {
            pendingDeliveries.Enqueue(signal);
        }

        return Task.CompletedTask;
    }

    [WorkflowSignal(TemporalNames.CancelDeliverySignal)]
    public Task CancelDeliveryAsync(CancelDeliverySignal signal)
    {
        if (!isTerminal &&
            !shipped &&
            signal.OperationId != Guid.Empty)
        {
            cancelRequested = true;
        }

        return Task.CompletedTask;
    }

    private async Task NotifyParentAsync(ShipmentStatus status)
    {
        var parent =
            Workflow.GetExternalWorkflowHandle<OrderWorkflow>(
                parentWorkflowId);

        await parent.SignalAsync(
            workflow => workflow.DeliveryProgressChangedAsync(
                new DeliveryProgressSignal(orderId, status)));
    }

    private DeliveryWorkflowResult CompleteCancelled()
    {
        isTerminal = true;

        return new DeliveryWorkflowResult(
            orderId,
            ShipmentStatus.Cancelled,
            "La entrega fue cancelada antes del envío.");
    }
}

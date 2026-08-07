using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Workflows.Activities;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Orders.Contracts;
using Temporalio.Workflows;

namespace StoreOrders.Workflows.Orders;

[Workflow]
public sealed class OrderWorkflow
{
    [WorkflowRun]
    public async Task<OrderWorkflowResult> RunAsync(
        StartOrderInput input)
    {
        var expectedWorkflowId =
            TemporalNames.OrderWorkflowId(input.OrderId);

        if (!string.Equals(
                input.TemporalWorkflowId,
                expectedWorkflowId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "TemporalWorkflowId no corresponde con OrderId.");
        }

        await Workflow.ExecuteActivityAsync(
            (OrderActivities activities) =>
                activities.CreateOrderAsync(
                    input.ToCreateOrderInput()),
            ActivityOptionsFactory.CreateDefault());

        var reservationResult =
            await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.ReserveInventoryAsync(
                        new ReserveInventoryInput(input.OrderId)),
                ActivityOptionsFactory.CreateDefault());

        return reservationResult.Outcome switch
        {
            ReserveInventoryOutcome.Reserved =>
                CreateAwaitingPaymentResult(input.OrderId),

            ReserveInventoryOutcome.AlreadyReserved =>
                CreateAwaitingPaymentResult(input.OrderId),

            ReserveInventoryOutcome.InsufficientInventory =>
                new OrderWorkflowResult(
                    input.OrderId,
                    OrderStatus.Rejected,
                    "Pedido rechazado por inventario insuficiente."),

            _ => throw new InvalidOperationException(
                $"Resultado de reserva desconocido: " +
                $"{reservationResult.Outcome}.")
        };
    }

    private static OrderWorkflowResult CreateAwaitingPaymentResult(
        Guid orderId)
    {
        return new OrderWorkflowResult(
            orderId,
            OrderStatus.AwaitingPayment,
            "Pedido creado y con inventario reservado.");
    }
}

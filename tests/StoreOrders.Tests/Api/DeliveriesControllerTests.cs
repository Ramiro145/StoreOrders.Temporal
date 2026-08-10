using Microsoft.AspNetCore.Mvc;
using StoreOrders.Api.Contracts.Deliveries;
using StoreOrders.Api.Controllers;
using StoreOrders.Api.Services;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Deliveries.Contracts;

namespace StoreOrders.Tests.Api;

public sealed class DeliveriesControllerTests
{
    [Fact]
    public async Task ShipmentShippedAsync_Accepted_ReturnsAccepted()
    {
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var controller = new DeliveriesController(
            new StaticDeliveryWorkflowGateway());

        var action = await controller.ShipmentShippedAsync(
            orderId,
            new ShipmentShippedRequest(
                eventId,
                "Paquetería Demo",
                "TRACK-0001001",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var accepted =
            Assert.IsType<AcceptedResult>(action.Result);

        var response =
            Assert.IsType<DeliveryEventAcceptedResponse>(
                accepted.Value);

        Assert.Equal(orderId, response.OrderId);
        Assert.Equal(eventId, response.EventId);
        Assert.Equal(
            TemporalNames.DeliveryWorkflowId(orderId),
            response.DeliveryWorkflowId);
        Assert.Equal("Accepted", response.EventStatus);
    }

    [Fact]
    public async Task ShipmentDeliveredAsync_Accepted_ReturnsAccepted()
    {
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var controller = new DeliveriesController(
            new StaticDeliveryWorkflowGateway());

        var action = await controller.ShipmentDeliveredAsync(
            orderId,
            new ShipmentDeliveredRequest(
                eventId,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var accepted =
            Assert.IsType<AcceptedResult>(action.Result);

        var response =
            Assert.IsType<DeliveryEventAcceptedResponse>(
                accepted.Value);

        Assert.Equal(orderId, response.OrderId);
        Assert.Equal(eventId, response.EventId);
        Assert.Equal("Accepted", response.EventStatus);
    }

    [Fact]
    public async Task ShipmentShippedAsync_NotReady_ReturnsConflict()
    {
        var orderId = Guid.NewGuid();
        var controller = new DeliveriesController(
            new StaticDeliveryWorkflowGateway(notReady: true));

        var action = await controller.ShipmentShippedAsync(
            orderId,
            new ShipmentShippedRequest(
                Guid.NewGuid(),
                "Paquetería Demo",
                "TRACK-0001002",
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var conflict =
            Assert.IsType<ConflictObjectResult>(action.Result);

        var problem =
            Assert.IsType<ProblemDetails>(conflict.Value);

        Assert.Equal(409, problem.Status);
        Assert.Equal(
            "delivery_not_ready",
            problem.Extensions["code"]);
        Assert.Equal(orderId, problem.Extensions["orderId"]);
    }

    private sealed class StaticDeliveryWorkflowGateway(
        bool notReady = false)
        : IDeliveryWorkflowGateway
    {
        public Task SignalShippedAsync(
            Guid orderId,
            ShipmentShippedSignal signal,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNotReady();
            return Task.CompletedTask;
        }

        public Task SignalDeliveredAsync(
            Guid orderId,
            ShipmentDeliveredSignal signal,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNotReady();
            return Task.CompletedTask;
        }

        private void ThrowIfNotReady()
        {
            if (notReady)
            {
                throw new DeliveryWorkflowNotReadyException(
                    "El proceso de entrega todavía no está disponible.",
                    new InvalidOperationException());
            }
        }
    }
}

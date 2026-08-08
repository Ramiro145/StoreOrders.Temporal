using Microsoft.AspNetCore.Mvc;
using StoreOrders.Api.Contracts.Orders;
using StoreOrders.Api.Controllers;
using StoreOrders.Api.Services;
using StoreOrders.Domain.Abstractions;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.ReadModels;
using StoreOrders.Workflows.Orders.Contracts;

namespace StoreOrders.Tests.Api;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task ChangeDeliveryAddressAsync_Accepted_ReturnsOk()
    {
        var orderId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var controller = CreateController(
            new ChangeAddressUpdateResult(
                operationId,
                orderId,
                Accepted: true,
                AddressVersion: 2,
                "La dirección fue actualizada."));

        var action = await controller.ChangeDeliveryAddressAsync(
            orderId,
            CreateRequest(operationId),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response =
            Assert.IsType<ChangeDeliveryAddressResponse>(ok.Value);

        Assert.True(response.Accepted);
        Assert.Equal(orderId, response.OrderId);
        Assert.Equal(operationId, response.OperationId);
        Assert.Equal(2, response.AddressVersion);
    }

    [Fact]
    public async Task ChangeDeliveryAddressAsync_NotAllowed_ReturnsConflict()
    {
        var orderId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var controller = CreateController(
            new ChangeAddressUpdateResult(
                operationId,
                orderId,
                Accepted: false,
                AddressVersion: 2,
                "El pedido ya fue enviado."));

        var action = await controller.ChangeDeliveryAddressAsync(
            orderId,
            CreateRequest(operationId),
            CancellationToken.None);

        var conflict =
            Assert.IsType<ConflictObjectResult>(action.Result);

        var problem =
            Assert.IsType<ProblemDetails>(conflict.Value);

        Assert.Equal(409, problem.Status);
        Assert.Equal(
            "https://storeorders.local/problems/" +
            "address-change-not-allowed",
            problem.Type);
        Assert.Equal(
            "address_change_not_allowed",
            problem.Extensions["code"]);
        Assert.Equal(orderId, problem.Extensions["orderId"]);
    }

    [Fact]
    public async Task CancelOrderAsync_Accepted_ReturnsOk()
    {
        var orderId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var controller = CreateController(
            new CancelOrderUpdateResult(
                operationId,
                orderId,
                Accepted: true,
                OrderStatus.AwaitingPayment,
                OrderStatus.Cancelled,
                ReleasedReservationCount: 2,
                "El pedido fue cancelado y sus reservaciones " +
                "fueron liberadas."));

        var action = await controller.CancelOrderAsync(
            orderId,
            CreateCancelRequest(operationId),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<CancelOrderResponse>(ok.Value);

        Assert.True(response.Accepted);
        Assert.Equal(orderId, response.OrderId);
        Assert.Equal(operationId, response.OperationId);
        Assert.Equal("AwaitingPayment", response.PreviousStatus);
        Assert.Equal("Cancelled", response.CurrentStatus);
        Assert.Equal(2, response.ReleasedReservationCount);
    }

    [Fact]
    public async Task CancelOrderAsync_NotAllowed_ReturnsConflict()
    {
        var orderId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var controller = CreateController(
            new CancelOrderUpdateResult(
                operationId,
                orderId,
                Accepted: false,
                OrderStatus.Shipped,
                OrderStatus.Shipped,
                ReleasedReservationCount: 0,
                "El pedido ya fue enviado."));

        var action = await controller.CancelOrderAsync(
            orderId,
            CreateCancelRequest(operationId),
            CancellationToken.None);

        var conflict =
            Assert.IsType<ConflictObjectResult>(action.Result);

        var problem =
            Assert.IsType<ProblemDetails>(conflict.Value);

        Assert.Equal(409, problem.Status);
        Assert.Equal(
            "https://storeorders.local/problems/" +
            "cancellation-not-allowed",
            problem.Type);
        Assert.Equal(
            "cancellation_not_allowed",
            problem.Extensions["code"]);
        Assert.Equal(orderId, problem.Extensions["orderId"]);
    }

    private static OrdersController CreateController(
        ChangeAddressUpdateResult result)
    {
        return new OrdersController(
            new UnusedOrderReadService(),
            new StaticWorkflowGateway(result));
    }

    private static OrdersController CreateController(
        CancelOrderUpdateResult result)
    {
        return new OrdersController(
            new UnusedOrderReadService(),
            new StaticWorkflowGateway(cancelResult: result));
    }

    private static ChangeDeliveryAddressRequest CreateRequest(
        Guid operationId)
    {
        return new ChangeDeliveryAddressRequest(
            operationId,
            "Ramiro González",
            "Av. Nueva 450",
            "Interior 2",
            "San Nicolás de los Garza",
            "Nuevo León",
            "66400",
            "MX");
    }

    private static CancelOrderRequest CreateCancelRequest(
        Guid operationId)
    {
        return new CancelOrderRequest(
            operationId,
            "El cliente capturó productos incorrectos.",
            "customer");
    }

    private sealed class UnusedOrderReadService
        : IOrderReadService
    {
        public Task<OrderReadModel?> GetByIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StaticWorkflowGateway(
        ChangeAddressUpdateResult? addressResult = null,
        CancelOrderUpdateResult? cancelResult = null)
        : IOrderWorkflowGateway
    {
        public Task<ChangeAddressUpdateResult>
            ChangeDeliveryAddressAsync(
                Guid orderId,
                ChangeAddressUpdate update,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                addressResult ??
                throw new NotSupportedException());
        }

        public Task<CancelOrderUpdateResult> CancelOrderAsync(
            Guid orderId,
            CancelOrderUpdate update,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                cancelResult ??
                throw new NotSupportedException());
        }

        public Task<StartOrderWorkflowResult> StartAsync(
            StartOrderInput input,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<bool> ExistsAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OrderRuntimeStatus> GetRuntimeStatusAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SignalPaymentConfirmedAsync(
            Guid orderId,
            PaymentConfirmedSignal signal,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SignalPackingCompletedAsync(
            Guid orderId,
            PackingCompletedSignal signal,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

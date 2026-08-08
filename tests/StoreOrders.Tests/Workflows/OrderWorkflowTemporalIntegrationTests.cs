using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Infrastructure.Persistence;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Orders;
using StoreOrders.Workflows.Orders.Contracts;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace StoreOrders.Tests.Workflows;

[Collection(IntegrationTestCollection.Name)]
public sealed class OrderWorkflowTemporalIntegrationTests
{
    [Fact]
    public async Task OrderWorkflow_ProcessesQueriesEarlyAndDuplicateSignals()
    {
        var successfulOrderId = Guid.NewGuid();
        var rejectedOrderId = Guid.NewGuid();

        Guid[] testOrderIds =
        [
            successfulOrderId,
            rejectedOrderId
        ];

        await using (var setupDbContext = CreateDbContext())
        {
            await setupDbContext.Database.MigrateAsync();
            await CleanupAsync(setupDbContext, testOrderIds);
        }

        ITemporalClient? client = null;

        try
        {
            Dictionary<int, StockSnapshot> stockBefore;
            decimal successfulTotal;

            await using (var snapshotDbContext = CreateDbContext())
            {
                stockBefore = await snapshotDbContext.InventoryStocks
                    .AsNoTracking()
                    .Where(stock =>
                        stock.ProductId == 1 ||
                        stock.ProductId == 2 ||
                        stock.ProductId == 3)
                    .ToDictionaryAsync(
                        stock => stock.ProductId,
                        stock => new StockSnapshot(
                            stock.AvailableQuantity,
                            stock.ReservedQuantity));

                successfulTotal = await snapshotDbContext.Products
                    .Where(product => product.ProductId == 2)
                    .Select(product => product.CurrentPrice)
                    .SingleAsync();
            }

            Assert.True(stockBefore[2].AvailableQuantity >= 1);

            var unavailableQuantity =
                checked(stockBefore[3].AvailableQuantity + 1);

            var successfulInput = CreateInput(
                successfulOrderId,
                [
                    new CreateOrderItemInput(2, 1)
                ]);

            var rejectedInput = CreateInput(
                rejectedOrderId,
                [
                    new CreateOrderItemInput(1, 1),
                    new CreateOrderItemInput(
                        3,
                        unavailableQuantity)
                ]);

            client = await TemporalClient.ConnectAsync(
                new("localhost:7233")
                {
                    Namespace = "default"
                });

            var successfulHandle =
                await client.StartWorkflowAsync(
                    (OrderWorkflow workflow) =>
                        workflow.RunAsync(successfulInput),
                    new(
                        id: TemporalNames.OrderWorkflowId(
                            successfulOrderId),
                        taskQueue: TemporalNames.TaskQueue));

            var addressOperationId = Guid.NewGuid();

            var addressUpdate = new ChangeAddressUpdate(
                addressOperationId,
                "Ramiro González",
                "Av. Nueva 450",
                "Interior 2",
                "San Nicolás de los Garza",
                "Nuevo León",
                "66400",
                "MX");

            var addressResult =
                await successfulHandle.ExecuteUpdateAsync(
                    workflow =>
                        workflow.ChangeDeliveryAddressAsync(
                            addressUpdate),
                    new WorkflowUpdateOptions
                    {
                        Id = addressOperationId.ToString("D")
                    });

            var duplicateAddressResult =
                await successfulHandle.ExecuteUpdateAsync(
                    workflow =>
                        workflow.ChangeDeliveryAddressAsync(
                            addressUpdate),
                    new WorkflowUpdateOptions
                    {
                        Id = addressOperationId.ToString("D")
                    });

            Assert.True(addressResult.Accepted);
            Assert.Equal(2, addressResult.AddressVersion);
            Assert.Equal(addressResult, duplicateAddressResult);

            var invalidAddressUpdate = addressUpdate with
            {
                OperationId = Guid.Empty
            };

            await Assert.ThrowsAsync<WorkflowUpdateFailedException>(
                () => successfulHandle.ExecuteUpdateAsync(
                    workflow =>
                        workflow.ChangeDeliveryAddressAsync(
                            invalidAddressUpdate),
                    new WorkflowUpdateOptions
                    {
                        Id = Guid.NewGuid().ToString("D")
                    }));

            var packingSignal = new PackingCompletedSignal(
                Guid.NewGuid(),
                "warehouse-test-user",
                DateTime.UtcNow);

            // Preparación anticipada y duplicada: debe permanecer en cola.
            await successfulHandle.SignalAsync(
                workflow =>
                    workflow.PackingCompletedAsync(packingSignal));

            await successfulHandle.SignalAsync(
                workflow =>
                    workflow.PackingCompletedAsync(packingSignal));

            var rejectedPaymentSignal =
                new PaymentConfirmedSignal(
                    Guid.NewGuid(),
                    $"PAY-REJECTED-{Guid.NewGuid():N}",
                    successfulTotal + 1,
                    "MXN",
                    DateTime.UtcNow);

            await successfulHandle.SignalAsync(
                workflow =>
                    workflow.PaymentConfirmedAsync(
                        rejectedPaymentSignal));

            var rejectedPaymentOperationKey =
                $"order:{successfulOrderId:D}:payment:" +
                $"{rejectedPaymentSignal.EventId:D}";

            await WaitForHistoryAsync(
                successfulOrderId,
                rejectedPaymentOperationKey);

            var awaitingPaymentStatus = await WaitForStageAsync(
                successfulHandle,
                OrderWorkflowStage.AwaitingPayment);

            Assert.False(awaitingPaymentStatus.PaymentReceived);

            var validPaymentSignal =
                new PaymentConfirmedSignal(
                    Guid.NewGuid(),
                    $"PAY-{Guid.NewGuid():N}",
                    successfulTotal,
                    "MXN",
                    DateTime.UtcNow);

            // Pago válido y duplicado: SQL debe observar un solo efecto.
            await successfulHandle.SignalAsync(
                workflow =>
                    workflow.PaymentConfirmedAsync(
                        validPaymentSignal));

            await successfulHandle.SignalAsync(
                workflow =>
                    workflow.PaymentConfirmedAsync(
                        validPaymentSignal));

            var readyStatus = await WaitForStageAsync(
                successfulHandle,
                OrderWorkflowStage.ReadyForShipment);

            Assert.True(readyStatus.PaymentReceived);
            Assert.True(readyStatus.PackingCompleted);
            Assert.False(readyStatus.DeliveryStarted);
            Assert.True(readyStatus.CanChangeAddress);
            Assert.True(readyStatus.CanCancel);
            Assert.Equal(
                OrderWorkflowWaitingFor.ShipmentShipped,
                readyStatus.WaitingFor);

            var cancelOperationId = Guid.NewGuid();
            var cancelUpdate = new CancelOrderUpdate(
                cancelOperationId,
                "El cliente capturó productos incorrectos.",
                "customer");

            var cancelResult =
                await successfulHandle.ExecuteUpdateAsync(
                    workflow =>
                        workflow.CancelOrderAsync(cancelUpdate),
                    new WorkflowUpdateOptions
                    {
                        Id = cancelOperationId.ToString("D")
                    });

            var duplicateCancelResult =
                await successfulHandle
                    .GetUpdateHandle<CancelOrderUpdateResult>(
                        cancelOperationId.ToString("D"))
                    .GetResultAsync();

            Assert.True(cancelResult.Accepted);
            Assert.Equal(
                OrderStatus.ReadyForShipment,
                cancelResult.PreviousStatus);
            Assert.Equal(
                OrderStatus.Cancelled,
                cancelResult.CurrentStatus);
            Assert.Equal(1, cancelResult.ReleasedReservationCount);
            Assert.Equal(cancelResult, duplicateCancelResult);

            var successfulResult =
                await successfulHandle.GetResultAsync();

            Assert.Equal(
                OrderStatus.Cancelled,
                successfulResult.Status);

            var rejectedResult =
                await client.ExecuteWorkflowAsync(
                    (OrderWorkflow workflow) =>
                        workflow.RunAsync(rejectedInput),
                    new(
                        id: TemporalNames.OrderWorkflowId(
                            rejectedOrderId),
                        taskQueue: TemporalNames.TaskQueue));

            Assert.Equal(
                OrderStatus.Rejected,
                rejectedResult.Status);

            await using var verificationDbContext =
                CreateDbContext();

            var successfulOrder =
                await verificationDbContext.Orders
                    .AsNoTracking()
                    .SingleAsync(
                        order =>
                            order.OrderId == successfulOrderId);

            var rejectedOrder =
                await verificationDbContext.Orders
                    .AsNoTracking()
                    .SingleAsync(
                        order =>
                            order.OrderId == rejectedOrderId);

            Assert.Equal(
                OrderStatus.Cancelled,
                successfulOrder.Status);

            Assert.Equal(
                OrderStatus.Rejected,
                rejectedOrder.Status);

            Assert.Equal(
                1,
                await verificationDbContext.Payments
                    .CountAsync(
                        payment =>
                            payment.OrderId ==
                            successfulOrderId));

            var fulfillment =
                await verificationDbContext.OrderFulfillments
                    .AsNoTracking()
                    .SingleAsync(
                        current =>
                            current.OrderId ==
                            successfulOrderId);

            Assert.Equal(
                FulfillmentStatus.Cancelled,
                fulfillment.Status);

            Assert.Equal(
                packingSignal.EventId,
                ExtractEventId(fulfillment.OperationKey));

            var reservation =
                await verificationDbContext.InventoryReservations
                    .AsNoTracking()
                    .SingleAsync(current =>
                        current.OrderItem.OrderId ==
                        successfulOrderId);

            Assert.Equal(
                ReservationStatus.Released,
                reservation.Status);
            Assert.NotNull(reservation.ReleasedAtUtc);

            Assert.Equal(
                8,
                await verificationDbContext.OrderHistory
                    .CountAsync(
                        entry =>
                            entry.OrderId ==
                            successfulOrderId));

            var updatedAddress =
                await verificationDbContext.OrderAddresses
                    .AsNoTracking()
                    .SingleAsync(
                        address =>
                            address.OrderId ==
                            successfulOrderId);

            Assert.Equal("Av. Nueva 450", updatedAddress.Line1);
            Assert.Equal("Interior 2", updatedAddress.Line2);
            Assert.Equal(2, updatedAddress.AddressVersion);

            Assert.Equal(
                2,
                await verificationDbContext.OrderHistory
                    .CountAsync(
                        entry =>
                            entry.OrderId ==
                            rejectedOrderId));

            var stockAfter =
                await verificationDbContext.InventoryStocks
                    .AsNoTracking()
                    .Where(stock =>
                        stock.ProductId == 1 ||
                        stock.ProductId == 2 ||
                        stock.ProductId == 3)
                    .ToDictionaryAsync(
                        stock => stock.ProductId,
                        stock => new StockSnapshot(
                            stock.AvailableQuantity,
                            stock.ReservedQuantity));

            Assert.Equal(
                stockBefore[2].AvailableQuantity,
                stockAfter[2].AvailableQuantity);

            Assert.Equal(
                stockBefore[2].ReservedQuantity,
                stockAfter[2].ReservedQuantity);

            Assert.Equal(stockBefore[1], stockAfter[1]);
            Assert.Equal(stockBefore[3], stockAfter[3]);
        }
        finally
        {
            if (client is not null)
            {
                try
                {
                    await client
                        .GetWorkflowHandle(
                            TemporalNames.OrderWorkflowId(
                                successfulOrderId))
                        .TerminateAsync(
                            "Limpieza de la prueba de integración.");
                }
                catch (RpcException exception)
                    when (exception.Code is
                          RpcException.StatusCode.NotFound or
                          RpcException.StatusCode.FailedPrecondition)
                {
                    // La ejecución no alcanzó a iniciar o ya no existe.
                }
            }

            await using var cleanupDbContext = CreateDbContext();

            await CleanupAsync(
                cleanupDbContext,
                testOrderIds);
        }
    }

    private static async Task<OrderRuntimeStatus> WaitForStageAsync(
        WorkflowHandle<OrderWorkflow, OrderWorkflowResult> handle,
        OrderWorkflowStage expectedStage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var status = await handle.QueryAsync(
                workflow => workflow.GetRuntimeStatus());

            if (status.Stage == expectedStage)
            {
                return status;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"El Workflow no alcanzó la etapa {expectedStage}.");
    }

    private static async Task WaitForHistoryAsync(
        Guid orderId,
        string operationKey)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            await using var dbContext = CreateDbContext();

            if (await dbContext.OrderHistory
                    .AsNoTracking()
                    .AnyAsync(
                        entry =>
                            entry.OrderId == orderId &&
                            entry.OperationKey ==
                            operationKey))
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            "No se registró el evento de pago rechazado.");
    }

    private static Guid ExtractEventId(string? operationKey)
    {
        Assert.False(string.IsNullOrWhiteSpace(operationKey));

        return Guid.Parse(
            operationKey!.Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Last());
    }

    private static StartOrderInput CreateInput(
        Guid orderId,
        CreateOrderItemInput[] items)
    {
        return new StartOrderInput(
            orderId,
            orderId.ToString("D"),
            TemporalNames.OrderWorkflowId(orderId),
            "Ramiro González",
            "ramiro@example.com",
            new CreateOrderAddressInput(
                "Ramiro González",
                "Av. Ejemplo 123",
                null,
                "Monterrey",
                "Nuevo León",
                "64000",
                "MX"),
            items);
    }

    private static StoreOrdersDbContext CreateDbContext()
    {
        var password =
            Environment.GetEnvironmentVariable(
                "STOREORDERS_SQL_PASSWORD")
            ?? throw new InvalidOperationException(
                "STOREORDERS_SQL_PASSWORD no está definida.");

        var port =
            Environment.GetEnvironmentVariable(
                "STOREORDERS_SQL_PORT")
            ?? "14330";

        var connectionString =
            new SqlConnectionStringBuilder
            {
                DataSource = $"localhost,{port}",
                InitialCatalog = "StoreOrdersDb",
                UserID = "sa",
                Password = password,
                Encrypt = true,
                TrustServerCertificate = true
            }.ConnectionString;

        var options =
            new DbContextOptionsBuilder<StoreOrdersDbContext>()
                .UseSqlServer(connectionString)
                .Options;

        return new StoreOrdersDbContext(options);
    }

    private static async Task CleanupAsync(
        StoreOrdersDbContext dbContext,
        Guid[] orderIds)
    {
        var activeReservations =
            await dbContext.InventoryReservations
                .Where(reservation =>
                    orderIds.Contains(
                        reservation.OrderItem.OrderId) &&
                    reservation.Status ==
                        ReservationStatus.Active)
                .Select(reservation => new
                {
                    reservation.OrderItem.ProductId,
                    reservation.Quantity
                })
                .ToListAsync();

        foreach (var reservation in activeReservations)
        {
            await dbContext.InventoryStocks
                .Where(stock =>
                    stock.ProductId ==
                    reservation.ProductId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            stock =>
                                stock.AvailableQuantity,
                            stock =>
                                stock.AvailableQuantity +
                                reservation.Quantity)
                        .SetProperty(
                            stock =>
                                stock.ReservedQuantity,
                            stock =>
                                stock.ReservedQuantity -
                                reservation.Quantity)
                        .SetProperty(
                            stock => stock.UpdatedAtUtc,
                            DateTime.UtcNow));
        }

        await dbContext.Payments
            .Where(payment =>
                orderIds.Contains(payment.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.OrderFulfillments
            .Where(fulfillment =>
                orderIds.Contains(fulfillment.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.InventoryReservations
            .Where(reservation =>
                orderIds.Contains(
                    reservation.OrderItem.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.OrderHistory
            .Where(entry =>
                orderIds.Contains(entry.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.OrderItems
            .Where(item =>
                orderIds.Contains(item.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.OrderAddresses
            .Where(address =>
                orderIds.Contains(address.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.Orders
            .Where(order =>
                orderIds.Contains(order.OrderId))
            .ExecuteDeleteAsync();
    }

    private sealed record StockSnapshot(
        int AvailableQuantity,
        int ReservedQuantity);
}

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Infrastructure.Persistence;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Orders;
using StoreOrders.Workflows.Orders.Contracts;
using Temporalio.Client;

namespace StoreOrders.Tests.Workflows;

public sealed class OrderWorkflowTemporalIntegrationTests
{
    [Fact]
    public async Task OrderWorkflow_ExecutesSuccessfulAndRejectedPaths()
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

        try
        {
            Dictionary<int, StockSnapshot> stockBefore;

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

            var client = await TemporalClient.ConnectAsync(
                new("localhost:7233")
                {
                    Namespace = "default"
                });

            var successfulResult =
                await client.ExecuteWorkflowAsync(
                    (OrderWorkflow workflow) =>
                        workflow.RunAsync(successfulInput),
                    new(
                        id: TemporalNames.OrderWorkflowId(
                            successfulOrderId),
                        taskQueue: TemporalNames.TaskQueue));

            var rejectedResult =
                await client.ExecuteWorkflowAsync(
                    (OrderWorkflow workflow) =>
                        workflow.RunAsync(rejectedInput),
                    new(
                        id: TemporalNames.OrderWorkflowId(
                            rejectedOrderId),
                        taskQueue: TemporalNames.TaskQueue));

            Assert.Equal(
                OrderStatus.AwaitingPayment,
                successfulResult.Status);

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
                OrderStatus.AwaitingPayment,
                successfulOrder.Status);

            Assert.Equal(
                OrderStatus.Rejected,
                rejectedOrder.Status);

            var successfulReservationCount =
                await verificationDbContext
                    .InventoryReservations
                    .AsNoTracking()
                    .CountAsync(
                        reservation =>
                            reservation.OrderItem.OrderId ==
                            successfulOrderId &&
                            reservation.Status ==
                            ReservationStatus.Active);

            var rejectedReservationCount =
                await verificationDbContext
                    .InventoryReservations
                    .AsNoTracking()
                    .CountAsync(
                        reservation =>
                            reservation.OrderItem.OrderId ==
                            rejectedOrderId);

            Assert.Equal(1, successfulReservationCount);
            Assert.Equal(0, rejectedReservationCount);

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
                stockBefore[2].AvailableQuantity - 1,
                stockAfter[2].AvailableQuantity);

            Assert.Equal(
                stockBefore[2].ReservedQuantity + 1,
                stockAfter[2].ReservedQuantity);

            // El intento rechazado no debe afectar inventario.
            Assert.Equal(stockBefore[1], stockAfter[1]);
            Assert.Equal(stockBefore[3], stockAfter[3]);

            Assert.Equal(
                2,
                await verificationDbContext.OrderHistory
                    .CountAsync(
                        entry =>
                            entry.OrderId == successfulOrderId));

            Assert.Equal(
                2,
                await verificationDbContext.OrderHistory
                    .CountAsync(
                        entry =>
                            entry.OrderId == rejectedOrderId));
        }
        finally
        {
            await using var cleanupDbContext = CreateDbContext();

            await CleanupAsync(
                cleanupDbContext,
                testOrderIds);
        }
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

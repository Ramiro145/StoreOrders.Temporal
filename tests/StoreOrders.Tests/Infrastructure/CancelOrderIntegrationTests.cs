using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Infrastructure.Operations;
using StoreOrders.Infrastructure.Persistence;

namespace StoreOrders.Tests.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public sealed class CancelOrderIntegrationTests
{
    private static readonly Guid TestOrderId =
        Guid.Parse("50000000-0000-0000-0000-000000000005");

    [Fact]
    public async Task CancelOrderAsync_ReleasesInventory_IsIdempotent_AndRejectsTerminalStates()
    {
        await using (var setupDbContext = CreateDbContext())
        {
            await setupDbContext.Database.MigrateAsync();
            await DeleteTestOrderAsync(setupDbContext);
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
                        stock.ProductId == 2)
                    .ToDictionaryAsync(
                        stock => stock.ProductId,
                        stock => new StockSnapshot(
                            stock.AvailableQuantity,
                            stock.ReservedQuantity));
            }

            Assert.True(stockBefore[1].AvailableQuantity >= 1);
            Assert.True(stockBefore[2].AvailableQuantity >= 1);

            var createInput = new CreateOrderInput(
                TestOrderId,
                TestOrderId.ToString("D"),
                $"order-{TestOrderId:D}",
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
                [
                    new CreateOrderItemInput(1, 1),
                    new CreateOrderItemInput(2, 1)
                ]);

            await using (var createDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(createDbContext);

                await operation.CreateOrderAsync(createInput);
                await operation.ReserveInventoryAsync(
                    new ReserveInventoryInput(TestOrderId));
            }

            var operationId = Guid.NewGuid();
            var cancelInput = new CancelOrderInput(
                TestOrderId,
                operationId,
                "El cliente capturó productos incorrectos.",
                "customer");

            CancelOrderResult firstResult;

            await using (var firstDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(firstDbContext);

                firstResult =
                    await operation.CancelOrderAsync(cancelInput);
            }

            CancelOrderResult duplicateResult;

            await using (var duplicateDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(duplicateDbContext);

                duplicateResult =
                    await operation.CancelOrderAsync(cancelInput);
            }

            Assert.Equal(
                CancelOrderOutcome.Cancelled,
                firstResult.Outcome);
            Assert.Equal(
                OrderStatus.AwaitingPayment,
                firstResult.PreviousStatus);
            Assert.Equal(
                OrderStatus.Cancelled,
                firstResult.CurrentStatus);
            Assert.Equal(2, firstResult.ReleasedReservationCount);

            Assert.Equal(
                CancelOrderOutcome.AlreadyCancelled,
                duplicateResult.Outcome);
            Assert.Equal(
                firstResult.PreviousStatus,
                duplicateResult.PreviousStatus);
            Assert.Equal(
                firstResult.CurrentStatus,
                duplicateResult.CurrentStatus);
            Assert.Equal(
                firstResult.ReleasedReservationCount,
                duplicateResult.ReleasedReservationCount);

            CancelOrderResult anotherOperationResult;

            await using (var anotherDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(anotherDbContext);

                anotherOperationResult =
                    await operation.CancelOrderAsync(
                        cancelInput with
                        {
                            OperationId = Guid.NewGuid()
                        });
            }

            Assert.Equal(
                CancelOrderOutcome.AlreadyCancelled,
                anotherOperationResult.Outcome);
            Assert.Equal(
                OrderStatus.Cancelled,
                anotherOperationResult.PreviousStatus);
            Assert.Equal(0, anotherOperationResult.ReleasedReservationCount);

            await using (var verificationDbContext = CreateDbContext())
            {
                var order = await verificationDbContext.Orders
                    .AsNoTracking()
                    .SingleAsync(
                        current => current.OrderId == TestOrderId);

                Assert.Equal(OrderStatus.Cancelled, order.Status);
                Assert.NotNull(order.CancelledAtUtc);

                var reservations =
                    await verificationDbContext.InventoryReservations
                        .AsNoTracking()
                        .Where(reservation =>
                            reservation.OrderItem.OrderId ==
                            TestOrderId)
                        .ToArrayAsync();

                Assert.Equal(2, reservations.Length);
                Assert.All(
                    reservations,
                    reservation =>
                    {
                        Assert.Equal(
                            ReservationStatus.Released,
                            reservation.Status);
                        Assert.NotNull(reservation.ReleasedAtUtc);
                    });

                var stockAfter =
                    await verificationDbContext.InventoryStocks
                        .AsNoTracking()
                        .Where(stock =>
                            stock.ProductId == 1 ||
                            stock.ProductId == 2)
                        .ToDictionaryAsync(
                            stock => stock.ProductId,
                            stock => new StockSnapshot(
                                stock.AvailableQuantity,
                                stock.ReservedQuantity));

                Assert.Equal(stockBefore[1], stockAfter[1]);
                Assert.Equal(stockBefore[2], stockAfter[2]);

                var cancellationHistory =
                    await verificationDbContext.OrderHistory
                        .AsNoTracking()
                        .Where(entry =>
                            entry.OrderId == TestOrderId &&
                            entry.EventType == "OrderCancelled")
                        .ToArrayAsync();

                var cancelledEvent =
                    Assert.Single(cancellationHistory);

                Assert.Equal(
                    $"order:{TestOrderId:D}:cancel:{operationId:D}",
                    cancelledEvent.OperationKey);
                Assert.Equal(
                    OrderStatus.AwaitingPayment,
                    cancelledEvent.PreviousStatus);
                Assert.Equal(
                    OrderStatus.Cancelled,
                    cancelledEvent.CurrentStatus);
                Assert.Equal(
                    ActorType.Customer,
                    cancelledEvent.ActorType);
                Assert.DoesNotContain(
                    "RolledBack",
                    cancelledEvent.Description ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }

            var rejectedStates = new[]
            {
                OrderStatus.Shipped,
                OrderStatus.Delivered,
                OrderStatus.Rejected
            };

            foreach (var rejectedState in rejectedStates)
            {
                await using (var statusDbContext = CreateDbContext())
                {
                    await statusDbContext.Orders
                        .Where(order => order.OrderId == TestOrderId)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(
                                    order => order.Status,
                                    rejectedState)
                                .SetProperty(
                                    order => order.UpdatedAtUtc,
                                    DateTime.UtcNow));
                }

                await using var rejectedDbContext = CreateDbContext();
                var operation =
                    new EfOrderOperations(rejectedDbContext);

                var rejectedResult =
                    await operation.CancelOrderAsync(
                        cancelInput with
                        {
                            OperationId = Guid.NewGuid()
                        });

                Assert.Equal(
                    CancelOrderOutcome.NotAllowed,
                    rejectedResult.Outcome);
                Assert.Equal(
                    rejectedState,
                    rejectedResult.CurrentStatus);
                Assert.Equal(0, rejectedResult.ReleasedReservationCount);
            }
        }
        finally
        {
            await using var cleanupDbContext = CreateDbContext();
            await DeleteTestOrderAsync(cleanupDbContext);
        }
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

    private static async Task DeleteTestOrderAsync(
        StoreOrdersDbContext dbContext)
    {
        var activeReservations =
            await dbContext.InventoryReservations
                .Where(reservation =>
                    reservation.OrderItem.OrderId == TestOrderId &&
                    reservation.Status == ReservationStatus.Active)
                .Select(reservation => new
                {
                    reservation.OrderItem.ProductId,
                    reservation.Quantity
                })
                .ToArrayAsync();

        foreach (var reservation in activeReservations)
        {
            await dbContext.InventoryStocks
                .Where(stock =>
                    stock.ProductId == reservation.ProductId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            stock => stock.AvailableQuantity,
                            stock =>
                                stock.AvailableQuantity +
                                reservation.Quantity)
                        .SetProperty(
                            stock => stock.ReservedQuantity,
                            stock =>
                                stock.ReservedQuantity -
                                reservation.Quantity)
                        .SetProperty(
                            stock => stock.UpdatedAtUtc,
                            DateTime.UtcNow));
        }

        await dbContext.InventoryReservations
            .Where(reservation =>
                reservation.OrderItem.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.Payments
            .Where(payment => payment.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderFulfillments
            .Where(fulfillment =>
                fulfillment.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.Shipments
            .Where(shipment => shipment.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderHistory
            .Where(entry => entry.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderItems
            .Where(item => item.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderAddresses
            .Where(address => address.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.Orders
            .Where(order => order.OrderId == TestOrderId)
            .ExecuteDeleteAsync();
    }

    private sealed record StockSnapshot(
        int AvailableQuantity,
        int ReservedQuantity);
}

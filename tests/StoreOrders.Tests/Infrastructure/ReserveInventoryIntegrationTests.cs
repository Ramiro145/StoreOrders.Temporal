using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Infrastructure.Operations;
using StoreOrders.Infrastructure.Persistence;

namespace StoreOrders.Tests.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public sealed class ReserveInventoryIntegrationTests
{
    private static readonly Guid SuccessfulOrderId =
        Guid.Parse("40000000-0000-0000-0000-000000000004");

    private static readonly Guid RejectedOrderId =
        Guid.Parse("40000000-0000-0000-0000-000000000005");

    private static readonly Guid[] TestOrderIds =
    [
        SuccessfulOrderId,
        RejectedOrderId
    ];

    [Fact]
    public async Task ReserveInventoryAsync_IsAtomicAndIdempotent()
    {
        await using (var setupDbContext = CreateDbContext())
        {
            await setupDbContext.Database.MigrateAsync();
            await CleanupAsync(setupDbContext);
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

            Assert.True(stockBefore[1].AvailableQuantity >= 2);
            Assert.True(stockBefore[2].AvailableQuantity >= 2);

            var unavailableQuantity =
                stockBefore[3].AvailableQuantity + 1;

            await using (var creationDbContext = CreateDbContext())
            {
                var operation = new EfOrderOperations(creationDbContext);

                await operation.CreateOrderAsync(
                    CreateInput(
                        SuccessfulOrderId,
                        [
                            new CreateOrderItemInput(1, 1),
                            new CreateOrderItemInput(2, 2)
                        ]));

                await operation.CreateOrderAsync(
                    CreateInput(
                        RejectedOrderId,
                        [
                            new CreateOrderItemInput(1, 1),
                            new CreateOrderItemInput(
                                3,
                                unavailableQuantity)
                        ]));
            }

            ReserveInventoryResult firstResult;

            await using (var firstReservationDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(firstReservationDbContext);

                firstResult = await operation.ReserveInventoryAsync(
                    new ReserveInventoryInput(SuccessfulOrderId));
            }

            ReserveInventoryResult retryResult;

            await using (var retryDbContext = CreateDbContext())
            {
                var operation = new EfOrderOperations(retryDbContext);

                retryResult = await operation.ReserveInventoryAsync(
                    new ReserveInventoryInput(SuccessfulOrderId));
            }

            ReserveInventoryResult rejectedResult;

            await using (var rejectedDbContext = CreateDbContext())
            {
                var operation = new EfOrderOperations(rejectedDbContext);

                rejectedResult = await operation.ReserveInventoryAsync(
                    new ReserveInventoryInput(RejectedOrderId));
            }

            Assert.Equal(
                ReserveInventoryOutcome.Reserved,
                firstResult.Outcome);

            Assert.Equal(
                ReserveInventoryOutcome.AlreadyReserved,
                retryResult.Outcome);

            Assert.Equal(
                ReserveInventoryOutcome.InsufficientInventory,
                rejectedResult.Outcome);

            Assert.Equal(3, rejectedResult.InsufficientProductId);

            await using var verificationDbContext = CreateDbContext();

            var successfulOrder = await verificationDbContext.Orders
                .AsNoTracking()
                .SingleAsync(
                    order => order.OrderId == SuccessfulOrderId);

            var rejectedOrder = await verificationDbContext.Orders
                .AsNoTracking()
                .SingleAsync(
                    order => order.OrderId == RejectedOrderId);

            Assert.Equal(
                OrderStatus.AwaitingPayment,
                successfulOrder.Status);

            Assert.Equal(
                OrderStatus.Rejected,
                rejectedOrder.Status);

            var successfulReservations =
                await verificationDbContext.InventoryReservations
                    .AsNoTracking()
                    .Where(reservation =>
                        reservation.OrderItem.OrderId ==
                        SuccessfulOrderId)
                    .ToListAsync();

            var rejectedReservationCount =
                await verificationDbContext.InventoryReservations
                    .AsNoTracking()
                    .CountAsync(reservation =>
                        reservation.OrderItem.OrderId ==
                        RejectedOrderId);

            Assert.Equal(2, successfulReservations.Count);
            Assert.All(
                successfulReservations,
                reservation => Assert.Equal(
                    ReservationStatus.Active,
                    reservation.Status));

            Assert.Equal(0, rejectedReservationCount);

            var stockAfter = await verificationDbContext.InventoryStocks
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
                stockBefore[1].AvailableQuantity - 1,
                stockAfter[1].AvailableQuantity);

            Assert.Equal(
                stockBefore[1].ReservedQuantity + 1,
                stockAfter[1].ReservedQuantity);

            Assert.Equal(
                stockBefore[2].AvailableQuantity - 2,
                stockAfter[2].AvailableQuantity);

            Assert.Equal(
                stockBefore[2].ReservedQuantity + 2,
                stockAfter[2].ReservedQuantity);

            Assert.Equal(stockBefore[3], stockAfter[3]);

            Assert.Equal(
                2,
                await verificationDbContext.OrderHistory.CountAsync(
                    entry =>
                        entry.OrderId == SuccessfulOrderId));

            Assert.Equal(
                2,
                await verificationDbContext.OrderHistory.CountAsync(
                    entry =>
                        entry.OrderId == RejectedOrderId));
        }
        finally
        {
            await using var cleanupDbContext = CreateDbContext();
            await CleanupAsync(cleanupDbContext);
        }
    }

    private static CreateOrderInput CreateInput(
        Guid orderId,
        CreateOrderItemInput[] items)
    {
        return new CreateOrderInput(
            orderId,
            orderId.ToString("D"),
            $"order-{orderId:D}",
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
            Environment.GetEnvironmentVariable("STOREORDERS_SQL_PORT")
            ?? "14330";

        var connectionString = new SqlConnectionStringBuilder
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
        StoreOrdersDbContext dbContext)
    {
        var activeReservations =
            await dbContext.InventoryReservations
                .Where(reservation =>
                    TestOrderIds.Contains(
                        reservation.OrderItem.OrderId) &&
                    reservation.Status == ReservationStatus.Active)
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
                TestOrderIds.Contains(
                    reservation.OrderItem.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.OrderHistory
            .Where(entry =>
                TestOrderIds.Contains(entry.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.OrderItems
            .Where(item =>
                TestOrderIds.Contains(item.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.OrderAddresses
            .Where(address =>
                TestOrderIds.Contains(address.OrderId))
            .ExecuteDeleteAsync();

        await dbContext.Orders
            .Where(order =>
                TestOrderIds.Contains(order.OrderId))
            .ExecuteDeleteAsync();
    }

    private sealed record StockSnapshot(
        int AvailableQuantity,
        int ReservedQuantity);
}

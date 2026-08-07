using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Infrastructure.Operations;
using StoreOrders.Infrastructure.Persistence;

namespace StoreOrders.Tests.Infrastructure;

public sealed class CreateOrderIntegrationTests
{
    private static readonly Guid TestOrderId =
        Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task CreateOrderAsync_CreatesCompleteOrder_AndIsIdempotent()
    {
        await using (var setupDbContext = CreateDbContext())
        {
            await setupDbContext.Database.MigrateAsync();
            await DeleteTestOrderAsync(setupDbContext);
        }

        try
        {
            var input = new CreateOrderInput(
                TestOrderId,
                TestOrderId.ToString("D"),
                $"order-{TestOrderId:D}",
                "Ramiro González",
                "ramiro@example.com",
                new CreateOrderAddressInput(
                    "Ramiro González",
                    "Av. Ejemplo 123",
                    "Col. Centro",
                    "Monterrey",
                    "Nuevo León",
                    "64000",
                    "MX"),
                [
                    new CreateOrderItemInput(1, 1),
                    new CreateOrderItemInput(2, 2)
                ]);

            CreateOrderResult firstResult;

            await using (var firstDbContext = CreateDbContext())
            {
                var operation = new EfOrderOperations(firstDbContext);

                firstResult = await operation.CreateOrderAsync(input);
            }

            CreateOrderResult secondResult;

            await using (var secondDbContext = CreateDbContext())
            {
                var operation = new EfOrderOperations(secondDbContext);

                secondResult = await operation.CreateOrderAsync(input);
            }

            Assert.Equal(CreateOrderOutcome.Created, firstResult.Outcome);
            Assert.Equal(CreateOrderOutcome.AlreadyExists, secondResult.Outcome);
            Assert.Equal(firstResult.OrderId, secondResult.OrderId);
            Assert.Equal(firstResult.OrderNumber, secondResult.OrderNumber);
            Assert.Equal(15_400.00m, firstResult.TotalAmount);
            Assert.Equal(OrderStatus.Received, firstResult.Status);

            await using var verificationDbContext = CreateDbContext();

            var order = await verificationDbContext.Orders
                .AsNoTracking()
                .Include(current => current.Address)
                .SingleAsync(current => current.OrderId == TestOrderId);

            var items = await verificationDbContext.OrderItems
                .AsNoTracking()
                .Where(item => item.OrderId == TestOrderId)
                .OrderBy(item => item.ProductId)
                .ToListAsync();

            var history = await verificationDbContext.OrderHistory
                .AsNoTracking()
                .Where(entry => entry.OrderId == TestOrderId)
                .ToListAsync();

            Assert.Equal(15_400.00m, order.TotalAmount);
            Assert.Equal(OrderStatus.Received, order.Status);
            Assert.NotNull(order.Address);
            Assert.Equal(1, order.Address.AddressVersion);

            Assert.Equal(2, items.Count);

            var laptop = Assert.Single(
                items,
                item => item.ProductId == 1);

            Assert.Equal(14_500.00m, laptop.UnitPrice);
            Assert.Equal(1, laptop.Quantity);
            Assert.Equal(14_500.00m, laptop.LineTotal);

            var mouse = Assert.Single(
                items,
                item => item.ProductId == 2);

            Assert.Equal(450.00m, mouse.UnitPrice);
            Assert.Equal(2, mouse.Quantity);
            Assert.Equal(900.00m, mouse.LineTotal);

            var receivedEvent = Assert.Single(history);

            Assert.Equal("OrderReceived", receivedEvent.EventType);
            Assert.Equal(OrderStatus.Received, receivedEvent.CurrentStatus);

            Assert.Equal(
                1,
                await verificationDbContext.Orders.CountAsync(
                    current => current.OrderId == TestOrderId));

            Assert.Equal(
                2,
                await verificationDbContext.OrderItems.CountAsync(
                    item => item.OrderId == TestOrderId));

            Assert.Equal(
                1,
                await verificationDbContext.OrderHistory.CountAsync(
                    entry => entry.OrderId == TestOrderId));
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
            Environment.GetEnvironmentVariable("STOREORDERS_SQL_PASSWORD")
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

        var options = new DbContextOptionsBuilder<StoreOrdersDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new StoreOrdersDbContext(options);
    }

    private static async Task DeleteTestOrderAsync(
        StoreOrdersDbContext dbContext)
    {
        await dbContext.InventoryReservations
            .Where(reservation =>
                reservation.OrderItem.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.Payments
            .Where(payment => payment.OrderId == TestOrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderFulfillments
            .Where(fulfillment => fulfillment.OrderId == TestOrderId)
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
}

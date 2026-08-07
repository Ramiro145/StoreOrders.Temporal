using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreOrders.Domain.Abstractions;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Infrastructure;
using StoreOrders.Infrastructure.Persistence;
using StoreOrders.Workflows.Configuration;

namespace StoreOrders.Tests.Infrastructure;

public sealed class OrderReadServiceIntegrationTests
{
    private static readonly Guid OrderId =
        Guid.Parse("70000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task GetByIdAsync_ReturnsCommercialOrderView()
    {
        var connectionString = CreateConnectionString();

        await using (var setupDbContext = CreateDbContext())
        {
            await setupDbContext.Database.MigrateAsync();
            await CleanupAsync(setupDbContext);
        }

        try
        {
            var services = new ServiceCollection();

            services.AddStoreOrdersInfrastructure(connectionString);

            await using var provider =
                services.BuildServiceProvider();

            await using (var operationScope =
                provider.CreateAsyncScope())
            {
                var operations = operationScope.ServiceProvider
                    .GetRequiredService<IOrderOperations>();

                await operations.CreateOrderAsync(
                    new CreateOrderInput(
                        OrderId,
                        OrderId.ToString("D"),
                        TemporalNames.OrderWorkflowId(OrderId),
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
                        ]));

                await operations.ReserveInventoryAsync(
                    new ReserveInventoryInput(OrderId));
            }

            await using var readScope =
                provider.CreateAsyncScope();

            var readService = readScope.ServiceProvider
                .GetRequiredService<IOrderReadService>();

            var result = await readService.GetByIdAsync(OrderId);

            Assert.NotNull(result);
            Assert.Equal(OrderId, result.OrderId);
            Assert.Equal(
                OrderStatus.AwaitingPayment,
                result.Status);

            Assert.Equal(
                "Ramiro González",
                result.CustomerName);

            Assert.Equal("MXN", result.Currency);
            Assert.Equal(2, result.Items.Count);

            Assert.Equal(
                "Ramiro González",
                result.DeliveryAddress.RecipientName);

            Assert.Equal(
                1,
                result.DeliveryAddress.AddressVersion);

            Assert.All(
                result.Items,
                item =>
                {
                    Assert.True(item.Quantity > 0);
                    Assert.True(item.UnitPrice >= 0);
                    Assert.Equal(
                        item.Quantity * item.UnitPrice,
                        item.LineTotal);
                });

            Assert.Equal(
                result.Items.Sum(item => item.LineTotal),
                result.TotalAmount);

            Assert.Null(result.Payment);
            Assert.Null(result.Fulfillment);
            Assert.Null(result.Shipment);

            Assert.Null(
                await readService.GetByIdAsync(Guid.NewGuid()));
        }
        finally
        {
            await using var cleanupDbContext = CreateDbContext();
            await CleanupAsync(cleanupDbContext);
        }
    }

    private static string CreateConnectionString()
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

        return new SqlConnectionStringBuilder
        {
            DataSource = $"localhost,{port}",
            InitialCatalog = "StoreOrdersDb",
            UserID = "sa",
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true
        }.ConnectionString;
    }

    private static StoreOrdersDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<StoreOrdersDbContext>()
                .UseSqlServer(CreateConnectionString())
                .Options;

        return new StoreOrdersDbContext(options);
    }

    private static async Task CleanupAsync(
        StoreOrdersDbContext dbContext)
    {
        var reservations =
            await dbContext.InventoryReservations
                .Where(reservation =>
                    reservation.OrderItem.OrderId == OrderId &&
                    reservation.Status ==
                        ReservationStatus.Active)
                .Select(reservation => new
                {
                    reservation.OrderItem.ProductId,
                    reservation.Quantity
                })
                .ToListAsync();

        foreach (var reservation in reservations)
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
                reservation.OrderItem.OrderId == OrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderHistory
            .Where(entry => entry.OrderId == OrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderItems
            .Where(item => item.OrderId == OrderId)
            .ExecuteDeleteAsync();

        await dbContext.OrderAddresses
            .Where(address => address.OrderId == OrderId)
            .ExecuteDeleteAsync();

        await dbContext.Orders
            .Where(order => order.OrderId == OrderId)
            .ExecuteDeleteAsync();
    }
}

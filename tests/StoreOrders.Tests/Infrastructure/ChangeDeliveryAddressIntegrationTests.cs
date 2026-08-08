using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Infrastructure.Operations;
using StoreOrders.Infrastructure.Persistence;

namespace StoreOrders.Tests.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public sealed class ChangeDeliveryAddressIntegrationTests
{
    private static readonly Guid TestOrderId =
        Guid.Parse("40000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task ChangeDeliveryAddressAsync_IsIdempotent_AndRejectsShipped()
    {
        await using (var setupDbContext = CreateDbContext())
        {
            await setupDbContext.Database.MigrateAsync();
            await DeleteTestOrderAsync(setupDbContext);
        }

        try
        {
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
                    new CreateOrderItemInput(1, 1)
                ]);

            await using (var createDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(createDbContext);

                await operation.CreateOrderAsync(createInput);
            }

            var operationId = Guid.NewGuid();

            var changeInput = new ChangeDeliveryAddressInput(
                TestOrderId,
                operationId,
                "Ramiro González",
                "Av. Nueva 450",
                "Interior 2",
                "San Nicolás de los Garza",
                "Nuevo León",
                "66400",
                "mx");

            ChangeDeliveryAddressResult firstResult;

            await using (var firstDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(firstDbContext);

                firstResult =
                    await operation.ChangeDeliveryAddressAsync(
                        changeInput);
            }

            ChangeDeliveryAddressResult duplicateResult;

            await using (var duplicateDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(duplicateDbContext);

                duplicateResult =
                    await operation.ChangeDeliveryAddressAsync(
                        changeInput);
            }

            Assert.Equal(
                ChangeDeliveryAddressOutcome.Changed,
                firstResult.Outcome);
            Assert.Equal(2, firstResult.AddressVersion);

            Assert.Equal(
                ChangeDeliveryAddressOutcome.AlreadyChanged,
                duplicateResult.Outcome);
            Assert.Equal(2, duplicateResult.AddressVersion);

            await using (var shippedDbContext = CreateDbContext())
            {
                await shippedDbContext.Orders
                    .Where(order => order.OrderId == TestOrderId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                order => order.Status,
                                OrderStatus.Shipped)
                            .SetProperty(
                                order => order.UpdatedAtUtc,
                                DateTime.UtcNow));
            }

            ChangeDeliveryAddressResult rejectedResult;

            await using (var rejectedDbContext = CreateDbContext())
            {
                var operation =
                    new EfOrderOperations(rejectedDbContext);

                rejectedResult =
                    await operation.ChangeDeliveryAddressAsync(
                        changeInput with
                        {
                            OperationId = Guid.NewGuid(),
                            Line1 = "Av. Rechazada 999"
                        });
            }

            Assert.Equal(
                ChangeDeliveryAddressOutcome.NotAllowed,
                rejectedResult.Outcome);
            Assert.Equal(2, rejectedResult.AddressVersion);
            Assert.Equal(
                "El pedido ya fue enviado.",
                rejectedResult.Message);

            await using var verificationDbContext =
                CreateDbContext();

            var address =
                await verificationDbContext.OrderAddresses
                    .AsNoTracking()
                    .SingleAsync(
                        current => current.OrderId == TestOrderId);

            Assert.Equal("Av. Nueva 450", address.Line1);
            Assert.Equal("Interior 2", address.Line2);
            Assert.Equal("MX", address.CountryCode);
            Assert.Equal(2, address.AddressVersion);

            var addressHistory =
                await verificationDbContext.OrderHistory
                    .AsNoTracking()
                    .Where(entry =>
                        entry.OrderId == TestOrderId &&
                        entry.EventType ==
                            "DeliveryAddressChanged")
                    .ToListAsync();

            var changedEvent = Assert.Single(addressHistory);

            Assert.Equal(
                $"order:{TestOrderId:D}:address:{operationId:D}",
                changedEvent.OperationKey);
            Assert.Equal(
                ActorType.Customer,
                changedEvent.ActorType);
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
}

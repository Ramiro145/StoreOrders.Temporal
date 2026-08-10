using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Domain.ReadModels;
using StoreOrders.Infrastructure.Operations;
using StoreOrders.Infrastructure.Persistence;
using StoreOrders.Infrastructure.Queries;
using StoreOrders.Workflows.Configuration;

namespace StoreOrders.Tests.Infrastructure;

[Collection(IntegrationTestCollection.Name)]
public sealed class DeliveryOperationsIntegrationTests
{
    private static readonly Guid TestOrderId =
        Guid.Parse("60000000-0000-0000-0000-000000000006");

    [Fact]
    public async Task DeliveryOperations_ConsumeReservations_AndAreIdempotent()
    {
        await using (var setupDbContext = CreateDbContext())
        {
            await setupDbContext.Database.MigrateAsync();
            await DeleteTestOrderAsync(setupDbContext);
        }

        try
        {
            StockSnapshot stockBefore;
            decimal totalAmount;

            await using (var snapshotDbContext = CreateDbContext())
            {
                stockBefore = await snapshotDbContext.InventoryStocks
                    .AsNoTracking()
                    .Where(stock => stock.ProductId == 2)
                    .Select(stock => new StockSnapshot(
                        stock.AvailableQuantity,
                        stock.ReservedQuantity))
                    .SingleAsync();

                totalAmount = await snapshotDbContext.Products
                    .Where(product => product.ProductId == 2)
                    .Select(product => product.CurrentPrice)
                    .SingleAsync();
            }

            Assert.True(stockBefore.AvailableQuantity >= 1);

            var paymentEventId = Guid.NewGuid();
            var packingEventId = Guid.NewGuid();

            await using (var createOrderDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(createOrderDbContext);

                var result = await operations.CreateOrderAsync(
                    CreateOrderInput());

                Assert.Equal(
                    CreateOrderOutcome.Created,
                    result.Outcome);
            }

            await using (var reserveInventoryDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(reserveInventoryDbContext);

                var result = await operations.ReserveInventoryAsync(
                    new ReserveInventoryInput(TestOrderId));

                Assert.Equal(
                    ReserveInventoryOutcome.Reserved,
                    result.Outcome);
            }

            await using (var confirmPaymentDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(confirmPaymentDbContext);

                var result = await operations.ConfirmPaymentAsync(
                    new ConfirmPaymentInput(
                        TestOrderId,
                        paymentEventId,
                        $"PAY-{Guid.NewGuid():N}",
                        totalAmount,
                        "MXN",
                        DateTime.UtcNow));

                Assert.Equal(
                    ConfirmPaymentOutcome.Confirmed,
                    result.Outcome);
            }

            await using (var startFulfillmentDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(startFulfillmentDbContext);

                var result = await operations.StartFulfillmentAsync(
                    new StartFulfillmentInput(TestOrderId));

                Assert.Equal(
                    StartFulfillmentOutcome.Started,
                    result.Outcome);
            }

            await using (var completePackingDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(completePackingDbContext);

                var result = await operations.CompletePackingAsync(
                    new CompletePackingInput(
                        TestOrderId,
                        packingEventId,
                        "warehouse-test-user",
                        DateTime.UtcNow));

                Assert.Equal(
                    CompletePackingOutcome.Packed,
                    result.Outcome);
            }

            var deliveryWorkflowId =
                TemporalNames.DeliveryWorkflowId(TestOrderId);

            CreateShipmentResult firstCreation;
            CreateShipmentResult duplicateCreation;

            await using (var creationDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(creationDbContext);

                firstCreation = await operations.CreateShipmentAsync(
                    new CreateShipmentInput(
                        TestOrderId,
                        deliveryWorkflowId));
            }

            await using (var duplicateDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(duplicateDbContext);

                duplicateCreation = await operations.CreateShipmentAsync(
                    new CreateShipmentInput(
                        TestOrderId,
                        deliveryWorkflowId));
            }

            Assert.Equal(
                CreateShipmentOutcome.Created,
                firstCreation.Outcome);
            Assert.Equal(
                CreateShipmentOutcome.AlreadyExists,
                duplicateCreation.Outcome);

            var shippedInput = new MarkShipmentShippedInput(
                TestOrderId,
                Guid.NewGuid(),
                "Paquetería Demo",
                $"TRACK-{Guid.NewGuid():N}",
                DateTime.UtcNow);

            MarkShipmentShippedResult firstShipped;
            MarkShipmentShippedResult duplicateShipped;

            await using (var shippedDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(shippedDbContext);

                firstShipped =
                    await operations.MarkShipmentShippedAsync(
                        shippedInput);
            }

            await using (var duplicateDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(duplicateDbContext);

                duplicateShipped =
                    await operations.MarkShipmentShippedAsync(
                        shippedInput);
            }

            Assert.Equal(
                MarkShipmentShippedOutcome.Shipped,
                firstShipped.Outcome);
            Assert.Equal(
                MarkShipmentShippedOutcome.AlreadyShipped,
                duplicateShipped.Outcome);

            await using (var shippedVerification = CreateDbContext())
            {
                var stockAfterShipment =
                    await shippedVerification.InventoryStocks
                        .AsNoTracking()
                        .Where(stock => stock.ProductId == 2)
                        .Select(stock => new StockSnapshot(
                            stock.AvailableQuantity,
                            stock.ReservedQuantity))
                        .SingleAsync();

                Assert.Equal(
                    stockBefore.AvailableQuantity - 1,
                    stockAfterShipment.AvailableQuantity);
                Assert.Equal(
                    stockBefore.ReservedQuantity,
                    stockAfterShipment.ReservedQuantity);

                var reservation =
                    await shippedVerification.InventoryReservations
                        .AsNoTracking()
                        .SingleAsync(current =>
                            current.OrderItem.OrderId == TestOrderId);

                Assert.Equal(
                    ReservationStatus.Consumed,
                    reservation.Status);
                Assert.NotNull(reservation.ConsumedAtUtc);
            }

            await using (var cancellationDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(cancellationDbContext);

                var cancellation = await operations.CancelOrderAsync(
                    new CancelOrderInput(
                        TestOrderId,
                        Guid.NewGuid(),
                        "Cancelación posterior al envío.",
                        "customer"));

                Assert.Equal(
                    CancelOrderOutcome.NotAllowed,
                    cancellation.Outcome);
            }

            var deliveredInput = new MarkShipmentDeliveredInput(
                TestOrderId,
                Guid.NewGuid(),
                DateTime.UtcNow);

            MarkShipmentDeliveredResult firstDelivered;
            MarkShipmentDeliveredResult duplicateDelivered;

            await using (var deliveredDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(deliveredDbContext);

                firstDelivered =
                    await operations.MarkShipmentDeliveredAsync(
                        deliveredInput);
            }

            await using (var duplicateDbContext = CreateDbContext())
            {
                var operations =
                    new EfOrderOperations(duplicateDbContext);

                duplicateDelivered =
                    await operations.MarkShipmentDeliveredAsync(
                        deliveredInput);
            }

            Assert.Equal(
                MarkShipmentDeliveredOutcome.Delivered,
                firstDelivered.Outcome);
            Assert.Equal(
                MarkShipmentDeliveredOutcome.AlreadyDelivered,
                duplicateDelivered.Outcome);

            await using var verificationDbContext = CreateDbContext();

            var order = await verificationDbContext.Orders
                .AsNoTracking()
                .SingleAsync(current => current.OrderId == TestOrderId);

            var shipment = await verificationDbContext.Shipments
                .AsNoTracking()
                .SingleAsync(current => current.OrderId == TestOrderId);

            Assert.Equal(OrderStatus.Delivered, order.Status);
            Assert.NotNull(order.DeliveredAtUtc);
            Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
            Assert.Equal(shippedInput.Carrier, shipment.Carrier);
            Assert.Equal(
                shippedInput.TrackingNumber,
                shipment.TrackingNumber);
            Assert.NotNull(shipment.ShippedAtUtc);
            Assert.NotNull(shipment.DeliveredAtUtc);

            var orderView = await new EfOrderReadService(
                    verificationDbContext)
                .GetByIdAsync(TestOrderId);

            Assert.NotNull(orderView);
            var shipmentView =
                Assert.IsType<OrderShipmentReadModel>(
                    orderView!.Shipment);

            Assert.Equal("Delivered", shipmentView.Status);
            Assert.Equal(
                shippedInput.TrackingNumber,
                shipmentView.TrackingNumber);

            Assert.Equal(
                1,
                await verificationDbContext.OrderHistory.CountAsync(
                    entry =>
                        entry.OrderId == TestOrderId &&
                        entry.EventType == "ShipmentCreated"));

            Assert.Equal(
                1,
                await verificationDbContext.OrderHistory.CountAsync(
                    entry =>
                        entry.OrderId == TestOrderId &&
                        entry.EventType == "ShipmentShipped"));

            Assert.Equal(
                1,
                await verificationDbContext.OrderHistory.CountAsync(
                    entry =>
                        entry.OrderId == TestOrderId &&
                        entry.EventType == "ShipmentDelivered"));
        }
        finally
        {
            await using var cleanupDbContext = CreateDbContext();
            await DeleteTestOrderAsync(cleanupDbContext);
        }
    }

    private static CreateOrderInput CreateOrderInput()
    {
        return new CreateOrderInput(
            TestOrderId,
            TestOrderId.ToString("D"),
            TemporalNames.OrderWorkflowId(TestOrderId),
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
                new CreateOrderItemInput(2, 1)
            ]);
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
        var reservations =
            await dbContext.InventoryReservations
                .Where(reservation =>
                    reservation.OrderItem.OrderId == TestOrderId)
                .Select(reservation => new
                {
                    reservation.OrderItem.ProductId,
                    reservation.Quantity,
                    reservation.Status
                })
                .ToArrayAsync();

        foreach (var reservation in reservations)
        {
            if (reservation.Status == ReservationStatus.Active)
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
            else if (reservation.Status == ReservationStatus.Consumed)
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
                                stock => stock.UpdatedAtUtc,
                                DateTime.UtcNow));
            }
        }

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

    private sealed record StockSnapshot(
        int AvailableQuantity,
        int ReservedQuantity);
}

using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Abstractions;
using StoreOrders.Domain.Entities;
using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Infrastructure.Persistence;

namespace StoreOrders.Infrastructure.Operations;

public sealed class EfOrderOperations(
    StoreOrdersDbContext dbContext)
    : IOrderOperations
{
    public async Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(input);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingOrder = await dbContext.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                order =>
                    order.OrderId == input.OrderId ||
                    order.ClientRequestId == input.ClientRequestId ||
                    order.TemporalWorkflowId == input.TemporalWorkflowId,
                cancellationToken);

        if (existingOrder is not null)
        {
            EnsureCompatibleRetry(existingOrder, input);

            await transaction.CommitAsync(cancellationToken);

            return ToResult(
                existingOrder,
                CreateOrderOutcome.AlreadyExists);
        }

        var requestedItems = input.Items
            .GroupBy(item => item.ProductId)
            .Select(group => new RequestedItem(
                group.Key,
                group.Sum(item => item.Quantity)))
            .OrderBy(item => item.ProductId)
            .ToArray();

        var productIds = requestedItems
            .Select(item => item.ProductId)
            .ToArray();

        var productsById = await dbContext.Products
            .Where(product =>
                productIds.Contains(product.ProductId) &&
                product.IsActive)
            .ToDictionaryAsync(
                product => product.ProductId,
                cancellationToken);

        var unavailableProductIds = productIds
            .Where(productId => !productsById.ContainsKey(productId))
            .ToArray();

        if (unavailableProductIds.Length > 0)
        {
            throw new InvalidOperationException(
                "Los siguientes productos no existen o están inactivos: " +
                string.Join(", ", unavailableProductIds));
        }

        var nowUtc = DateTime.UtcNow;

        var order = new Order
        {
            OrderId = input.OrderId,
            OrderNumber = BuildOrderNumber(input.OrderId),
            ClientRequestId = input.ClientRequestId.Trim(),
            TemporalWorkflowId = input.TemporalWorkflowId.Trim(),
            Status = OrderStatus.Received,
            CustomerName = input.CustomerName.Trim(),
            CustomerEmail = input.CustomerEmail.Trim(),
            Currency = "MXN",
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        order.Address = new OrderAddress
        {
            OrderId = order.OrderId,
            RecipientName = input.Address.RecipientName.Trim(),
            Line1 = input.Address.Line1.Trim(),
            Line2 = NormalizeOptional(input.Address.Line2),
            City = input.Address.City.Trim(),
            State = input.Address.State.Trim(),
            PostalCode = input.Address.PostalCode.Trim(),
            CountryCode = input.Address.CountryCode.Trim().ToUpperInvariant(),
            AddressVersion = 1,
            UpdatedAtUtc = nowUtc,
            Order = order
        };

        foreach (var requestedItem in requestedItems)
        {
            var product = productsById[requestedItem.ProductId];
            var lineTotal = product.CurrentPrice * requestedItem.Quantity;

            order.Items.Add(new OrderItem
            {
                OrderItemId = Guid.NewGuid(),
                OrderId = order.OrderId,
                ProductId = product.ProductId,
                ProductSku = product.Sku,
                ProductName = product.Name,
                Quantity = requestedItem.Quantity,
                UnitPrice = product.CurrentPrice,
                LineTotal = lineTotal,
                Order = order,
                Product = product
            });
        }

        order.TotalAmount = order.Items.Sum(item => item.LineTotal);

        if (order.TotalAmount <= 0)
        {
            throw new InvalidOperationException(
                "El total calculado del pedido debe ser mayor que cero.");
        }

        order.History.Add(new OrderHistoryEntry
        {
            OrderId = order.OrderId,
            EventType = "OrderReceived",
            PreviousStatus = null,
            CurrentStatus = OrderStatus.Received,
            ActorType = ActorType.System,
            Description = "Pedido recibido y creado en SQL Server.",
            OperationKey = $"order:{order.OrderId:D}:create",
            OccurredAtUtc = nowUtc,
            Order = order
        });

        dbContext.Orders.Add(order);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResult(order, CreateOrderOutcome.Created);
    }

    public async Task<ReserveInventoryResult> ReserveInventoryAsync(
        ReserveInventoryInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.OrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId no puede ser un GUID vacío.",
                nameof(input));
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        var order = await dbContext.Orders
            .Include(current => current.Items)
            .ThenInclude(item => item.InventoryReservation)
            .SingleOrDefaultAsync(
                current => current.OrderId == input.OrderId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"No existe el pedido {input.OrderId:D}.");

        if (order.Items.Count == 0)
        {
            throw new InvalidOperationException(
                "El pedido no contiene partidas para reservar.");
        }

        var existingReservations = order.Items
            .Where(item => item.InventoryReservation is not null)
            .Select(item => item.InventoryReservation!)
            .ToArray();

        var completeActiveReservation =
            existingReservations.Length == order.Items.Count &&
            existingReservations.All(
                reservation =>
                    reservation.Status == ReservationStatus.Active);

        if (completeActiveReservation &&
            order.Status == OrderStatus.AwaitingPayment)
        {
            await transaction.CommitAsync(cancellationToken);

            return new ReserveInventoryResult(
                order.OrderId,
                order.Status,
                ReserveInventoryOutcome.AlreadyReserved,
                null);
        }

        if (existingReservations.Length > 0)
        {
            throw new InvalidOperationException(
                "El pedido contiene una reservación parcial o incompatible.");
        }

        if (order.Status == OrderStatus.Rejected)
        {
            await transaction.CommitAsync(cancellationToken);

            return new ReserveInventoryResult(
                order.OrderId,
                order.Status,
                ReserveInventoryOutcome.InsufficientInventory,
                null);
        }

        if (order.Status != OrderStatus.Received)
        {
            throw new InvalidOperationException(
                $"El pedido está en estado {order.Status} y no puede reservarse.");
        }

        var nowUtc = DateTime.UtcNow;

        await transaction.CreateSavepointAsync(
            "BeforeInventoryChanges",
            cancellationToken);

        foreach (var item in order.Items.OrderBy(item => item.ProductId))
        {
            var affectedRows = await dbContext.InventoryStocks
                .Where(stock =>
                    stock.ProductId == item.ProductId &&
                    stock.AvailableQuantity >= item.Quantity)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            stock => stock.AvailableQuantity,
                            stock =>
                                stock.AvailableQuantity - item.Quantity)
                        .SetProperty(
                            stock => stock.ReservedQuantity,
                            stock =>
                                stock.ReservedQuantity + item.Quantity)
                        .SetProperty(
                            stock => stock.UpdatedAtUtc,
                            nowUtc),
                    cancellationToken);

            if (affectedRows == 0)
            {
                await transaction.RollbackToSavepointAsync(
                    "BeforeInventoryChanges",
                    cancellationToken);

                order.Status = OrderStatus.Rejected;
                order.UpdatedAtUtc = nowUtc;

                dbContext.OrderHistory.Add(new OrderHistoryEntry
                {
                    OrderId = order.OrderId,
                    EventType = "InventoryRejected",
                    PreviousStatus = OrderStatus.Received,
                    CurrentStatus = OrderStatus.Rejected,
                    ActorType = ActorType.System,
                    Description =
                        $"Inventario insuficiente para el producto " +
                        $"{item.ProductId}.",
                    OperationKey =
                        $"order:{order.OrderId:D}:reserve",
                    OccurredAtUtc = nowUtc,
                    Order = order
                });

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new ReserveInventoryResult(
                    order.OrderId,
                    order.Status,
                    ReserveInventoryOutcome.InsufficientInventory,
                    item.ProductId);
            }
        }

        foreach (var item in order.Items)
        {
            dbContext.InventoryReservations.Add(
                new InventoryReservation
                {
                    ReservationId = Guid.NewGuid(),
                    OrderItemId = item.OrderItemId,
                    Quantity = item.Quantity,
                    Status = ReservationStatus.Active,
                    OperationKey =
                        $"order:{order.OrderId:D}:" +
                        $"item:{item.OrderItemId:D}:reserve",
                    CreatedAtUtc = nowUtc,
                    OrderItem = item
                });
        }

        order.Status = OrderStatus.AwaitingPayment;
        order.UpdatedAtUtc = nowUtc;

        dbContext.OrderHistory.Add(new OrderHistoryEntry
        {
            OrderId = order.OrderId,
            EventType = "InventoryReserved",
            PreviousStatus = OrderStatus.Received,
            CurrentStatus = OrderStatus.AwaitingPayment,
            ActorType = ActorType.System,
            Description =
                "Inventario reservado para todas las partidas.",
            OperationKey =
                $"order:{order.OrderId:D}:reserve",
            OccurredAtUtc = nowUtc,
            Order = order
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ReserveInventoryResult(
            order.OrderId,
            order.Status,
            ReserveInventoryOutcome.Reserved,
            null);
    }

    public async Task<ConfirmPaymentResult> ConfirmPaymentAsync(
        ConfirmPaymentInput input,
        CancellationToken cancellationToken = default)
    {
        ValidatePaymentInput(input);

        var operationKey =
            $"order:{input.OrderId:D}:payment:{input.EventId:D}";

        var externalReference =
            input.ExternalPaymentReference.Trim();

        var currency =
            input.Currency.Trim().ToUpperInvariant();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(current => current.Payment)
            .SingleOrDefaultAsync(
                current => current.OrderId == input.OrderId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"No existe el pedido {input.OrderId:D}.");

        if (order.Payment is not null)
        {
            var outcome = PaymentMatches(
                    order.Payment,
                    externalReference,
                    input.Amount,
                    currency)
                ? ConfirmPaymentOutcome.AlreadyConfirmed
                : ConfirmPaymentOutcome.OrderNotPayable;

            await transaction.CommitAsync(cancellationToken);

            return new ConfirmPaymentResult(
                order.OrderId,
                order.Status,
                outcome);
        }

        var existingPayment = await dbContext.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                payment =>
                    payment.OperationKey == operationKey ||
                    payment.ExternalPaymentReference ==
                        externalReference,
                cancellationToken);

        if (existingPayment is not null)
        {
            var samePayment =
                existingPayment.OrderId == order.OrderId &&
                PaymentMatches(
                    existingPayment,
                    externalReference,
                    input.Amount,
                    currency);

            await transaction.CommitAsync(cancellationToken);

            return new ConfirmPaymentResult(
                order.OrderId,
                order.Status,
                samePayment
                    ? ConfirmPaymentOutcome.AlreadyConfirmed
                    : ConfirmPaymentOutcome.OrderNotPayable);
        }

        var previousAttempt = await dbContext.OrderHistory
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.OperationKey == operationKey,
                cancellationToken);

        if (previousAttempt is not null)
        {
            var previousOutcome = previousAttempt.EventType switch
            {
                "PaymentRejectedAmount" =>
                    ConfirmPaymentOutcome.RejectedAmount,
                "PaymentRejectedCurrency" =>
                    ConfirmPaymentOutcome.RejectedCurrency,
                _ => ConfirmPaymentOutcome.OrderNotPayable
            };

            await transaction.CommitAsync(cancellationToken);

            return new ConfirmPaymentResult(
                order.OrderId,
                order.Status,
                previousOutcome);
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            await transaction.CommitAsync(cancellationToken);

            return new ConfirmPaymentResult(
                order.OrderId,
                order.Status,
                ConfirmPaymentOutcome.OrderNotPayable);
        }

        if (input.Amount != order.TotalAmount)
        {
            dbContext.OrderHistory.Add(
                CreatePaymentRejectionHistory(
                    order,
                    operationKey,
                    "PaymentRejectedAmount",
                    "El importe informado no coincide con el total del pedido."));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ConfirmPaymentResult(
                order.OrderId,
                order.Status,
                ConfirmPaymentOutcome.RejectedAmount);
        }

        if (!string.Equals(
                currency,
                order.Currency,
                StringComparison.OrdinalIgnoreCase))
        {
            dbContext.OrderHistory.Add(
                CreatePaymentRejectionHistory(
                    order,
                    operationKey,
                    "PaymentRejectedCurrency",
                    "La moneda informada no coincide con la del pedido."));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ConfirmPaymentResult(
                order.OrderId,
                order.Status,
                ConfirmPaymentOutcome.RejectedCurrency);
        }

        var nowUtc = DateTime.UtcNow;

        var affectedOrders = await dbContext.Orders
            .Where(current =>
                current.OrderId == input.OrderId &&
                current.Status == OrderStatus.AwaitingPayment)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        current => current.Status,
                        OrderStatus.Paid)
                    .SetProperty(
                        current => current.UpdatedAtUtc,
                        nowUtc),
                cancellationToken);

        if (affectedOrders != 1)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new ConfirmPaymentResult(
                order.OrderId,
                order.Status,
                ConfirmPaymentOutcome.OrderNotPayable);
        }

        dbContext.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            OrderId = order.OrderId,
            ExternalPaymentReference = externalReference,
            Amount = input.Amount,
            Currency = currency,
            Status = "Confirmed",
            OperationKey = operationKey,
            ConfirmedAtUtc = input.ConfirmedAtUtc,
            CreatedAtUtc = nowUtc
        });

        dbContext.OrderHistory.Add(new OrderHistoryEntry
        {
            OrderId = order.OrderId,
            EventType = "PaymentConfirmed",
            PreviousStatus = OrderStatus.AwaitingPayment,
            CurrentStatus = OrderStatus.Paid,
            ActorType = ActorType.PaymentService,
            Description = "Pago confirmado por el servicio externo.",
            OperationKey = operationKey,
            OccurredAtUtc = nowUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ConfirmPaymentResult(
            order.OrderId,
            OrderStatus.Paid,
            ConfirmPaymentOutcome.Confirmed);
    }

    public async Task<StartFulfillmentResult> StartFulfillmentAsync(
        StartFulfillmentInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.OrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId no puede ser un GUID vacío.",
                nameof(input));
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(current => current.Fulfillment)
            .SingleOrDefaultAsync(
                current => current.OrderId == input.OrderId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"No existe el pedido {input.OrderId:D}.");

        if (order.Fulfillment is not null)
        {
            await transaction.CommitAsync(cancellationToken);

            return new StartFulfillmentResult(
                order.OrderId,
                order.Status,
                StartFulfillmentOutcome.AlreadyStarted);
        }

        if (order.Status != OrderStatus.Paid)
        {
            await transaction.CommitAsync(cancellationToken);

            return new StartFulfillmentResult(
                order.OrderId,
                order.Status,
                StartFulfillmentOutcome.OrderNotReady);
        }

        var nowUtc = DateTime.UtcNow;

        var affectedOrders = await dbContext.Orders
            .Where(current =>
                current.OrderId == input.OrderId &&
                current.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        current => current.Status,
                        OrderStatus.Preparing)
                    .SetProperty(
                        current => current.UpdatedAtUtc,
                        nowUtc),
                cancellationToken);

        if (affectedOrders != 1)
        {
            await transaction.RollbackAsync(cancellationToken);

            return new StartFulfillmentResult(
                order.OrderId,
                order.Status,
                StartFulfillmentOutcome.OrderNotReady);
        }

        dbContext.OrderFulfillments.Add(new OrderFulfillment
        {
            FulfillmentId = Guid.NewGuid(),
            OrderId = order.OrderId,
            Status = FulfillmentStatus.Preparing,
            CreatedAtUtc = nowUtc
        });

        dbContext.OrderHistory.Add(new OrderHistoryEntry
        {
            OrderId = order.OrderId,
            EventType = "FulfillmentStarted",
            PreviousStatus = OrderStatus.Paid,
            CurrentStatus = OrderStatus.Preparing,
            ActorType = ActorType.System,
            Description = "El pedido entró al proceso de preparación.",
            OperationKey =
                $"order:{order.OrderId:D}:fulfillment:start",
            OccurredAtUtc = nowUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new StartFulfillmentResult(
            order.OrderId,
            OrderStatus.Preparing,
            StartFulfillmentOutcome.Started);
    }

    public async Task<CompletePackingResult> CompletePackingAsync(
        CompletePackingInput input,
        CancellationToken cancellationToken = default)
    {
        ValidatePackingInput(input);

        var operationKey =
            $"order:{input.OrderId:D}:packing:{input.EventId:D}";

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);

        var order = await dbContext.Orders
            .Include(current => current.Fulfillment)
            .SingleOrDefaultAsync(
                current => current.OrderId == input.OrderId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"No existe el pedido {input.OrderId:D}.");

        if (order.Fulfillment?.Status == FulfillmentStatus.Packed)
        {
            await transaction.CommitAsync(cancellationToken);

            return new CompletePackingResult(
                order.OrderId,
                order.Status,
                CompletePackingOutcome.AlreadyPacked);
        }

        if (order.Status != OrderStatus.Preparing ||
            order.Fulfillment?.Status != FulfillmentStatus.Preparing)
        {
            await transaction.CommitAsync(cancellationToken);

            return new CompletePackingResult(
                order.OrderId,
                order.Status,
                CompletePackingOutcome.OrderNotReady);
        }

        var nowUtc = DateTime.UtcNow;

        order.Fulfillment.Status = FulfillmentStatus.Packed;
        order.Fulfillment.PackedBy = input.PackedBy.Trim();
        order.Fulfillment.PackedAtUtc = input.PackedAtUtc;
        order.Fulfillment.OperationKey = operationKey;

        order.Status = OrderStatus.ReadyForShipment;
        order.UpdatedAtUtc = nowUtc;

        dbContext.OrderHistory.Add(new OrderHistoryEntry
        {
            OrderId = order.OrderId,
            EventType = "PackingCompleted",
            PreviousStatus = OrderStatus.Preparing,
            CurrentStatus = OrderStatus.ReadyForShipment,
            ActorType = ActorType.Warehouse,
            Description = "Almacén confirmó que el paquete está preparado.",
            OperationKey = operationKey,
            OccurredAtUtc = nowUtc,
            Order = order
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CompletePackingResult(
            order.OrderId,
            order.Status,
            CompletePackingOutcome.Packed);
    }

    private static void ValidateInput(CreateOrderInput input)
    {
        if (input.OrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId no puede ser un GUID vacío.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.ClientRequestId))
        {
            throw new ArgumentException(
                "ClientRequestId es obligatorio.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.TemporalWorkflowId))
        {
            throw new ArgumentException(
                "TemporalWorkflowId es obligatorio.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.CustomerName) ||
            string.IsNullOrWhiteSpace(input.CustomerEmail))
        {
            throw new ArgumentException(
                "El nombre y correo del cliente son obligatorios.",
                nameof(input));
        }

        if (input.Address is null)
        {
            throw new ArgumentException(
                "La dirección es obligatoria.",
                nameof(input));
        }

        if (input.Items is not { Length: > 0 })
        {
            throw new ArgumentException(
                "El pedido debe contener al menos un producto.",
                nameof(input));
        }

        if (input.Items.Any(item => item.Quantity <= 0))
        {
            throw new ArgumentException(
                "Todas las cantidades deben ser mayores que cero.",
                nameof(input));
        }
    }

    private static void EnsureCompatibleRetry(
        Order existingOrder,
        CreateOrderInput input)
    {
        var compatible =
            existingOrder.OrderId == input.OrderId &&
            string.Equals(
                existingOrder.ClientRequestId,
                input.ClientRequestId.Trim(),
                StringComparison.Ordinal) &&
            string.Equals(
                existingOrder.TemporalWorkflowId,
                input.TemporalWorkflowId.Trim(),
                StringComparison.Ordinal);

        if (!compatible)
        {
            throw new InvalidOperationException(
                "Uno de los identificadores ya pertenece a otro pedido.");
        }
    }

    private static CreateOrderResult ToResult(
        Order order,
        CreateOrderOutcome outcome)
    {
        return new CreateOrderResult(
            order.OrderId,
            order.OrderNumber,
            order.TotalAmount,
            order.Status,
            outcome);
    }

    private static string BuildOrderNumber(Guid orderId)
    {
        const string alphabet =
            "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        Span<byte> bytes = stackalloc byte[16];
        orderId.TryWriteBytes(bytes);

        Span<char> encoded = stackalloc char[26];

        uint buffer = 0;
        var bitsInBuffer = 0;
        var encodedIndex = 0;

        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;

                encoded[encodedIndex++] =
                    alphabet[(int)((buffer >> bitsInBuffer) & 31)];
            }

            buffer = bitsInBuffer == 0
                ? 0
                : buffer & ((1u << bitsInBuffer) - 1);
        }

        if (bitsInBuffer > 0)
        {
            encoded[encodedIndex] =
                alphabet[(int)((buffer << (5 - bitsInBuffer)) & 31)];
        }

        return $"ORD-{new string(encoded)}";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void ValidatePaymentInput(
        ConfirmPaymentInput input)
    {
        if (input.OrderId == Guid.Empty ||
            input.EventId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId y EventId deben contener GUID válidos.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(
                input.ExternalPaymentReference))
        {
            throw new ArgumentException(
                "ExternalPaymentReference es obligatorio.",
                nameof(input));
        }

        if (input.Amount <= 0)
        {
            throw new ArgumentException(
                "Amount debe ser mayor que cero.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.Currency) ||
            input.Currency.Trim().Length != 3)
        {
            throw new ArgumentException(
                "Currency debe contener tres caracteres.",
                nameof(input));
        }

        if (input.ConfirmedAtUtc == default)
        {
            throw new ArgumentException(
                "ConfirmedAtUtc es obligatorio.",
                nameof(input));
        }
    }

    private static void ValidatePackingInput(
        CompletePackingInput input)
    {
        if (input.OrderId == Guid.Empty ||
            input.EventId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId y EventId deben contener GUID válidos.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.PackedBy))
        {
            throw new ArgumentException(
                "PackedBy es obligatorio.",
                nameof(input));
        }

        if (input.PackedAtUtc == default)
        {
            throw new ArgumentException(
                "PackedAtUtc es obligatorio.",
                nameof(input));
        }
    }

    private static bool PaymentMatches(
        Payment payment,
        string externalReference,
        decimal amount,
        string currency)
    {
        return string.Equals(
                   payment.ExternalPaymentReference,
                   externalReference,
                   StringComparison.Ordinal) &&
               payment.Amount == amount &&
               string.Equals(
                   payment.Currency,
                   currency,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static OrderHistoryEntry CreatePaymentRejectionHistory(
        Order order,
        string operationKey,
        string eventType,
        string description)
    {
        return new OrderHistoryEntry
        {
            OrderId = order.OrderId,
            EventType = eventType,
            PreviousStatus = OrderStatus.AwaitingPayment,
            CurrentStatus = OrderStatus.AwaitingPayment,
            ActorType = ActorType.PaymentService,
            Description = description,
            OperationKey = operationKey,
            OccurredAtUtc = DateTime.UtcNow
        };
    }

    private sealed record RequestedItem(
        int ProductId,
        int Quantity);
}


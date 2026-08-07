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
        var value = $"ORD-{orderId:N}".ToUpperInvariant();
        return value[..30];
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record RequestedItem(
        int ProductId,
        int Quantity);
}

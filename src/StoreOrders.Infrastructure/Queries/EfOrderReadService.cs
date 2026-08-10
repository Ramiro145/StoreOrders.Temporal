using Microsoft.EntityFrameworkCore;
using StoreOrders.Domain.Abstractions;
using StoreOrders.Domain.ReadModels;
using StoreOrders.Infrastructure.Persistence;

namespace StoreOrders.Infrastructure.Queries;

public sealed class EfOrderReadService(
    StoreOrdersDbContext dbContext)
    : IOrderReadService
{
    public async Task<OrderReadModel?> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId no puede ser un GUID vacío.",
                nameof(orderId));
        }

        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(current => current.OrderId == orderId)
            .Select(current => new
            {
                current.OrderId,
                current.OrderNumber,
                current.Status,
                current.CustomerName,
                current.CustomerEmail,
                current.Currency,
                current.TotalAmount,
                current.CreatedAtUtc,
                current.UpdatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var address = await dbContext.OrderAddresses
            .AsNoTracking()
            .Where(current => current.OrderId == orderId)
            .Select(current => new OrderAddressReadModel(
                current.RecipientName,
                current.Line1,
                current.Line2,
                current.City,
                current.State,
                current.PostalCode,
                current.CountryCode,
                current.AddressVersion))
            .SingleAsync(cancellationToken);

        var items = await dbContext.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .OrderBy(item => item.ProductId)
            .Select(item => new OrderItemReadModel(
                item.ProductId,
                item.ProductSku,
                item.ProductName,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal))
            .ToArrayAsync(cancellationToken);

        var payment = await dbContext.Payments
            .AsNoTracking()
            .Where(current => current.OrderId == orderId)
            .Select(current => new OrderPaymentReadModel(
                current.ExternalPaymentReference,
                current.Amount,
                current.Currency,
                current.Status,
                current.ConfirmedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        var fulfillmentData = await dbContext.OrderFulfillments
            .AsNoTracking()
            .Where(current => current.OrderId == orderId)
            .Select(current => new
            {
                current.Status,
                current.PackedBy,
                current.PackedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        var fulfillment = fulfillmentData is null
            ? null
            : new OrderFulfillmentReadModel(
                fulfillmentData.Status.ToString(),
                fulfillmentData.PackedBy,
                fulfillmentData.PackedAtUtc);

        var shipmentData = await dbContext.Shipments
            .AsNoTracking()
            .Where(current => current.OrderId == orderId)
            .Select(current => new
            {
                current.Status,
                current.Carrier,
                current.TrackingNumber,
                current.ShippedAtUtc,
                current.DeliveredAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        var shipment = shipmentData is null
            ? null
            : new OrderShipmentReadModel(
                shipmentData.Status.ToString(),
                shipmentData.Carrier,
                shipmentData.TrackingNumber,
                shipmentData.ShippedAtUtc,
                shipmentData.DeliveredAtUtc);

        return new OrderReadModel(
            order.OrderId,
            order.OrderNumber,
            order.Status,
            order.CustomerName,
            order.CustomerEmail,
            order.Currency,
            order.TotalAmount,
            address,
            items,
            payment,
            fulfillment,
            shipment,
            order.CreatedAtUtc,
            order.UpdatedAtUtc);
    }
}

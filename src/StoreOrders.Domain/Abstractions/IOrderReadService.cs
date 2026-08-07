using StoreOrders.Domain.ReadModels;

namespace StoreOrders.Domain.Abstractions;

public interface IOrderReadService
{
    Task<OrderReadModel?> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}

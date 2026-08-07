using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;

namespace StoreOrders.Domain.Abstractions;

public interface IOrderOperations
{
    Task<CreateOrderResult> CreateOrderAsync(
        CreateOrderInput input,
        CancellationToken cancellationToken = default);

    Task<ReserveInventoryResult> ReserveInventoryAsync(
        ReserveInventoryInput input,
        CancellationToken cancellationToken = default);
}

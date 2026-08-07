using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Entities;

public sealed class OrderHistoryEntry
{
    public long HistoryId { get; set; }
    public Guid OrderId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public OrderStatus? PreviousStatus { get; set; }
    public OrderStatus CurrentStatus { get; set; }
    public ActorType ActorType { get; set; }
    public string? Description { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }

    public Order Order { get; set; } = null!;
}

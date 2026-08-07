using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Entities;

public sealed class OrderFulfillment
{
    public Guid FulfillmentId { get; set; }
    public Guid OrderId { get; set; }
    public FulfillmentStatus Status { get; set; }
    public string? PackedBy { get; set; }
    public string? OperationKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PackedAtUtc { get; set; }

    public Order Order { get; set; } = null!;
}

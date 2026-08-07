using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Entities;

public sealed class InventoryReservation
{
    public Guid ReservationId { get; set; }
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
    public ReservationStatus Status { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }

    public OrderItem OrderItem { get; set; } = null!;
}

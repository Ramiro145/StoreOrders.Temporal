using StoreOrders.Domain.Enums;

namespace StoreOrders.Domain.Entities;

public sealed class Order
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string ClientRequestId { get; set; } = string.Empty;
    public string TemporalWorkflowId { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Currency { get; set; } = "MXN";
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public OrderAddress? Address { get; set; }
    public ICollection<OrderItem> Items { get; set; } = [];
    public Payment? Payment { get; set; }
    public OrderFulfillment? Fulfillment { get; set; }
    public Shipment? Shipment { get; set; }
    public ICollection<OrderHistoryEntry> History { get; set; } = [];
}

namespace StoreOrders.Domain.Entities;

public sealed class Product
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public InventoryStock? InventoryStock { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = [];
}

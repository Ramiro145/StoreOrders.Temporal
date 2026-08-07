namespace StoreOrders.Domain.Entities;

public sealed class Payment
{
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public string ExternalPaymentReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MXN";
    public string Status { get; set; } = "Confirmed";
    public string OperationKey { get; set; } = string.Empty;
    public DateTime ConfirmedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Order Order { get; set; } = null!;
}

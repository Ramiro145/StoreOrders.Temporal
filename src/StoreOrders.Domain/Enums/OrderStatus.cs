namespace StoreOrders.Domain.Enums;

public enum OrderStatus
{
    Received,
    AwaitingPayment,
    Paid,
    Preparing,
    ReadyForShipment,
    Shipped,
    Delivered,
    Cancelled,
    Rejected
}

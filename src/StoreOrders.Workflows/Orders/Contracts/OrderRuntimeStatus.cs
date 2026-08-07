namespace StoreOrders.Workflows.Orders.Contracts;

public enum OrderWorkflowStage
{
    Initializing,
    ReservingInventory,
    AwaitingPayment,
    RecordingPayment,
    Preparing,
    ReadyForShipment,
    Shipped,
    Delivered,
    Cancelled,
    Rejected
}

public enum OrderWorkflowWaitingFor
{
    OrderCreation,
    InventoryReservation,
    PaymentConfirmed,
    PaymentProcessing,
    PackingCompleted,
    ShipmentShipped,
    ShipmentDelivered,
    None
}

public sealed record OrderRuntimeStatus(
    Guid OrderId,
    string WorkflowId,
    OrderWorkflowStage Stage,
    OrderWorkflowWaitingFor WaitingFor,
    bool PaymentReceived,
    bool PackingCompleted,
    bool DeliveryStarted,
    bool CanChangeAddress,
    bool CanCancel);

namespace StoreOrders.Workflows.Configuration;

public static class TemporalNames
{
    public const string TaskQueue = "store-orders-v1";

    public const string GetRuntimeStatusQuery =
        "GetRuntimeStatus";

    public const string PaymentConfirmedSignal =
        "PaymentConfirmed";

    public const string PackingCompletedSignal =
        "PackingCompleted";

    public const string ChangeDeliveryAddressUpdate =
        "ChangeDeliveryAddress";

    public const string CancelOrderUpdate =
        "CancelOrder";

    public const string ShipmentShippedSignal =
        "ShipmentShipped";

    public const string ShipmentDeliveredSignal =
        "ShipmentDelivered";

    public const string DeliveryProgressChangedSignal =
        "DeliveryProgressChanged";

    public const string CancelDeliverySignal =
        "CancelDelivery";

    public static string OrderWorkflowId(Guid orderId)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId no puede ser un GUID vacío.",
                nameof(orderId));
        }

        return $"order-{orderId:D}";
    }

    public static string DeliveryWorkflowId(Guid orderId)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException(
                "OrderId no puede ser un GUID vacío.",
                nameof(orderId));
        }

        return $"delivery-{orderId:D}";
    }
}

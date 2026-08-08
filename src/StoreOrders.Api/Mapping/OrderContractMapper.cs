using StoreOrders.Api.Contracts.Orders;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.ReadModels;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Orders.Contracts;

namespace StoreOrders.Api.Mapping;

public static class OrderContractMapper
{
    public static StartOrderInput ToWorkflowInput(
        CreateOrderRequest request,
        Guid orderId)
    {
        return new StartOrderInput(
            orderId,
            orderId.ToString("D"),
            TemporalNames.OrderWorkflowId(orderId),
            request.Customer.Name,
            request.Customer.Email,
            new CreateOrderAddressInput(
                request.DeliveryAddress.RecipientName,
                request.DeliveryAddress.Line1,
                request.DeliveryAddress.Line2,
                request.DeliveryAddress.City,
                request.DeliveryAddress.State,
                request.DeliveryAddress.PostalCode,
                request.DeliveryAddress.CountryCode),
            request.Items
                .Select(item => new CreateOrderItemInput(
                    item.ProductId,
                    item.Quantity))
                .ToArray());
    }

    public static CreateOrderResponse ToCreateResponse(Guid orderId)
    {
        var orderLink = $"/api/orders/{orderId:D}";

        return new CreateOrderResponse(
            orderId,
            TemporalNames.OrderWorkflowId(orderId),
            "Accepted",
            "El pedido fue aceptado para procesamiento.",
            new CreateOrderLinksResponse(
                orderLink,
                $"{orderLink}/runtime-status"));
    }

    public static OrderResponse ToResponse(OrderReadModel model)
    {
        return new OrderResponse(
            model.OrderId,
            model.OrderNumber,
            model.Status,
            new OrderCustomerResponse(
                model.CustomerName,
                model.CustomerEmail),
            model.Currency,
            model.TotalAmount,
            new OrderAddressResponse(
                model.DeliveryAddress.RecipientName,
                model.DeliveryAddress.Line1,
                model.DeliveryAddress.Line2,
                model.DeliveryAddress.City,
                model.DeliveryAddress.State,
                model.DeliveryAddress.PostalCode,
                model.DeliveryAddress.CountryCode,
                model.DeliveryAddress.AddressVersion),
            model.Items
                .Select(item => new OrderItemResponse(
                    item.ProductId,
                    item.Sku,
                    item.Name,
                    item.Quantity,
                    item.UnitPrice,
                    item.LineTotal))
                .ToArray(),
            model.Payment is null
                ? null
                : new OrderPaymentResponse(
                    model.Payment.ExternalPaymentReference,
                    model.Payment.Amount,
                    model.Payment.Currency,
                    model.Payment.Status,
                    model.Payment.ConfirmedAtUtc),
            model.Fulfillment is null
                ? null
                : new OrderFulfillmentResponse(
                    model.Fulfillment.Status,
                    model.Fulfillment.PackedBy,
                    model.Fulfillment.PackedAtUtc),
            model.Shipment is null
                ? null
                : new OrderShipmentResponse(
                    model.Shipment.Status,
                    model.Shipment.Carrier,
                    model.Shipment.TrackingNumber,
                    model.Shipment.ShippedAtUtc,
                    model.Shipment.DeliveredAtUtc),
            model.CreatedAtUtc,
            model.UpdatedAtUtc);
    }

    public static OrderRuntimeStatusResponse ToResponse(
        OrderRuntimeStatus status)
    {
        return new OrderRuntimeStatusResponse(
            status.OrderId,
            status.WorkflowId,
            status.Stage.ToString(),
            status.WaitingFor.ToString(),
            status.PaymentReceived,
            status.PackingCompleted,
            status.DeliveryStarted,
            status.CanChangeAddress,
            status.CanCancel);
    }

    public static PaymentConfirmedSignal ToSignal(
        PaymentConfirmedRequest request)
    {
        return new PaymentConfirmedSignal(
            request.EventId,
            request.ExternalPaymentReference,
            request.Amount,
            request.Currency,
            request.ConfirmedAtUtc.UtcDateTime);
    }

    public static PackingCompletedSignal ToSignal(
        PackingCompletedRequest request)
    {
        return new PackingCompletedSignal(
            request.EventId,
            request.PackedBy,
            request.PackedAtUtc.UtcDateTime);
    }

    public static ChangeAddressUpdate ToUpdate(
        ChangeDeliveryAddressRequest request)
    {
        return new ChangeAddressUpdate(
            request.OperationId,
            request.RecipientName,
            request.Line1,
            request.Line2,
            request.City,
            request.State,
            request.PostalCode,
            request.CountryCode);
    }

    public static ChangeDeliveryAddressResponse ToResponse(
        ChangeAddressUpdateResult result)
    {
        return new ChangeDeliveryAddressResponse(
            result.OperationId,
            result.OrderId,
            result.Accepted,
            result.AddressVersion,
            result.Message);
    }

    public static CancelOrderUpdate ToUpdate(
        CancelOrderRequest request)
    {
        return new CancelOrderUpdate(
            request.OperationId,
            request.Reason,
            request.RequestedBy);
    }

    public static CancelOrderResponse ToResponse(
        CancelOrderUpdateResult result)
    {
        return new CancelOrderResponse(
            result.OperationId,
            result.OrderId,
            result.Accepted,
            result.PreviousStatus.ToString(),
            result.CurrentStatus.ToString(),
            result.ReleasedReservationCount,
            result.Message);
    }

    public static WorkflowEventAcceptedResponse ToAcceptedResponse(
        Guid orderId,
        Guid eventId,
        string message)
    {
        return new WorkflowEventAcceptedResponse(
            orderId,
            eventId,
            "Accepted",
            message);
    }
}

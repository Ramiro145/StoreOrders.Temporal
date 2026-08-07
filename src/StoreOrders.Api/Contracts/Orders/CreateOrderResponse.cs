namespace StoreOrders.Api.Contracts.Orders;

public sealed record CreateOrderResponse(
    Guid OrderId,
    string WorkflowId,
    string RequestStatus,
    string Message,
    CreateOrderLinksResponse Links);

public sealed record CreateOrderLinksResponse(
    string Order,
    string RuntimeStatus);

public sealed record OrderProcessingResponse(
    Guid OrderId,
    string RequestStatus,
    string Message);

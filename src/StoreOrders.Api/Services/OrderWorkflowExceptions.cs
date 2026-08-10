namespace StoreOrders.Api.Services;

public sealed class OrderWorkflowUnavailableException(
    string message,
    Exception innerException)
    : Exception(message, innerException);

public sealed class OrderWorkflowConflictException(
    string message,
    Exception innerException)
    : Exception(message, innerException);

public sealed class OrderWorkflowNotFoundException(
    string message,
    Exception innerException)
    : Exception(message, innerException);

public sealed class DeliveryWorkflowNotReadyException(
    string message,
    Exception innerException)
    : Exception(message, innerException);

public sealed class DeliveryWorkflowUnavailableException(
    string message,
    Exception innerException)
    : Exception(message, innerException);

using Microsoft.AspNetCore.Mvc;
using StoreOrders.Api.Contracts.Deliveries;
using StoreOrders.Api.Mapping;
using StoreOrders.Api.Services;

namespace StoreOrders.Api.Controllers;

[ApiController]
[Route("api/deliveries")]
public sealed class DeliveriesController(
    IDeliveryWorkflowGateway workflowGateway)
    : ControllerBase
{
    [HttpPost("{orderId:guid}/shipped")]
    public async Task<ActionResult<DeliveryEventAcceptedResponse>>
        ShipmentShippedAsync(
            Guid orderId,
            [FromBody] ShipmentShippedRequest request,
            CancellationToken cancellationToken)
    {
        if (!ValidateEvent(
                request.EventId,
                request.ShippedAtUtc,
                "shippedAtUtc"))
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            await workflowGateway.SignalShippedAsync(
                orderId,
                DeliveryContractMapper.ToSignal(request),
                cancellationToken);

            return Accepted(
                DeliveryContractMapper.ToAcceptedResponse(
                    orderId,
                    request.EventId));
        }
        catch (DeliveryWorkflowNotReadyException exception)
        {
            return DeliveryNotReady(orderId, exception.Message);
        }
        catch (DeliveryWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Temporal no está disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("{orderId:guid}/delivered")]
    public async Task<ActionResult<DeliveryEventAcceptedResponse>>
        ShipmentDeliveredAsync(
            Guid orderId,
            [FromBody] ShipmentDeliveredRequest request,
            CancellationToken cancellationToken)
    {
        if (!ValidateEvent(
                request.EventId,
                request.DeliveredAtUtc,
                "deliveredAtUtc"))
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            await workflowGateway.SignalDeliveredAsync(
                orderId,
                DeliveryContractMapper.ToSignal(request),
                cancellationToken);

            return Accepted(
                DeliveryContractMapper.ToAcceptedResponse(
                    orderId,
                    request.EventId));
        }
        catch (DeliveryWorkflowNotReadyException exception)
        {
            return DeliveryNotReady(orderId, exception.Message);
        }
        catch (DeliveryWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Temporal no está disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    private ObjectResult DeliveryNotReady(
        Guid orderId,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Type =
                "https://storeorders.local/problems/" +
                "delivery-not-ready",
            Title = "La entrega no está lista",
            Detail = detail,
            Status = StatusCodes.Status409Conflict
        };

        problem.Extensions["code"] = "delivery_not_ready";
        problem.Extensions["orderId"] = orderId;

        return Conflict(problem);
    }

    private bool ValidateEvent(
        Guid eventId,
        DateTimeOffset occurredAtUtc,
        string dateField)
    {
        if (eventId == Guid.Empty)
        {
            ModelState.AddModelError(
                "eventId",
                "EventId debe contener un GUID válido.");
        }

        if (occurredAtUtc == default)
        {
            ModelState.AddModelError(
                dateField,
                "La fecha del evento es obligatoria.");
        }

        return ModelState.IsValid;
    }
}

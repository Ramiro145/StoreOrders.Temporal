using Microsoft.AspNetCore.Mvc;
using StoreOrders.Api.Contracts.Orders;
using StoreOrders.Api.Mapping;
using StoreOrders.Api.Services;
using StoreOrders.Domain.Abstractions;

namespace StoreOrders.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(
    IOrderReadService orderReadService,
    IOrderWorkflowGateway workflowGateway)
    : ControllerBase
{
    private const string GetOrderByIdRouteName =
        "GetOrderById";
    [HttpPost]
    public async Task<ActionResult<CreateOrderResponse>> CreateAsync(
        [FromHeader(Name = "Idempotency-Key")]
        string? idempotencyKey,
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(idempotencyKey, out var orderId) ||
            orderId == Guid.Empty)
        {
            ModelState.AddModelError(
                "Idempotency-Key",
                "Idempotency-Key debe contener un GUID válido.");

            return ValidationProblem(ModelState);
        }

        var workflowInput =
            OrderContractMapper.ToWorkflowInput(
                request,
                orderId);

        try
        {
            await workflowGateway.StartAsync(
                workflowInput,
                cancellationToken);

            var response =
                OrderContractMapper.ToCreateResponse(orderId);

            return AcceptedAtRoute(
                GetOrderByIdRouteName,
                new { orderId },
                response);
        }
        catch (OrderWorkflowConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflicto de identificador",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (OrderWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Temporal no está disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("{orderId:guid}", Name = GetOrderByIdRouteName)]
    public async Task<ActionResult<OrderResponse>> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await orderReadService.GetByIdAsync(
            orderId,
            cancellationToken);

        if (order is not null)
        {
            return Ok(OrderContractMapper.ToResponse(order));
        }

        try
        {
            if (await workflowGateway.ExistsAsync(
                    orderId,
                    cancellationToken))
            {
                return Accepted(new OrderProcessingResponse(
                    orderId,
                    "Processing",
                    "El proceso existe, pero el pedido todavía no está disponible en SQL Server."));
            }

            return NotFound(new ProblemDetails
            {
                Title = "Pedido no encontrado",
                Detail =
                    "No existe el pedido ni un Workflow con ese identificador.",
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OrderWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Temporal no está disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("{orderId:guid}/runtime-status")]
    public async Task<ActionResult<OrderRuntimeStatusResponse>>
        GetRuntimeStatusAsync(
            Guid orderId,
            CancellationToken cancellationToken)
    {
        try
        {
            var status =
                await workflowGateway.GetRuntimeStatusAsync(
                    orderId,
                    cancellationToken);

            return Ok(OrderContractMapper.ToResponse(status));
        }
        catch (OrderWorkflowNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Workflow no encontrado",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OrderWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Estado interno no disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("{orderId:guid}/payment-confirmed")]
    public async Task<ActionResult<WorkflowEventAcceptedResponse>>
        ConfirmPaymentAsync(
            Guid orderId,
            [FromBody] PaymentConfirmedRequest request,
            CancellationToken cancellationToken)
    {
        if (!ValidateEvent(
                request.EventId,
                request.ConfirmedAtUtc,
                "confirmedAtUtc"))
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            await workflowGateway.SignalPaymentConfirmedAsync(
                orderId,
                OrderContractMapper.ToSignal(request),
                cancellationToken);

            return Accepted(
                OrderContractMapper.ToAcceptedResponse(
                    orderId,
                    request.EventId,
                    "Temporal recibió la confirmación de pago."));
        }
        catch (OrderWorkflowNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Workflow no encontrado",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OrderWorkflowConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "El pedido no admite pagos",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (OrderWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Temporal no está disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("{orderId:guid}/packing-completed")]
    public async Task<ActionResult<WorkflowEventAcceptedResponse>>
        CompletePackingAsync(
            Guid orderId,
            [FromBody] PackingCompletedRequest request,
            CancellationToken cancellationToken)
    {
        if (!ValidateEvent(
                request.EventId,
                request.PackedAtUtc,
                "packedAtUtc"))
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            await workflowGateway.SignalPackingCompletedAsync(
                orderId,
                OrderContractMapper.ToSignal(request),
                cancellationToken);

            return Accepted(
                OrderContractMapper.ToAcceptedResponse(
                    orderId,
                    request.EventId,
                    "Temporal recibió la confirmación de preparación."));
        }
        catch (OrderWorkflowNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Workflow no encontrado",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OrderWorkflowConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "El pedido no admite preparación",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (OrderWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Temporal no está disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPut("{orderId:guid}/delivery-address")]
    public async Task<ActionResult<ChangeDeliveryAddressResponse>>
        ChangeDeliveryAddressAsync(
            Guid orderId,
            [FromBody] ChangeDeliveryAddressRequest request,
            CancellationToken cancellationToken)
    {
        if (request.OperationId == Guid.Empty)
        {
            ModelState.AddModelError(
                "operationId",
                "OperationId debe contener un GUID válido.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var result =
                await workflowGateway.ChangeDeliveryAddressAsync(
                    orderId,
                    OrderContractMapper.ToUpdate(request),
                    cancellationToken);

            if (!result.Accepted)
            {
                var problem = new ProblemDetails
                {
                    Type =
                        "https://storeorders.local/problems/" +
                        "address-change-not-allowed",
                    Title = "No se puede cambiar la dirección",
                    Detail = result.Message,
                    Status = StatusCodes.Status409Conflict
                };

                problem.Extensions["code"] =
                    "address_change_not_allowed";
                problem.Extensions["orderId"] = orderId;

                return Conflict(problem);
            }

            return Ok(OrderContractMapper.ToResponse(result));
        }
        catch (OrderWorkflowNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Workflow no encontrado",
                Detail = exception.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OrderWorkflowConflictException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "No se puede cambiar la dirección",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (OrderWorkflowUnavailableException exception)
        {
            return Problem(
                title: "Temporal no está disponible",
                detail: exception.Message,
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
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

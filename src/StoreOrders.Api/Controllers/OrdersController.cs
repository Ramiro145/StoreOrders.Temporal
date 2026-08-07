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
}

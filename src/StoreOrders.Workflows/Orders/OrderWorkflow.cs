using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Workflows.Activities;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Deliveries;
using StoreOrders.Workflows.Deliveries.Contracts;
using StoreOrders.Workflows.Orders.Contracts;
using Temporalio.Workflows;

namespace StoreOrders.Workflows.Orders;

[Workflow]
public sealed class OrderWorkflow
{
    private const string WaitForPaymentPatch =
        "order-wait-for-payment-v1";

    private const string DeliveryChildWorkflowPatch =
        "order-delivery-child-v1";

    private readonly Guid orderId;
    private readonly string workflowId;
    private readonly Queue<PaymentConfirmedSignal> pendingPayments = [];
    private readonly Queue<PackingCompletedSignal> pendingPacking = [];
    private readonly HashSet<Guid> receivedPaymentEventIds = [];
    private readonly HashSet<Guid> receivedPackingEventIds = [];
    private readonly Temporalio.Workflows.Mutex mutationMutex = new();

    private ChildWorkflowHandle<
        DeliveryWorkflow,
        DeliveryWorkflowResult>? deliveryHandle;

    private OrderWorkflowStage stage =
        OrderWorkflowStage.Initializing;

    private OrderWorkflowWaitingFor waitingFor =
        OrderWorkflowWaitingFor.OrderCreation;

    private bool paymentReceived;
    private bool packingCompleted;
    private bool deliveryStarted;
    private bool orderCreated;
    private bool isTerminal;

    [WorkflowInit]
    public OrderWorkflow(StartOrderInput input)
    {
        orderId = input.OrderId;
        workflowId = TemporalNames.OrderWorkflowId(input.OrderId);

        if (input.OrderId == Guid.Empty ||
            !string.Equals(
                input.TemporalWorkflowId,
                workflowId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La identidad inicial del Workflow no es válida.");
        }
    }

    [WorkflowRun]
    public async Task<OrderWorkflowResult> RunAsync(
        StartOrderInput input)
    {
        await Workflow.ExecuteActivityAsync(
            (OrderActivities activities) =>
                activities.CreateOrderAsync(
                    input.ToCreateOrderInput()),
            ActivityOptionsFactory.CreateDefault());

        orderCreated = true;

        stage = OrderWorkflowStage.ReservingInventory;
        waitingFor =
            OrderWorkflowWaitingFor.InventoryReservation;

        ReserveInventoryResult? reservationResult = null;

        await mutationMutex.WaitOneAsync();

        try
        {
            if (!isTerminal)
            {
                reservationResult =
                    await Workflow.ExecuteActivityAsync(
                        (OrderActivities activities) =>
                            activities.ReserveInventoryAsync(
                                new ReserveInventoryInput(orderId)),
                        ActivityOptionsFactory.CreateDefault());
            }
        }
        finally
        {
            mutationMutex.ReleaseMutex();
        }

        if (isTerminal)
        {
            return await CompleteTerminalResultAsync();
        }

        if (reservationResult is null)
        {
            throw new InvalidOperationException(
                "La reserva de inventario no produjo un resultado.");
        }

        if (reservationResult.Outcome ==
            ReserveInventoryOutcome.InsufficientInventory)
        {
            stage = OrderWorkflowStage.Rejected;
            waitingFor = OrderWorkflowWaitingFor.None;
            isTerminal = true;

            return await CompleteTerminalResultAsync();
        }

        if (reservationResult.Outcome is not
            (ReserveInventoryOutcome.Reserved or
             ReserveInventoryOutcome.AlreadyReserved))
        {
            throw new InvalidOperationException(
                $"Resultado de reserva desconocido: " +
                $"{reservationResult.Outcome}.");
        }

        stage = OrderWorkflowStage.AwaitingPayment;
        waitingFor = OrderWorkflowWaitingFor.PaymentConfirmed;

        // Las ejecuciones creadas con el Incremento 5 terminaban aquí.
        // El patch permite reproducir sus historiales sin no determinismo.
        if (!Workflow.Patched(WaitForPaymentPatch))
        {
            return new OrderWorkflowResult(
                orderId,
                OrderStatus.AwaitingPayment,
                "Pedido creado y con inventario reservado.");
        }

        await ProcessPaymentsAsync();

        if (isTerminal)
        {
            return await CompleteTerminalResultAsync();
        }

        await StartFulfillmentAsync();

        if (isTerminal)
        {
            return await CompleteTerminalResultAsync();
        }

        await ProcessPackingAsync();

        if (isTerminal)
        {
            return await CompleteTerminalResultAsync();
        }

        // Mantiene compatibles los historiales creados antes de que
        // existiera el Child Workflow de entrega.
        if (!Workflow.Patched(DeliveryChildWorkflowPatch))
        {
            await Workflow.WaitConditionAsync(() => isTerminal);
            return await CompleteTerminalResultAsync();
        }

        await StartDeliveryAsync();

        if (isTerminal && deliveryHandle is null)
        {
            return await CompleteTerminalResultAsync();
        }

        var deliveryResult = await deliveryHandle!.GetResultAsync();

        if (deliveryResult.Status == ShipmentStatus.Delivered)
        {
            stage = OrderWorkflowStage.Delivered;
            waitingFor = OrderWorkflowWaitingFor.None;
            isTerminal = true;
        }
        else if (deliveryResult.Status == ShipmentStatus.Cancelled)
        {
            stage = OrderWorkflowStage.Cancelled;
            waitingFor = OrderWorkflowWaitingFor.None;
            isTerminal = true;
        }
        else
        {
            throw new InvalidOperationException(
                $"Resultado de entrega desconocido: " +
                $"{deliveryResult.Status}.");
        }

        return await CompleteTerminalResultAsync();
    }

    [WorkflowQuery(TemporalNames.GetRuntimeStatusQuery)]
    public OrderRuntimeStatus GetRuntimeStatus()
    {
        var canModify =
            !isTerminal &&
            stage is not
                (OrderWorkflowStage.Shipped or
                 OrderWorkflowStage.Delivered or
                 OrderWorkflowStage.Cancelled or
                 OrderWorkflowStage.Rejected);

        return new OrderRuntimeStatus(
            orderId,
            workflowId,
            stage,
            waitingFor,
            paymentReceived,
            packingCompleted,
            deliveryStarted,
            CanChangeAddress: canModify,
            CanCancel: canModify);
    }

    [WorkflowSignal(TemporalNames.PaymentConfirmedSignal)]
    public Task PaymentConfirmedAsync(
        PaymentConfirmedSignal signal)
    {
        if (!isTerminal &&
            signal.EventId != Guid.Empty &&
            receivedPaymentEventIds.Add(signal.EventId))
        {
            pendingPayments.Enqueue(signal);
        }

        return Task.CompletedTask;
    }

    [WorkflowSignal(TemporalNames.PackingCompletedSignal)]
    public Task PackingCompletedAsync(
        PackingCompletedSignal signal)
    {
        if (!isTerminal &&
            signal.EventId != Guid.Empty &&
            receivedPackingEventIds.Add(signal.EventId))
        {
            pendingPacking.Enqueue(signal);
        }

        return Task.CompletedTask;
    }

    [WorkflowSignal(TemporalNames.DeliveryProgressChangedSignal)]
    public Task DeliveryProgressChangedAsync(
        DeliveryProgressSignal signal)
    {
        if (signal.OrderId != orderId ||
            isTerminal)
        {
            return Task.CompletedTask;
        }

        deliveryStarted = true;

        if (signal.Status == ShipmentStatus.Shipped)
        {
            stage = OrderWorkflowStage.Shipped;
            waitingFor =
                OrderWorkflowWaitingFor.ShipmentDelivered;
        }
        else if (signal.Status == ShipmentStatus.Delivered)
        {
            stage = OrderWorkflowStage.Delivered;
            waitingFor = OrderWorkflowWaitingFor.None;
            isTerminal = true;
        }

        return Task.CompletedTask;
    }

    [WorkflowUpdate(TemporalNames.ChangeDeliveryAddressUpdate)]
    public async Task<ChangeAddressUpdateResult>
        ChangeDeliveryAddressAsync(ChangeAddressUpdate update)
    {
        await Workflow.WaitConditionAsync(
            () => orderCreated || isTerminal);

        await mutationMutex.WaitOneAsync();

        try
        {
            var result = await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.ChangeDeliveryAddressAsync(
                        update.ToInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

            return new ChangeAddressUpdateResult(
                update.OperationId,
                result.OrderId,
                result.Outcome is not
                    ChangeDeliveryAddressOutcome.NotAllowed,
                result.AddressVersion,
                result.Message);
        }
        finally
        {
            mutationMutex.ReleaseMutex();
        }
    }

    [WorkflowUpdateValidator(nameof(ChangeDeliveryAddressAsync))]
    public void ValidateChangeDeliveryAddress(
        ChangeAddressUpdate update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        if (update.OperationId == Guid.Empty)
        {
            throw new ArgumentException(
                "OperationId debe contener un GUID válido.",
                nameof(update));
        }

        ValidateRequiredText(
            update.RecipientName,
            150,
            nameof(update.RecipientName));

        ValidateRequiredText(
            update.Line1,
            200,
            nameof(update.Line1));

        if (update.Line2?.Length > 200)
        {
            throw new ArgumentException(
                "Line2 no puede exceder 200 caracteres.",
                nameof(update));
        }

        ValidateRequiredText(
            update.City,
            100,
            nameof(update.City));

        ValidateRequiredText(
            update.State,
            100,
            nameof(update.State));

        ValidateRequiredText(
            update.PostalCode,
            20,
            nameof(update.PostalCode));

        if (string.IsNullOrWhiteSpace(update.CountryCode) ||
            update.CountryCode.Trim().Length != 2 ||
            !update.CountryCode.Trim().All(char.IsLetter))
        {
            throw new ArgumentException(
                "CountryCode debe contener dos letras.",
                nameof(update));
        }
    }

    [WorkflowUpdate(TemporalNames.CancelOrderUpdate)]
    public async Task<CancelOrderUpdateResult> CancelOrderAsync(
        CancelOrderUpdate update)
    {
        await Workflow.WaitConditionAsync(
            () => orderCreated || isTerminal);

        await mutationMutex.WaitOneAsync();

        try
        {
            var result = await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.CancelOrderAsync(
                        update.ToInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

            var accepted = result.Outcome is
                CancelOrderOutcome.Cancelled or
                CancelOrderOutcome.AlreadyCancelled;

            if (accepted)
            {
                stage = OrderWorkflowStage.Cancelled;
                waitingFor = OrderWorkflowWaitingFor.None;
                isTerminal = true;

                if (deliveryHandle is not null &&
                    result.PreviousStatus !=
                        OrderStatus.Cancelled)
                {
                    await deliveryHandle.SignalAsync(
                        workflow => workflow.CancelDeliveryAsync(
                            new CancelDeliverySignal(
                                update.OperationId)));
                }
            }

            return new CancelOrderUpdateResult(
                update.OperationId,
                result.OrderId,
                accepted,
                result.PreviousStatus,
                result.CurrentStatus,
                result.ReleasedReservationCount,
                result.Message);
        }
        finally
        {
            mutationMutex.ReleaseMutex();
        }
    }

    [WorkflowUpdateValidator(nameof(CancelOrderAsync))]
    public void ValidateCancelOrder(CancelOrderUpdate update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        if (update.OperationId == Guid.Empty)
        {
            throw new ArgumentException(
                "OperationId debe contener un GUID válido.",
                nameof(update));
        }

        ValidateRequiredText(
            update.Reason,
            400,
            nameof(update.Reason));

        ValidateRequiredText(
            update.RequestedBy,
            30,
            nameof(update.RequestedBy));

        if (!Enum.TryParse<ActorType>(
                update.RequestedBy.Trim(),
                ignoreCase: true,
                out var actor) ||
            !Enum.IsDefined(actor))
        {
            throw new ArgumentException(
                "RequestedBy debe ser System, Customer, " +
                "PaymentService, Warehouse o DeliveryService.",
                nameof(update));
        }
    }

    private async Task ProcessPaymentsAsync()
    {
        while (!paymentReceived)
        {
            stage = OrderWorkflowStage.AwaitingPayment;
            waitingFor =
                OrderWorkflowWaitingFor.PaymentConfirmed;

            await Workflow.WaitConditionAsync(
                () => pendingPayments.Count > 0 || isTerminal);

            if (isTerminal)
            {
                return;
            }

            var signal = pendingPayments.Dequeue();

            stage = OrderWorkflowStage.RecordingPayment;
            waitingFor =
                OrderWorkflowWaitingFor.PaymentProcessing;

            await mutationMutex.WaitOneAsync();

            try
            {
                if (isTerminal)
                {
                    return;
                }

                var result = await Workflow.ExecuteActivityAsync(
                    (OrderActivities activities) =>
                        activities.ConfirmPaymentAsync(
                            signal.ToInput(orderId)),
                    ActivityOptionsFactory.CreateDefault());

                paymentReceived = result.Outcome is
                    ConfirmPaymentOutcome.Confirmed or
                    ConfirmPaymentOutcome.AlreadyConfirmed;
            }
            finally
            {
                mutationMutex.ReleaseMutex();
            }
        }
    }

    private async Task StartFulfillmentAsync()
    {
        await mutationMutex.WaitOneAsync();

        try
        {
            if (isTerminal)
            {
                return;
            }

            var result = await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.StartFulfillmentAsync(
                        new StartFulfillmentInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

            if (result.Outcome is not
                (StartFulfillmentOutcome.Started or
                 StartFulfillmentOutcome.AlreadyStarted))
            {
                throw new InvalidOperationException(
                    "El pago fue confirmado, pero no fue posible " +
                    "iniciar la preparación.");
            }

            stage = OrderWorkflowStage.Preparing;
            waitingFor = OrderWorkflowWaitingFor.PackingCompleted;
        }
        finally
        {
            mutationMutex.ReleaseMutex();
        }
    }

    private async Task ProcessPackingAsync()
    {
        while (!packingCompleted)
        {
            await Workflow.WaitConditionAsync(
                () => pendingPacking.Count > 0 || isTerminal);

            if (isTerminal)
            {
                return;
            }

            var signal = pendingPacking.Dequeue();

            await mutationMutex.WaitOneAsync();

            try
            {
                if (isTerminal)
                {
                    return;
                }

                var result = await Workflow.ExecuteActivityAsync(
                    (OrderActivities activities) =>
                        activities.CompletePackingAsync(
                            signal.ToInput(orderId)),
                    ActivityOptionsFactory.CreateDefault());

                packingCompleted = result.Outcome is
                    CompletePackingOutcome.Packed or
                    CompletePackingOutcome.AlreadyPacked;

                if (packingCompleted)
                {
                    stage = OrderWorkflowStage.ReadyForShipment;
                    waitingFor =
                        OrderWorkflowWaitingFor.ShipmentShipped;
                }
            }
            finally
            {
                mutationMutex.ReleaseMutex();
            }
        }
    }

    private async Task StartDeliveryAsync()
    {
        await mutationMutex.WaitOneAsync();

        try
        {
            if (isTerminal)
            {
                return;
            }

            var deliveryWorkflowId =
                TemporalNames.DeliveryWorkflowId(orderId);

            deliveryHandle = await Workflow.StartChildWorkflowAsync(
                (DeliveryWorkflow workflow) =>
                    workflow.RunAsync(
                        new StartDeliveryInput(
                            orderId,
                            workflowId,
                            deliveryWorkflowId)),
                new ChildWorkflowOptions
                {
                    Id = deliveryWorkflowId,
                    TaskQueue = TemporalNames.TaskQueue
                });

            deliveryStarted = true;

            if (stage is not
                (OrderWorkflowStage.Shipped or
                 OrderWorkflowStage.Delivered))
            {
                stage = OrderWorkflowStage.ReadyForShipment;
                waitingFor =
                    OrderWorkflowWaitingFor.ShipmentShipped;
            }
        }
        finally
        {
            mutationMutex.ReleaseMutex();
        }
    }

    private async Task<OrderWorkflowResult>
        CompleteTerminalResultAsync()
    {
        await Workflow.WaitConditionAsync(
            () => Workflow.AllHandlersFinished);

        return new OrderWorkflowResult(
            orderId,
            ToTerminalOrderStatus(),
            stage == OrderWorkflowStage.Cancelled
                ? "El pedido fue cancelado."
                : stage == OrderWorkflowStage.Rejected
                    ? "Pedido rechazado por inventario insuficiente."
                    : "El pedido fue entregado.");
    }

    private OrderStatus ToTerminalOrderStatus()
    {
        return stage switch
        {
            OrderWorkflowStage.Delivered =>
                OrderStatus.Delivered,
            OrderWorkflowStage.Cancelled =>
                OrderStatus.Cancelled,
            OrderWorkflowStage.Rejected =>
                OrderStatus.Rejected,
            _ => throw new InvalidOperationException(
                $"La etapa {stage} no es terminal.")
        };
    }

    private static void ValidateRequiredText(
        string value,
        int maximumLength,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{propertyName} es obligatorio y no puede exceder " +
                $"{maximumLength} caracteres.",
                propertyName);
        }
    }
}

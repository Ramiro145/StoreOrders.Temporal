using StoreOrders.Domain.Enums;
using StoreOrders.Domain.Operations.Inputs;
using StoreOrders.Domain.Operations.Results;
using StoreOrders.Workflows.Activities;
using StoreOrders.Workflows.Configuration;
using StoreOrders.Workflows.Orders.Contracts;
using Temporalio.Workflows;

namespace StoreOrders.Workflows.Orders;

[Workflow]
public sealed class OrderWorkflow
{
    private const string WaitForPaymentPatch =
        "order-wait-for-payment-v1";

    private readonly Guid orderId;
    private readonly string workflowId;
    private readonly Queue<PaymentConfirmedSignal> pendingPayments = [];
    private readonly Queue<PackingCompletedSignal> pendingPacking = [];
    private readonly HashSet<Guid> receivedPaymentEventIds = [];
    private readonly HashSet<Guid> receivedPackingEventIds = [];
    private readonly Temporalio.Workflows.Mutex updateMutex = new();

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

        var reservationResult =
            await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.ReserveInventoryAsync(
                        new ReserveInventoryInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

        if (reservationResult.Outcome ==
            ReserveInventoryOutcome.InsufficientInventory)
        {
            stage = OrderWorkflowStage.Rejected;
            waitingFor = OrderWorkflowWaitingFor.None;
            isTerminal = true;

            await Workflow.WaitConditionAsync(
                () => Workflow.AllHandlersFinished);

            return new OrderWorkflowResult(
                orderId,
                OrderStatus.Rejected,
                "Pedido rechazado por inventario insuficiente.");
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
        await StartFulfillmentAsync();
        await ProcessPackingAsync();

        // El Incremento 11 sustituirá esta espera por DeliveryWorkflow.
        await Workflow.WaitConditionAsync(() => isTerminal);
        await Workflow.WaitConditionAsync(
            () => Workflow.AllHandlersFinished);

        return new OrderWorkflowResult(
            orderId,
            ToTerminalOrderStatus(),
            "El proceso del pedido terminó.");
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

    [WorkflowUpdate(TemporalNames.ChangeDeliveryAddressUpdate)]
    public async Task<ChangeAddressUpdateResult>
        ChangeDeliveryAddressAsync(ChangeAddressUpdate update)
    {
        await Workflow.WaitConditionAsync(
            () => orderCreated || isTerminal);

        await updateMutex.WaitOneAsync();

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
            updateMutex.ReleaseMutex();
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

            var result = await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.ConfirmPaymentAsync(
                        signal.ToInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

            paymentReceived = result.Outcome is
                ConfirmPaymentOutcome.Confirmed or
                ConfirmPaymentOutcome.AlreadyConfirmed;
        }
    }

    private async Task StartFulfillmentAsync()
    {
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

            var result = await Workflow.ExecuteActivityAsync(
                (OrderActivities activities) =>
                    activities.CompletePackingAsync(
                        signal.ToInput(orderId)),
                ActivityOptionsFactory.CreateDefault());

            packingCompleted = result.Outcome is
                CompletePackingOutcome.Packed or
                CompletePackingOutcome.AlreadyPacked;
        }

        stage = OrderWorkflowStage.ReadyForShipment;
        waitingFor = OrderWorkflowWaitingFor.ShipmentShipped;
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

using System.Buffers.Text;
using System.Security.Cryptography;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Orders;

/// <summary>A step the customer follows the order by (Fase 24C, ADR 0023). Not a state of the order: several states are one step.</summary>
public enum OrderTrackingStep
{
    Received = 0,
    /// <summary>Confirmed or accepted by the business («Aceptado» for a service).</summary>
    Confirmed = 1,
    /// <summary>In preparation («En proceso» for a service).</summary>
    Preparing = 2,
    Ready = 3,
    /// <summary>Went out with the courier (Fase 22D): only for an order to the customer's address.</summary>
    OnTheWay = 4,
    /// <summary>Delivered («Terminado» for a service).</summary>
    Delivered = 5,
    Cancelled = 6
}

public enum OrderTrackingStepState
{
    Done = 0,
    Current = 1,
    Upcoming = 2
}

public class OrderTrackingStepDto
{
    public OrderTrackingStep Step { get; set; }
    /// <summary>What the customer reads, by the kind of order: the client shows it as is.</summary>
    public string Label { get; set; } = null!;
    public OrderTrackingStepState State { get; set; }
    /// <summary>When the order reached it; none while it has not, or when the business skipped it.</summary>
    public DateTimeOffset? At { get; set; }
}

/// <summary>A change of state of the order, as its history keeps it.</summary>
public readonly record struct OrderStatusChange(OrderStatus PreviousStatus, OrderStatus NewStatus, DateTimeOffset ChangedAt);

/// <summary>
/// How a customer follows an order (Fase 24C, ADR 0023): the steps of its kind with the time it reached each one, from the
/// history of its states and the time it went out with the courier; the link of an online order; and how long it is followed.
/// </summary>
public static class OrderTracking
{
    /// <summary>
    /// Days an order is followed: the link opens it and the account's orders show it in progress. An order still open
    /// after them was left behind by the business; it is not «en curso» any more.
    /// </summary>
    public const int Days = 30;

    private const string CancelledLabel = "Cancelado";

    public const string NotFoundMessage = "No encontramos ese pedido. El enlace pudo haber vencido.";

    private static readonly OrderStatus[] Finished = [OrderStatus.Delivered, OrderStatus.Completed, OrderStatus.Cancelled];

    /// <summary>A new link: 32 random bytes in Base64Url (43 characters), the shape <see cref="Order.IsTrackingToken"/> takes.</summary>
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Whether the order is still followed at <paramref name="now"/>: open, and placed in the last <see cref="Days"/> days.</summary>
    public static bool IsInProgress(OrderStatus status, DateTimeOffset createdAt, DateTimeOffset now)
        => !Finished.Contains(status) && createdAt >= now.AddDays(-Days);

    /// <summary>Whether it is on its way to the customer: it went out with the courier and was not delivered nor cancelled.</summary>
    public static bool IsOnTheWay(OrderStatus status, DateTimeOffset? dispatchedAt)
        => dispatchedAt is not null && !Finished.Contains(status);

    /// <summary>
    /// The steps of the order, in order. Those before where it is are done, with the first time it reached each one (a step
    /// the business skipped is done without a time); the one where it is, current; the rest, to come. A cancelled order
    /// shows the steps it reached and then «Cancelado», with its time.
    /// </summary>
    /// <param name="hasDelivery">It goes to the customer's address: «En camino» is one of its steps.</param>
    public static List<OrderTrackingStepDto> Steps(
        BusinessType businessType,
        OrderType orderType,
        OrderStatus status,
        DateTimeOffset createdAt,
        IEnumerable<OrderStatusChange> history,
        bool hasDelivery,
        DateTimeOffset? dispatchedAt)
    {
        var flow = Flow(businessType, orderType, hasDelivery);
        var changes = history.Where(change => change.NewStatus != change.PreviousStatus).OrderBy(change => change.ChangedAt).ToList();
        var reached = new Dictionary<OrderTrackingStep, DateTimeOffset> { [OrderTrackingStep.Received] = createdAt };
        foreach (var change in changes)
        {
            if (StepOf(change.NewStatus, flow) is { } step)
                reached.TryAdd(step, change.ChangedAt);
        }
        if (dispatchedAt is { } wentOut && flow.Any(f => f.Step == OrderTrackingStep.OnTheWay))
            reached.TryAdd(OrderTrackingStep.OnTheWay, wentOut);

        if (status == OrderStatus.Cancelled)
        {
            // Where it was when it was cancelled: the state before, or on its way if it had gone out.
            var cancellation = changes.LastOrDefault(change => change.NewStatus == OrderStatus.Cancelled);
            var before = StepOf(cancellation.PreviousStatus, flow) ?? OrderTrackingStep.Received;
            var last = dispatchedAt is not null && Index(flow, OrderTrackingStep.OnTheWay) > Index(flow, before)
                ? OrderTrackingStep.OnTheWay
                : before;
            var steps = flow
                .Take(Index(flow, last) + 1)
                .Select(f => Step(f, OrderTrackingStepState.Done, reached))
                .ToList();
            steps.Add(new OrderTrackingStepDto
            {
                Step = OrderTrackingStep.Cancelled,
                Label = CancelledLabel,
                State = OrderTrackingStepState.Current,
                At = cancellation == default ? null : cancellation.ChangedAt
            });
            return steps;
        }

        var at = Index(flow, CurrentIn(flow, status, dispatchedAt));
        return flow
            .Select((f, i) => Step(
                f,
                i < at ? OrderTrackingStepState.Done : i == at ? OrderTrackingStepState.Current : OrderTrackingStepState.Upcoming,
                reached))
            .ToList();
    }

    /// <summary>Where the order is, as the customer reads it («En preparación», «En camino», «Cancelado»).</summary>
    public static (OrderTrackingStep Step, string Label) Current(
        BusinessType businessType,
        OrderType orderType,
        OrderStatus status,
        bool hasDelivery,
        DateTimeOffset? dispatchedAt)
    {
        if (status == OrderStatus.Cancelled)
            return (OrderTrackingStep.Cancelled, CancelledLabel);
        var flow = Flow(businessType, orderType, hasDelivery);
        var current = CurrentIn(flow, status, dispatchedAt);
        return flow[Index(flow, current)];
    }

    private static OrderTrackingStep CurrentIn(List<(OrderTrackingStep Step, string Label)> flow, OrderStatus status, DateTimeOffset? dispatchedAt)
        => IsOnTheWay(status, dispatchedAt) && Index(flow, OrderTrackingStep.OnTheWay) >= 0
            ? OrderTrackingStep.OnTheWay
            : StepOf(status, flow) ?? OrderTrackingStep.Received;

    private static OrderTrackingStepDto Step(
        (OrderTrackingStep Step, string Label) flowStep,
        OrderTrackingStepState state,
        Dictionary<OrderTrackingStep, DateTimeOffset> reached) => new()
    {
        Step = flowStep.Step,
        Label = flowStep.Label,
        State = state,
        At = state == OrderTrackingStepState.Upcoming ? null : reached.TryGetValue(flowStep.Step, out var at) ? at : null
    };

    /// <summary>
    /// The steps of each kind of order, as its state machine moves it: a kitchen prepares and has it ready, a store
    /// confirms and delivers, a service is accepted, done and finished.
    /// </summary>
    private static List<(OrderTrackingStep Step, string Label)> Flow(BusinessType businessType, OrderType orderType, bool hasDelivery)
    {
        if (orderType == OrderType.Service)
        {
            return
            [
                (OrderTrackingStep.Received, "Recibido"),
                (OrderTrackingStep.Confirmed, "Aceptado"),
                (OrderTrackingStep.Preparing, "En proceso"),
                (OrderTrackingStep.Delivered, "Terminado")
            ];
        }

        var flow = new List<(OrderTrackingStep Step, string Label)>
        {
            (OrderTrackingStep.Received, "Recibido"),
            (OrderTrackingStep.Confirmed, "Confirmado")
        };
        if (businessType == BusinessType.Food)
        {
            flow.Add((OrderTrackingStep.Preparing, "En preparación"));
            flow.Add((OrderTrackingStep.Ready, "Listo"));
        }
        if (hasDelivery)
            flow.Add((OrderTrackingStep.OnTheWay, OrderStatusLabels.OnTheWay));
        flow.Add((OrderTrackingStep.Delivered, "Entregado"));
        return flow;
    }

    /// <summary>The step a state is in this flow; none for a state the flow has no step for.</summary>
    private static OrderTrackingStep? StepOf(OrderStatus status, List<(OrderTrackingStep Step, string Label)> flow)
    {
        OrderTrackingStep? step = status switch
        {
            OrderStatus.Pending => OrderTrackingStep.Received,
            OrderStatus.Confirmed or OrderStatus.Accepted => OrderTrackingStep.Confirmed,
            OrderStatus.InPreparation or OrderStatus.InProgress => OrderTrackingStep.Preparing,
            OrderStatus.Ready => OrderTrackingStep.Ready,
            OrderStatus.Delivered or OrderStatus.Completed => OrderTrackingStep.Delivered,
            _ => null
        };
        return step is { } found && Index(flow, found) >= 0 ? found : null;
    }

    private static int Index(List<(OrderTrackingStep Step, string Label)> flow, OrderTrackingStep step)
        => flow.FindIndex(f => f.Step == step);
}

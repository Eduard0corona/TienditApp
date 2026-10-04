using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Billing.Entities;

/// <summary>
/// A business's plan and where it stands with billing. The status follows the dates: during the trial or a paid
/// period it is in use; when they end the business keeps working for <see cref="GraceDays"/> days (past due) and
/// then loses access (suspended). The platform can also suspend it by hand, which only it lifts, or cancel it.
/// Payments are kept as they were registered: each one extends the paid period from where it ended.
/// </summary>
public class TenantSubscription : AuditableEntity, ITenantScoped, IAggregateRoot
{
    public const int GraceDays = 7;
    public const int MaxMonthsPerPayment = 24;
    public const int MaxReasonLength = 500;

    private readonly List<SubscriptionPayment> _payments = new();

    private TenantSubscription() { }

    /// <param name="trialDays">Free days from <paramref name="now"/>; with none the first payment is due at once.</param>
    public TenantSubscription(Guid tenantId, Guid planId, int trialDays, DateTimeOffset now)
    {
        if (trialDays is < 0 or > Plan.MaxTrialDays)
            throw new DomainException($"La prueba gratis dura de 0 a {Plan.MaxTrialDays} días.");

        TenantId = tenantId;
        PlanId = planId;
        StartedAt = now;
        // Without trial the coverage ends now: the business starts in its grace days until it pays.
        TrialEndsAt = now.AddDays(trialDays);
        Status = Evaluate(now);
    }

    public Guid TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset TrialEndsAt { get; private set; }
    /// <summary>End of the last paid period; none before the first payment.</summary>
    public DateTimeOffset? PaidThrough { get; private set; }
    /// <summary>Suspended by the platform (not for lack of payment): payments do not lift it.</summary>
    public bool SuspendedByPlatform { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public IReadOnlyCollection<SubscriptionPayment> Payments => _payments.AsReadOnly();

    /// <summary>Until when the business may use the plan without paying more: the later of trial and paid period.</summary>
    public DateTimeOffset CoveredUntil => PaidThrough is { } paid && paid > TrialEndsAt ? paid : TrialEndsAt;

    /// <summary>When it loses access if nothing is paid.</summary>
    public DateTimeOffset SuspendsAt => CoveredUntil.AddDays(GraceDays);

    public bool HasAccess => Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    /// <summary>The status the dates give at <paramref name="now"/>; manual suspension and cancellation stay.</summary>
    public SubscriptionStatus Evaluate(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Cancelled)
            return SubscriptionStatus.Cancelled;
        if (SuspendedByPlatform)
            return SubscriptionStatus.Suspended;
        if (now < CoveredUntil)
            return PaidThrough is { } paid && paid > now ? SubscriptionStatus.Active : SubscriptionStatus.Trial;
        return now < SuspendsAt ? SubscriptionStatus.PastDue : SubscriptionStatus.Suspended;
    }

    /// <returns>True when the status changed.</returns>
    public bool Refresh(DateTimeOffset now)
    {
        var status = Evaluate(now);
        if (status == Status)
            return false;
        Status = status;
        if (status is SubscriptionStatus.Trial or SubscriptionStatus.Active)
            StatusReason = null;
        else if (status == SubscriptionStatus.Suspended && !SuspendedByPlatform)
            StatusReason = "Sin pago después de los días de gracia.";
        return true;
    }

    public void ChangePlan(Guid planId, DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Cancelled)
            throw new DomainException("La suscripción está cancelada: reactívala antes de cambiar el plan.");
        PlanId = planId;
        Refresh(now);
    }

    /// <summary>Gives free days from today (or from the end of the current trial if it is later).</summary>
    public void ExtendTrial(int days, DateTimeOffset now)
    {
        if (days is < 1 or > Plan.MaxTrialDays)
            throw new DomainException($"Extiende la prueba de 1 a {Plan.MaxTrialDays} días.");
        if (Status == SubscriptionStatus.Cancelled)
            throw new DomainException("La suscripción está cancelada: reactívala antes de extender la prueba.");
        TrialEndsAt = (TrialEndsAt > now ? TrialEndsAt : now).AddDays(days);
        Refresh(now);
    }

    /// <summary>
    /// Records a payment of <paramref name="months"/> months: the paid period continues where the coverage ends (a
    /// payment during the trial starts when the trial ends), or from today when it already ended.
    /// </summary>
    public SubscriptionPayment RegisterPayment(
        decimal amount,
        string currency,
        int months,
        SubscriptionPaymentMethod method,
        string? reference,
        DateTimeOffset paidAt,
        string recordedBy)
    {
        if (Status == SubscriptionStatus.Cancelled)
            throw new DomainException("La suscripción está cancelada: reactívala antes de registrar pagos.");
        if (months is < 1 or > MaxMonthsPerPayment)
            throw new DomainException($"Un pago cubre de 1 a {MaxMonthsPerPayment} meses.");

        var periodStart = CoveredUntil > paidAt ? CoveredUntil : paidAt;
        var periodEnd = periodStart.AddMonths(months);
        var payment = new SubscriptionPayment(TenantId, Id, amount, currency, months, method, reference, paidAt, periodStart, periodEnd, recordedBy);
        _payments.Add(payment);
        PaidThrough = periodEnd;
        Refresh(paidAt);
        return payment;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Cancelled)
            throw new DomainException("La suscripción ya está cancelada.");
        SuspendedByPlatform = true;
        StatusReason = CleanReason(reason) ?? throw new DomainException("Escribe el motivo de la suspensión.");
        Refresh(now);
    }

    /// <summary>Lifts a manual suspension or a cancellation; the dates decide the status again.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        SuspendedByPlatform = false;
        CancelledAt = null;
        if (Status == SubscriptionStatus.Cancelled)
            Status = SubscriptionStatus.Suspended;
        StatusReason = null;
        Refresh(now);
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        StatusReason = CleanReason(reason) ?? throw new DomainException("Escribe el motivo de la cancelación.");
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = now;
    }

    private static string? CleanReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length <= MaxReasonLength ? trimmed : throw new DomainException($"El motivo tiene máximo {MaxReasonLength} caracteres.");
    }
}

/// <summary>A payment of the plan, kept as it was registered.</summary>
public class SubscriptionPayment : BaseEntity, ITenantScoped
{
    public const int MaxReferenceLength = 120;

    private SubscriptionPayment() { }

    internal SubscriptionPayment(
        Guid tenantId,
        Guid subscriptionId,
        decimal amount,
        string currency,
        int months,
        SubscriptionPaymentMethod method,
        string? reference,
        DateTimeOffset paidAt,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string recordedBy)
    {
        if (amount < 0 || decimal.Round(amount, 2) != amount)
            throw new DomainException("El monto del pago no puede ser negativo y lleva máximo 2 decimales.");
        if (!Enum.IsDefined(method))
            throw new DomainException("El método de pago no es válido.");
        var trimmedReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (trimmedReference?.Length > MaxReferenceLength)
            throw new DomainException($"La referencia tiene máximo {MaxReferenceLength} caracteres.");

        TenantId = tenantId;
        SubscriptionId = subscriptionId;
        Amount = amount;
        Currency = currency;
        Months = months;
        Method = method;
        Reference = trimmedReference;
        PaidAt = paidAt;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        RecordedBy = recordedBy;
    }

    public Guid TenantId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = Plan.DefaultCurrency;
    public int Months { get; private set; }
    public SubscriptionPaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }
    public DateTimeOffset PeriodStart { get; private set; }
    public DateTimeOffset PeriodEnd { get; private set; }
    public string RecordedBy { get; private set; } = null!;
}

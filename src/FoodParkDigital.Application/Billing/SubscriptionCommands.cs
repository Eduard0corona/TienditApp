using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Billing.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Billing;

/// <summary>
/// Base of the platform's actions on one business. The platform admin is not a member of it: the business named by
/// the request is put in scope, so its rows are read through the usual filters and written with its tenant, and
/// every action is audited in that business.
/// </summary>
public abstract class PlatformSubscriptionHandler
{
    protected PlatformSubscriptionHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        Context = context;
        TenantService = tenantService;
        CurrentUser = currentUser;
        Clock = clock;
    }

    protected IApplicationDbContext Context { get; }
    protected ICurrentTenantService TenantService { get; }
    protected ICurrentUserService CurrentUser { get; }
    protected IDateTimeProvider Clock { get; }
    protected string Actor => CurrentUser.UserName ?? "platform";

    protected async Task<Tenant?> EnterTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await Context.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (tenant is not null)
            TenantService.SetTenant(tenant.Id);
        return tenant;
    }

    protected Task<TenantSubscription?> SubscriptionAsync(Guid tenantId, CancellationToken cancellationToken)
        => Context.TenantSubscriptions.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);

    /// <summary>Saves the change, carries the billing status to the business and audits it.</summary>
    protected async Task<Result> SaveAsync(Tenant tenant, TenantSubscription? subscription, string action, object details, CancellationToken cancellationToken)
    {
        if (subscription is not null)
            tenant.SetStatus(SubscriptionRules.TenantStatusFor(subscription.Status));
        Context.AuditLogs.Add(SubscriptionRules.Audit(tenant.Id, action, Actor, details));
        await Context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    protected const string NotFound = "El negocio no existe.";
    protected const string WithoutPlan = "El negocio no tiene plan: asígnale uno primero.";
}

/// <summary>
/// Puts a business on a plan: the first time with a free trial (the plan's days unless others are given), later
/// as a change of plan. The business keeps only the modules the plan includes.
/// </summary>
[RequiresPlatformAdmin]
public record AssignPlanCommand : IRequest<Result>
{
    public Guid TenantId { get; init; }
    public Guid PlanId { get; init; }
    /// <summary>Only for the first plan of the business; the plan's trial days when omitted.</summary>
    public int? TrialDays { get; init; }
}

public class AssignPlanCommandValidator : AbstractValidator<AssignPlanCommand>
{
    public AssignPlanCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.TrialDays).InclusiveBetween(0, Plan.MaxTrialDays).When(x => x.TrialDays.HasValue);
    }
}

public class AssignPlanCommandHandler : PlatformSubscriptionHandler, IRequestHandler<AssignPlanCommand, Result>
{
    public AssignPlanCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
        : base(context, tenantService, currentUser, clock) { }

    public async Task<Result> Handle(AssignPlanCommand request, CancellationToken cancellationToken)
    {
        var tenant = await EnterTenantAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(NotFound);
        var plan = await Context.Plans.FirstOrDefaultAsync(x => x.Id == request.PlanId, cancellationToken);
        if (plan is null)
            return Result.Failure("El plan no existe.");

        var now = Clock.UtcNow;
        var subscription = await SubscriptionAsync(tenant.Id, cancellationToken);
        var previousPlanId = subscription?.PlanId;
        try
        {
            if (subscription is null)
            {
                if (!plan.IsActive)
                    return Result.Failure("El plan está inactivo: actívalo para asignarlo.");
                subscription = new TenantSubscription(tenant.Id, plan.Id, request.TrialDays ?? plan.TrialDays, now);
                Context.TenantSubscriptions.Add(subscription);
            }
            else
            {
                subscription.ChangePlan(plan.Id, now);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        var settings = await Context.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, cancellationToken);
        var previousModules = settings?.Capabilities;
        if (settings is not null)
            SubscriptionRules.ClampModules(settings, tenant.BusinessType, plan);

        return await SaveAsync(tenant, subscription, previousPlanId is null ? "PlanAssigned" : "PlanChanged", new
        {
            Plan = plan.Code,
            PreviousPlanId = previousPlanId,
            subscription.Status,
            subscription.TrialEndsAt,
            PreviousModules = previousModules?.ToString(),
            Modules = settings?.Capabilities.ToString()
        }, cancellationToken);
    }
}

/// <summary>Records a payment of the plan; the paid period continues where the coverage ends.</summary>
[RequiresPlatformAdmin]
public record RegisterSubscriptionPaymentCommand : IRequest<Result>
{
    public Guid TenantId { get; init; }
    public int Months { get; init; } = 1;
    /// <summary>The plan's price times the months when omitted (a discount is written here).</summary>
    public decimal? Amount { get; init; }
    public SubscriptionPaymentMethod Method { get; init; }
    public string? Reference { get; init; }
}

public class RegisterSubscriptionPaymentCommandValidator : AbstractValidator<RegisterSubscriptionPaymentCommand>
{
    public RegisterSubscriptionPaymentCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Months).InclusiveBetween(1, TenantSubscription.MaxMonthsPerPayment);
        RuleFor(x => x.Amount!.Value).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true).When(x => x.Amount.HasValue);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(SubscriptionPayment.MaxReferenceLength);
    }
}

public class RegisterSubscriptionPaymentCommandHandler : PlatformSubscriptionHandler, IRequestHandler<RegisterSubscriptionPaymentCommand, Result>
{
    public RegisterSubscriptionPaymentCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
        : base(context, tenantService, currentUser, clock) { }

    public async Task<Result> Handle(RegisterSubscriptionPaymentCommand request, CancellationToken cancellationToken)
    {
        var tenant = await EnterTenantAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(NotFound);
        var subscription = await Context.TenantSubscriptions
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id, cancellationToken);
        if (subscription is null)
            return Result.Failure(WithoutPlan);
        var plan = await Context.Plans.FirstAsync(x => x.Id == subscription.PlanId, cancellationToken);

        SubscriptionPayment payment;
        try
        {
            payment = subscription.RegisterPayment(
                amount: request.Amount ?? plan.MonthlyPrice * request.Months,
                currency: plan.Currency,
                months: request.Months,
                method: request.Method,
                reference: request.Reference,
                paidAt: Clock.UtcNow,
                recordedBy: Actor);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        return await SaveAsync(tenant, subscription, "SubscriptionPaymentRegistered", new
        {
            payment.Amount,
            payment.Currency,
            payment.Months,
            payment.PeriodStart,
            payment.PeriodEnd,
            Method = payment.Method.ToString(),
            subscription.Status
        }, cancellationToken);
    }
}

[RequiresPlatformAdmin]
public record ExtendTrialCommand : IRequest<Result>
{
    public Guid TenantId { get; init; }
    public int Days { get; init; }
}

public class ExtendTrialCommandHandler : PlatformSubscriptionHandler, IRequestHandler<ExtendTrialCommand, Result>
{
    public ExtendTrialCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
        : base(context, tenantService, currentUser, clock) { }

    public async Task<Result> Handle(ExtendTrialCommand request, CancellationToken cancellationToken)
    {
        var tenant = await EnterTenantAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(NotFound);
        var subscription = await SubscriptionAsync(tenant.Id, cancellationToken);
        if (subscription is null)
            return Result.Failure(WithoutPlan);
        try
        {
            subscription.ExtendTrial(request.Days, Clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        return await SaveAsync(tenant, subscription, "TrialExtended", new { request.Days, subscription.TrialEndsAt, subscription.Status }, cancellationToken);
    }
}

/// <summary>Takes away the access of a business (for a reason other than lack of payment, which is automatic).</summary>
[RequiresPlatformAdmin]
public record SuspendTenantCommand : IRequest<Result>
{
    public Guid TenantId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public class SuspendTenantCommandValidator : AbstractValidator<SuspendTenantCommand>
{
    public SuspendTenantCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(TenantSubscription.MaxReasonLength);
    }
}

public class SuspendTenantCommandHandler : PlatformSubscriptionHandler, IRequestHandler<SuspendTenantCommand, Result>
{
    public SuspendTenantCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
        : base(context, tenantService, currentUser, clock) { }

    public async Task<Result> Handle(SuspendTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await EnterTenantAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(NotFound);
        var subscription = await SubscriptionAsync(tenant.Id, cancellationToken);
        try
        {
            if (subscription is null)
                tenant.SetStatus(Domain.Tenants.Enums.TenantStatus.Suspended); // A business without plan is suspended directly.
            else
                subscription.Suspend(request.Reason, Clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        return await SaveAsync(tenant, subscription, "TenantSuspended", new { request.Reason }, cancellationToken);
    }
}

/// <summary>Lifts a suspension by the platform or a cancellation; the dates decide whether the business is in use.</summary>
[RequiresPlatformAdmin]
public record ReactivateTenantCommand : IRequest<Result>
{
    public Guid TenantId { get; init; }
}

public class ReactivateTenantCommandHandler : PlatformSubscriptionHandler, IRequestHandler<ReactivateTenantCommand, Result>
{
    public ReactivateTenantCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
        : base(context, tenantService, currentUser, clock) { }

    public async Task<Result> Handle(ReactivateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await EnterTenantAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(NotFound);
        var subscription = await SubscriptionAsync(tenant.Id, cancellationToken);
        if (subscription is null)
            tenant.SetStatus(Domain.Tenants.Enums.TenantStatus.Active);
        else
            subscription.Reactivate(Clock.UtcNow);

        // Without payment after the grace days it stays suspended: the console shows it and offers a payment or more trial.
        return await SaveAsync(tenant, subscription, "TenantReactivated", new { Status = subscription?.Status.ToString() }, cancellationToken);
    }
}

[RequiresPlatformAdmin]
public record CancelSubscriptionCommand : IRequest<Result>
{
    public Guid TenantId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public class CancelSubscriptionCommandValidator : AbstractValidator<CancelSubscriptionCommand>
{
    public CancelSubscriptionCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(TenantSubscription.MaxReasonLength);
    }
}

public class CancelSubscriptionCommandHandler : PlatformSubscriptionHandler, IRequestHandler<CancelSubscriptionCommand, Result>
{
    public CancelSubscriptionCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
        : base(context, tenantService, currentUser, clock) { }

    public async Task<Result> Handle(CancelSubscriptionCommand request, CancellationToken cancellationToken)
    {
        var tenant = await EnterTenantAsync(request.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure(NotFound);
        var subscription = await SubscriptionAsync(tenant.Id, cancellationToken);
        if (subscription is null)
            return Result.Failure(WithoutPlan);
        try
        {
            subscription.Cancel(request.Reason, Clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        return await SaveAsync(tenant, subscription, "SubscriptionCancelled", new { request.Reason }, cancellationToken);
    }
}

using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Billing.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Platform console: businesses, plans and billing. Only the platform admin; every action on a business is audited
/// in that business.
/// </summary>
[ApiController]
[Route("api/v1/platform")]
[Authorize]
public class PlatformController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<PlatformOverviewDto>> GetOverview(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPlatformOverviewQuery(), cancellationToken));

    [HttpGet("tenants")]
    public async Task<ActionResult<PaginatedList<PlatformTenantDto>>> GetTenants(
        [FromQuery] string? search,
        [FromQuery] SubscriptionStatus? subscriptionStatus,
        [FromQuery] bool withoutPlan = false,
        [FromQuery] Guid? planId = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetPlatformTenantsQuery
        {
            Search = search,
            SubscriptionStatus = subscriptionStatus,
            WithoutPlan = withoutPlan,
            PlanId = planId,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    [HttpGet("tenants/{tenantId:guid}/subscription")]
    public async Task<ActionResult<SubscriptionDetailDto>> GetTenantSubscription(Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantSubscriptionQuery { TenantId = tenantId }, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("tenants/{tenantId:guid}/plan")]
    public Task<IActionResult> AssignPlan(Guid tenantId, [FromBody] AssignPlanRequest request, CancellationToken cancellationToken)
        => SendAsync(new AssignPlanCommand { TenantId = tenantId, PlanId = request.PlanId, TrialDays = request.TrialDays }, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/payments")]
    public Task<IActionResult> RegisterPayment(Guid tenantId, [FromBody] RegisterSubscriptionPaymentRequest request, CancellationToken cancellationToken)
        => SendAsync(new RegisterSubscriptionPaymentCommand
        {
            TenantId = tenantId,
            Months = request.Months,
            Amount = request.Amount,
            Method = request.Method,
            Reference = request.Reference
        }, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/trial")]
    public Task<IActionResult> ExtendTrial(Guid tenantId, [FromBody] ExtendTrialRequest request, CancellationToken cancellationToken)
        => SendAsync(new ExtendTrialCommand { TenantId = tenantId, Days = request.Days }, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/suspend")]
    public Task<IActionResult> Suspend(Guid tenantId, [FromBody] PlatformReasonRequest request, CancellationToken cancellationToken)
        => SendAsync(new SuspendTenantCommand { TenantId = tenantId, Reason = request.Reason }, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/reactivate")]
    public Task<IActionResult> Reactivate(Guid tenantId, CancellationToken cancellationToken)
        => SendAsync(new ReactivateTenantCommand { TenantId = tenantId }, cancellationToken);

    [HttpPost("tenants/{tenantId:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid tenantId, [FromBody] PlatformReasonRequest request, CancellationToken cancellationToken)
        => SendAsync(new CancelSubscriptionCommand { TenantId = tenantId, Reason = request.Reason }, cancellationToken);

    [HttpGet("plans")]
    public async Task<ActionResult<List<PlanDto>>> GetPlans(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPlansQuery(), cancellationToken));

    [HttpPost("plans")]
    public async Task<ActionResult<PlanDto>> CreatePlan([FromBody] CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<ActionResult<PlanDto>> UpdatePlan(Guid id, [FromBody] UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    private async Task<IActionResult> SendAsync(IRequest<Result> command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : NoContent();
    }
}

public record AssignPlanRequest(Guid PlanId, int? TrialDays);

public record RegisterSubscriptionPaymentRequest(int Months, decimal? Amount, SubscriptionPaymentMethod Method, string? Reference);

public record ExtendTrialRequest(int Days);

public record PlatformReasonRequest(string Reason);

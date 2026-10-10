using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Queries;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Guided setup, sales policy, public menu and theme of the signed-in user's business. Under /auth/my-tenant, so the
/// middleware checks the membership; the requests allow changes only to owners and admins.
/// </summary>
[ApiController]
[Route("api/v1/auth/my-tenant")]
[Authorize]
public class TenantSetupController : ControllerBase
{
    private readonly IMediator _mediator;

    public TenantSetupController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("setup")]
    public async Task<ActionResult<TenantSetupDto>> GetSetup(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantSetupQuery(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("setup/complete")]
    public async Task<IActionResult> CompleteSetup(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CompleteTenantSetupCommand(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpPost("setup/starter-catalog")]
    public async Task<ActionResult<InstallStarterCatalogResultDto>> InstallStarterCatalog(
        [FromBody] InstallStarterCatalogCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("sales-policy")]
    public async Task<ActionResult<TenantSalesPolicyDto>> UpdateSalesPolicy([FromBody] UpdateSalesPolicyCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Plan, billing status, limits in use and payments of the business (owners and admins).</summary>
    [HttpGet("subscription")]
    public async Task<ActionResult<SubscriptionDetailDto>> GetSubscription(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMySubscriptionQuery(), cancellationToken));

    /// <summary>Turns the public menu off, on, or on with orders.</summary>
    [HttpPut("online-menu")]
    public async Task<ActionResult<OnlineMenuSettingsDto>> UpdateOnlineMenu([FromBody] UpdateOnlineMenuRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateOnlineMenuCommand(request.Mode), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>When the business attends: the public menu says whether it is open and when it opens or closes.</summary>
    [HttpPut("opening-hours")]
    public async Task<ActionResult<OpeningHoursDto>> UpdateOpeningHours([FromBody] UpdateOpeningHoursRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateOpeningHoursCommand(request.Intervals), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>The colors of the business for its team's app and its public menu.</summary>
    [HttpPut("theme")]
    public async Task<ActionResult<BusinessThemeDto>> UpdateTheme([FromBody] UpdateBusinessThemeRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateBusinessThemeCommand(request.Theme), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }
}

public record UpdateOnlineMenuRequest(OnlineMenuMode Mode);

public record UpdateBusinessThemeRequest(BusinessTheme Theme);

/// <summary>The openings of the week; empty or null removes the hours.</summary>
public record UpdateOpeningHoursRequest(List<OpeningIntervalDto>? Intervals);

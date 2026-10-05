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
/// Guided setup and sales policy of the signed-in user's business. Under /auth/my-tenant, so the
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
    public async Task<ActionResult<TenantSetupDto>> GetSetup()
    {
        var result = await _mediator.Send(new GetTenantSetupQuery());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("setup/complete")]
    public async Task<IActionResult> CompleteSetup()
    {
        var result = await _mediator.Send(new CompleteTenantSetupCommand());
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpPost("setup/starter-catalog")]
    public async Task<ActionResult<InstallStarterCatalogResultDto>> InstallStarterCatalog(
        [FromBody] InstallStarterCatalogCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("sales-policy")]
    public async Task<ActionResult<TenantSalesPolicyDto>> UpdateSalesPolicy([FromBody] UpdateSalesPolicyCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Plan, billing status, limits in use and payments of the business (owners and admins).</summary>
    [HttpGet("subscription")]
    public async Task<ActionResult<SubscriptionDetailDto>> GetSubscription()
        => Ok(await _mediator.Send(new GetMySubscriptionQuery()));

    /// <summary>Turns the public menu off, on, or on with orders.</summary>
    [HttpPut("online-menu")]
    public async Task<ActionResult<OnlineMenuSettingsDto>> UpdateOnlineMenu([FromBody] UpdateOnlineMenuRequest request)
    {
        var result = await _mediator.Send(new UpdateOnlineMenuCommand(request.Mode));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }
}

public record UpdateOnlineMenuRequest(OnlineMenuMode Mode);

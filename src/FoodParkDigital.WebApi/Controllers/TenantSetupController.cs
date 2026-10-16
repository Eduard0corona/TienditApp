using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
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
/// Guided setup, sales policy, public menu, hours, special days and theme of the signed-in user's business. Under
/// /auth/my-tenant, so the middleware checks the membership; the requests allow changes only to owners and admins.
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

    /// <summary>«Cómo te ven en TienditApp»: its category, its description and whether it appears in Explorar.</summary>
    [HttpGet("directory")]
    public async Task<ActionResult<DirectoryProfileDto>> GetDirectoryProfile(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetDirectoryProfileQuery(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Changes how the business shows in Explorar; Explorar shows it within 30 seconds.</summary>
    [HttpPut("directory")]
    public async Task<ActionResult<DirectoryProfileDto>> UpdateDirectoryProfile(
        [FromBody] UpdateDirectoryProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateDirectoryProfileCommand(request.Category, request.Description, request.Listed), cancellationToken);
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

    /// <summary>The days the business closes or opens with other hours, with the holidays it may want to mark.</summary>
    [HttpGet("special-days")]
    public async Task<ActionResult<SpecialDaysDto>> GetSpecialDays(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSpecialDaysQuery(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Marks one date, or consecutive dates, as closed or with other hours; answers the refreshed list.</summary>
    [HttpPost("special-days")]
    public async Task<ActionResult<SpecialDaysDto>> AddSpecialDay([FromBody] SaveSpecialDayRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddSpecialDayCommand(request.From, request.To, request.Kind, request.Openings, request.Reason), cancellationToken);
        if (result.IsFailure)
            return SpecialDayProblem(result.Error);
        return Ok(result.Value);
    }

    /// <summary>Moves a period of special days or gives it another rule; the dates that passed stay as they were.</summary>
    [HttpPut("special-days/{id:guid}")]
    public async Task<ActionResult<SpecialDaysDto>> UpdateSpecialDay(Guid id, [FromBody] UpdateSpecialDayRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateSpecialDayCommand(id, request.Revision, request.From, request.To, request.Kind, request.Openings, request.Reason), cancellationToken);
        if (result.IsFailure)
            return SpecialDayProblem(result.Error);
        return Ok(result.Value);
    }

    /// <summary>Removes a period of special days; one under way ends yesterday instead.</summary>
    [HttpDelete("special-days/{id:guid}")]
    public async Task<ActionResult<SpecialDaysDto>> RemoveSpecialDay(Guid id, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RemoveSpecialDayCommand(id, revision), cancellationToken);
        if (result.IsFailure)
            return SpecialDayProblem(result.Error);
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

    // Someone changed the period since the owner loaded it: like a concurrent save, the client reloads.
    private ActionResult SpecialDayProblem(string? error)
        => error == SpecialDayErrors.Stale
            ? this.ApiError(StatusCodes.Status409Conflict, error, ApiErrorCodes.ConcurrencyConflict)
            : this.ApiError(StatusCodes.Status400BadRequest, error);
}

public record UpdateOnlineMenuRequest(OnlineMenuMode Mode);

/// <param name="Category">Where Explorar lists the business.</param>
/// <param name="Description">One line under its name; empty removes it.</param>
/// <param name="Listed">Appear in Explorar while the menu is published.</param>
public record UpdateDirectoryProfileRequest(BusinessCategory Category, string? Description, bool Listed);

public record UpdateBusinessThemeRequest(BusinessTheme Theme);

/// <summary>The openings of the week; empty or null removes the hours.</summary>
public record UpdateOpeningHoursRequest(List<OpeningIntervalDto>? Intervals);

/// <summary>A special day: from a date to another (null for one day), closed or with one or two openings.</summary>
public record SaveSpecialDayRequest(DateOnly From, DateOnly? To, SpecialDayKind Kind, List<SpecialOpeningDto>? Openings, string? Reason);

/// <summary>A period of special days as it should be, with the revision the owner saw.</summary>
public record UpdateSpecialDayRequest(int Revision, DateOnly From, DateOnly? To, SpecialDayKind Kind, List<SpecialOpeningDto>? Openings, string? Reason);

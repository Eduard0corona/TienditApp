using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Guided setup and sales policy of the signed-in user's business. Under /auth/my-tenant, so the
/// middleware checks the membership; the handlers allow changes only to owners and admins.
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
            return NotFound(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("setup/complete")]
    public async Task<IActionResult> CompleteSetup()
    {
        var result = await _mediator.Send(new CompleteTenantSetupCommand());
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpPost("setup/starter-catalog")]
    public async Task<ActionResult<InstallStarterCatalogResultDto>> InstallStarterCatalog(
        [FromBody] InstallStarterCatalogCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPut("sales-policy")]
    public async Task<ActionResult<TenantSalesPolicyDto>> UpdateSalesPolicy([FromBody] UpdateSalesPolicyCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }
}

using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.Application.Tenants.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Role, permissions and modules of the signed-in user in the current business, resolved by the server.</summary>
    [HttpGet("access")]
    public async Task<ActionResult<MyAccessDto>> GetMyAccess()
        => Ok(await _mediator.Send(new GetMyAccessQuery()));

    [HttpGet("tenants")]
    public async Task<ActionResult<IReadOnlyList<MyTenantDto>>> GetMyTenants()
    {
        var result = await _mediator.Send(new GetMyTenantsQuery());
        return Ok(result);
    }

    /// <summary>Opens another business with this account, as its owner. Switch to it to finish its setup.</summary>
    [HttpPost("tenants")]
    public async Task<ActionResult<MyTenantDto>> CreateBusiness([FromBody] CreateBusinessCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }
}

using FoodParkDigital.Application.Dining;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>Tables of the dining room and their open tabs (businesses with the Food module).</summary>
[ApiController]
[Route("api/v1/tables")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
public class TablesController : ControllerBase
{
    private readonly IMediator _mediator;

    public TablesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<DiningRoomDto>> GetDiningRoom([FromQuery] bool includeInactive = false)
        => Ok(await _mediator.Send(new GetDiningRoomQuery(includeInactive)));

    [HttpPost]
    public async Task<ActionResult<DiningTableDto>> Create([FromBody] CreateDiningTableCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DiningTableDto>> Update(Guid id, [FromBody] UpdateDiningTableCommand command)
    {
        var result = await _mediator.Send(command with { Id = id });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }
}

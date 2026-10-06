using FoodParkDigital.Application.Dining;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>Tables of the dining room and their open tabs (businesses with the Food module).</summary>
[ApiController]
[Route("api/v1/tables")]
[Authorize]
public class TablesController : ControllerBase
{
    private readonly IMediator _mediator;

    public TablesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<DiningRoomDto>> GetDiningRoom([FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetDiningRoomQuery(includeInactive), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<DiningTableDto>> Create([FromBody] CreateDiningTableCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DiningTableDto>> Update(Guid id, [FromBody] UpdateDiningTableCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command with { Id = id }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }
}

using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Application.Inventory.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/branches")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
public class BranchesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BranchesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<InventoryLocationDto>>> GetBranches([FromQuery] bool? isActive)
    {
        var result = await _mediator.Send(new GetInventoryLocationsQuery
        {
            IsActive = isActive
        });

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<InventoryLocationDto>> CreateBranch([FromBody] CreateInventoryLocationCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InventoryLocationDto>> UpdateBranch(Guid id, [FromBody] UpdateInventoryLocationCommand command)
    {
        if (id != command.Id)
            return BadRequest(new { error = "Route ID and body ID mismatch." });

        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetBranchActive(Guid id, [FromBody] SetBranchActiveRequest request)
    {
        var result = await _mediator.Send(new SetInventoryLocationActiveCommand
        {
            Id = id,
            IsActive = request.IsActive
        });

        if (result.IsFailure)
            return BadRequest(new { error = result.Error });

        return NoContent();
    }
}

public record SetBranchActiveRequest(bool IsActive);

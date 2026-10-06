using FoodParkDigital.Application.Branches;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>Branches of the business: every member reads them, owners and admins manage them.</summary>
[ApiController]
[Route("api/v1/branches")]
[Authorize]
public class BranchesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BranchesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<BranchDto>>> GetBranches([FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBranchesQuery
        {
            IsActive = isActive
        }, cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<BranchDto>> CreateBranch([FromBody] CreateBranchCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BranchDto>> UpdateBranch(Guid id, [FromBody] UpdateBranchCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetBranchActive(Guid id, [FromBody] SetBranchActiveRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetBranchActiveCommand
        {
            Id = id,
            IsActive = request.IsActive
        }, cancellationToken);

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpPost("{id:guid}/default")]
    public async Task<IActionResult> SetDefaultBranch(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetDefaultBranchCommand { Id = id }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }
}

public record SetBranchActiveRequest(bool IsActive);

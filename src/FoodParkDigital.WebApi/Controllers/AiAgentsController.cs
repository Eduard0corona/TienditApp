using FoodParkDigital.Application.AiAgents.Commands;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.AiAgents.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/ai-agents")]
[Authorize]
public class AiAgentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiAgentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<AiAgentDto>>> GetAll([FromQuery] bool? isActive)
    {
        var result = await _mediator.Send(new GetAiAgentsQuery
        {
            IsActive = isActive
        });

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AiAgentDto>> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetAiAgentByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<AiAgentDto>> Create([FromBody] CreateAiAgentCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AiAgentDto>> Update(Guid id, [FromBody] UpdateAiAgentCommand command)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetAiAgentActiveRequest request)
    {
        var result = await _mediator.Send(new SetAiAgentActiveCommand
        {
            Id = id,
            IsActive = request.IsActive
        });

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _mediator.Send(new DeleteAiAgentCommand(id));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }
}

public record SetAiAgentActiveRequest(bool IsActive);

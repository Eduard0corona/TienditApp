using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Catalog.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CategoriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> GetAll([FromQuery] bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCategoriesQuery { IncludeArchived = includeArchived }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("templates")]
    public async Task<ActionResult<List<CategoryTemplateDto>>> GetTemplates(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCategoryTemplatesQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("templates/{templateId}/install")]
    public async Task<ActionResult<InstallCategoryTemplateResultDto>> InstallTemplate(string templateId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new InstallCategoryTemplateCommand
        {
            TemplateId = templateId
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCategoryByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, [FromBody] UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteCategoryCommand(id), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> ToggleActive(Guid id, [FromBody] ToggleCategoryActiveRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ToggleCategoryActiveCommand
        {
            Id = id,
            IsActive = request.IsActive,
            MoveProductsToCategoryId = request.MoveProductsToCategoryId
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }
}

public record ToggleCategoryActiveRequest(bool IsActive, Guid? MoveProductsToCategoryId = null);

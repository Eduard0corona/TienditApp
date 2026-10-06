using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Employees.Commands;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Application.Employees.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<EmployeeDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? appRole,
        [FromQuery] string? tenantRole,
        [FromQuery] string? status,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetEmployeesQuery
        {
            Search = search,
            AppRole = appRole,
            TenantRole = tenantRole,
            Status = status,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);

        return Ok(result);
    }

    [HttpGet("{tenantUserId:guid}")]
    public async Task<ActionResult<EmployeeDto>> GetById(Guid tenantUserId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeByIdQuery(tenantUserId), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);

        return Ok(result.Value);
    }

    [HttpGet("permission-options")]
    public async Task<ActionResult<IReadOnlyList<EmployeePermissionOptionDto>>> GetPermissionOptions(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeePermissionOptionsQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>Named roles (Gerente, Cajero, Mesero, Cocina, Técnico) that fill the role and the permissions.</summary>
    [HttpGet("role-presets")]
    public async Task<ActionResult<IReadOnlyList<EmployeeRolePresetDto>>> GetRolePresets(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmployeeRolePresetsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{tenantUserId:guid}/resend-invitation")]
    public async Task<ActionResult<EmployeeInvitationDto>> ResendInvitation(Guid tenantUserId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResendEmployeeInvitationCommand(tenantUserId), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> Create([FromBody] CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetById), new { tenantUserId = result.Value.TenantUserId }, result.Value);
    }

    [HttpPut("{tenantUserId:guid}")]
    public async Task<ActionResult<EmployeeDto>> Update(Guid tenantUserId, [FromBody] UpdateEmployeeCommand command, CancellationToken cancellationToken)
    {
        if (tenantUserId != command.TenantUserId)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("{tenantUserId:guid}/active")]
    public async Task<IActionResult> SetActive(Guid tenantUserId, [FromBody] SetEmployeeActiveRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetEmployeeActiveCommand
        {
            TenantUserId = tenantUserId,
            IsActive = request.IsActive
        }, cancellationToken);

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }
}

public record SetEmployeeActiveRequest(bool IsActive);

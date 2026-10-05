using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/tenants/{tenantId:guid}/orders")]
[Authorize]
public class TenantOrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantAccessService _tenantAccessService;
    private readonly ICurrentTenantService _currentTenantService;

    public TenantOrdersController(
        IMediator mediator,
        ITenantAccessService tenantAccessService,
        ICurrentTenantService currentTenantService)
    {
        _mediator = mediator;
        _tenantAccessService = tenantAccessService;
        _currentTenantService = currentTenantService;
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(Guid tenantId, [FromBody] CreateTenantOrderRequest request)
    {
        // The middleware validated membership, permission and capabilities for the request tenant
        // (token tenant or SUPER_ADMIN override); the route must target that same tenant.
        if (tenantId != _currentTenantService.TenantId)
            return Forbid();

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
            return this.ApiError(StatusCodes.Status401Unauthorized, "Usuario no autenticado.");

        var hasAccess = await _tenantAccessService.HasActiveAccessAsync(userId, tenantId, HttpContext.RequestAborted);
        if (!hasAccess)
            return Forbid();

        var command = new CreateTenantOrderCommand
        {
            TenantId = tenantId,
            Phone = request.Phone,
            Name = request.Name,
            CustomerNotes = request.CustomerNotes,
            TableNumber = request.TableNumber,
            DefaultCountry = request.DefaultCountry,
            Items = request.Items
        };

        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }
}

public class CreateTenantOrderRequest
{
    public string Phone { get; init; } = null!;
    public string? Name { get; init; }
    public string? CustomerNotes { get; init; }
    public string? TableNumber { get; init; }
    public string DefaultCountry { get; init; } = "MX";
    public List<CreateOrderItemDto> Items { get; init; } = [];
}

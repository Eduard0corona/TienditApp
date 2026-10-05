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
    private readonly ICurrentTenantService _currentTenantService;

    public TenantOrdersController(IMediator mediator, ICurrentTenantService currentTenantService)
    {
        _mediator = mediator;
        _currentTenantService = currentTenantService;
    }

    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(Guid tenantId, [FromBody] CreateTenantOrderRequest request)
    {
        // The middleware validated the membership in the request's business (the token's, or the platform admin's
        // override) and the request declares who may take orders: the route must name that same business.
        if (tenantId != _currentTenantService.TenantId)
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

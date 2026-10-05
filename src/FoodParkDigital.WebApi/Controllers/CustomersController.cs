using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Customers.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;

    public CustomersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<CustomerDto>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetCustomersQuery
        {
            Search = search,
            IsActive = isActive,
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    /// <summary>
    /// The customer card by phone or registered customer: orders, balance, favorite products, appointments and
    /// opt-out. Also readable with ManageOrders or ManageWhatsApp (orders and chat screens).
    /// </summary>
    [HttpGet("card")]
    public async Task<ActionResult<CustomerCardDto>> GetCard([FromQuery] string? phone, [FromQuery] Guid? customerId)
    {
        var result = await _mediator.Send(new GetCustomerCardQuery { Phone = phone, CustomerId = customerId });
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetCustomerByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, [FromBody] UpdateCustomerCommand command)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetCustomerActiveRequest request)
    {
        var result = await _mediator.Send(new SetCustomerActiveCommand
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
        var result = await _mediator.Send(new DeleteCustomerCommand(id));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpPut("{id:guid}/notes")]
    public async Task<ActionResult<CustomerDto>> UpdateNotes(Guid id, [FromBody] UpdateCustomerNotesRequest request)
        => ToResult(await _mediator.Send(new UpdateCustomerNotesCommand(id, request.Notes)));

    [HttpPost("{id:guid}/addresses")]
    public async Task<ActionResult<CustomerDto>> AddAddress(Guid id, [FromBody] CustomerAddressRequest request)
        => ToResult(await _mediator.Send(new AddCustomerAddressCommand
        {
            CustomerId = id,
            Label = request.Label,
            AddressLine = request.AddressLine,
            References = request.References,
            IsDefault = request.IsDefault
        }));

    [HttpPut("{id:guid}/addresses/{addressId:guid}")]
    public async Task<ActionResult<CustomerDto>> UpdateAddress(Guid id, Guid addressId, [FromBody] CustomerAddressRequest request)
        => ToResult(await _mediator.Send(new UpdateCustomerAddressCommand
        {
            CustomerId = id,
            AddressId = addressId,
            Label = request.Label,
            AddressLine = request.AddressLine,
            References = request.References,
            IsDefault = request.IsDefault
        }));

    [HttpDelete("{id:guid}/addresses/{addressId:guid}")]
    public async Task<ActionResult<CustomerDto>> RemoveAddress(Guid id, Guid addressId)
        => ToResult(await _mediator.Send(new RemoveCustomerAddressCommand(id, addressId)));

    private ActionResult<CustomerDto> ToResult(Result<CustomerDto> result)
        => result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
}

public record SetCustomerActiveRequest(bool IsActive);

public record UpdateCustomerNotesRequest(string? Notes);

public record CustomerAddressRequest(string AddressLine, string? Label = null, string? References = null, bool IsDefault = false);


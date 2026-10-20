using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
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
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCustomersQuery
        {
            Search = search,
            IsActive = isActive,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The customer card by phone or registered customer: orders, balance, favorite products, appointments and
    /// opt-out. Also readable with ManageOrders or ManageWhatsApp (orders and chat screens).
    /// </summary>
    [HttpGet("card")]
    public async Task<ActionResult<CustomerCardDto>> GetCard([FromQuery] string? phone, [FromQuery] Guid? customerId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCustomerCardQuery { Phone = phone, CustomerId = customerId }, cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>
    /// Clientes › Por cobrar (Fase 26A): who owes the business and how much, by phone. Also readable with ChargeOrders.
    /// </summary>
    [HttpGet("receivables")]
    public async Task<ActionResult<ReceivablesDto>> GetReceivables(
        [FromQuery] string? search,
        [FromQuery] ReceivablesSort sort = ReceivablesSort.Owed,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetReceivablesQuery
        {
            Search = search,
            Sort = sort,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    /// <summary>The account of a customer, by record or phone: limit, what they owe, the orders owed and their latest payments.</summary>
    [HttpGet("account")]
    public async Task<ActionResult<CustomerAccountDto>> GetAccount([FromQuery] Guid? customerId, [FromQuery] string? phone, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new GetCustomerAccountQuery { CustomerId = customerId, Phone = phone }, cancellationToken));

    /// <summary>A payment to the account of a customer, split over the orders they owe from the oldest (needs an open drawer).</summary>
    [HttpPost("account/payments")]
    public async Task<ActionResult<CustomerAccountDto>> PayAccount([FromBody] PayCustomerAccountCommand command, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(command, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCustomerByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create([FromBody] CreateCustomerCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, [FromBody] UpdateCustomerCommand command, CancellationToken cancellationToken)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetCustomerActiveRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetCustomerActiveCommand
        {
            Id = id,
            IsActive = request.IsActive
        }, cancellationToken);

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteCustomerCommand(id), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    /// <summary>How much the business lets the customer owe; null takes it away (owners and admins).</summary>
    [HttpPut("{id:guid}/credit-limit")]
    public async Task<ActionResult<CustomerDto>> UpdateCreditLimit(Guid id, [FromBody] UpdateCustomerCreditLimitRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new UpdateCustomerCreditLimitCommand { CustomerId = id, CreditLimit = request.CreditLimit }, cancellationToken));

    [HttpPut("{id:guid}/notes")]
    public async Task<ActionResult<CustomerDto>> UpdateNotes(Guid id, [FromBody] UpdateCustomerNotesRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new UpdateCustomerNotesCommand(id, request.Notes), cancellationToken));

    [HttpPost("{id:guid}/addresses")]
    public async Task<ActionResult<CustomerDto>> AddAddress(Guid id, [FromBody] CustomerAddressRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new AddCustomerAddressCommand
        {
            CustomerId = id,
            Label = request.Label,
            AddressLine = request.AddressLine,
            References = request.References,
            IsDefault = request.IsDefault
        }, cancellationToken));

    [HttpPut("{id:guid}/addresses/{addressId:guid}")]
    public async Task<ActionResult<CustomerDto>> UpdateAddress(Guid id, Guid addressId, [FromBody] CustomerAddressRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new UpdateCustomerAddressCommand
        {
            CustomerId = id,
            AddressId = addressId,
            Label = request.Label,
            AddressLine = request.AddressLine,
            References = request.References,
            IsDefault = request.IsDefault
        }, cancellationToken));

    [HttpDelete("{id:guid}/addresses/{addressId:guid}")]
    public async Task<ActionResult<CustomerDto>> RemoveAddress(Guid id, Guid addressId, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new RemoveCustomerAddressCommand(id, addressId), cancellationToken));

    private ActionResult<CustomerDto> ToResult(Result<CustomerDto> result)
        => result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);

    private ActionResult<CustomerAccountDto> ToResult(Result<CustomerAccountDto> result)
        => result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
}

public record SetCustomerActiveRequest(bool IsActive);

public record UpdateCustomerNotesRequest(string? Notes);

public record UpdateCustomerCreditLimitRequest(decimal? CreditLimit);

public record CustomerAddressRequest(string AddressLine, string? Label = null, string? References = null, bool IsDefault = false);


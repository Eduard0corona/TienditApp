using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Orders.Queries;
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
        [FromQuery] bool overdueOnly = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetReceivablesQuery
        {
            Search = search,
            Sort = sort,
            OverdueOnly = overdueOnly,
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

    /// <summary>
    /// What a customer leaves in their favor (Fase 26D): it adds to their balance and enters the drawer of whoever charges as
    /// a movement of its own, not a sale (needs an open drawer).
    /// </summary>
    [HttpPost("account/balance/deposits")]
    public async Task<ActionResult<CustomerAccountDto>> AddDeposit([FromBody] AddCustomerDepositCommand command, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(command, cancellationToken));

    /// <summary>
    /// Gives a customer back part or all of their balance in their favor (Fase 26D), out of the drawer of whoever gives it
    /// (needs an open drawer and who may refund).
    /// </summary>
    [HttpPost("account/balance/returns")]
    public async Task<ActionResult<CustomerAccountDto>> ReturnBalance([FromBody] ReturnCustomerBalanceCommand command, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(command, cancellationToken));

    /// <summary>
    /// Forgives part or all of what a customer owes (Fase 26B), on one order or from the oldest: a record apart on each
    /// order, no money moves (owners and admins).
    /// </summary>
    [HttpPost("account/write-offs")]
    public async Task<ActionResult<CustomerAccountDto>> WriteOffAccount([FromBody] WriteOffCustomerAccountCommand command, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(command, cancellationToken));

    /// <summary>Undoes a write-off made by mistake (Fase 26B): its record stays and what it forgave is owed again (owners and admins).</summary>
    [HttpPost("account/write-offs/{id:guid}/reversal")]
    public async Task<ActionResult<CustomerAccountDto>> ReverseWriteOff(Guid id, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new ReverseCustomerWriteOffCommand { WriteOffId = id }, cancellationToken));

    /// <summary>How many days the business gives to pay what customers owe, or no term (Fase 26B; owners and admins).</summary>
    [HttpPut("credit-term")]
    public async Task<ActionResult<CustomerCreditTermDto>> UpdateCreditTerm([FromBody] UpdateCustomerCreditTermCommand command, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(command, cancellationToken));

    /// <summary>The reminders of what is overdue and what setting them up needs (Fase 26C; owners and admins).</summary>
    [HttpGet("credit-reminders")]
    public async Task<ActionResult<CreditRemindersDto>> GetCreditReminders(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetCreditRemindersQuery(), cancellationToken));

    /// <summary>Turns the WhatsApp reminders of what is overdue on or off, every how many days and with which template (Fase 26C).</summary>
    [HttpPut("credit-reminders")]
    public async Task<ActionResult<CreditRemindersDto>> UpdateCreditReminders([FromBody] UpdateCreditRemindersCommand command, CancellationToken cancellationToken)
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

    /// <summary>The customer's own term to pay what they owe; null gives them the business's (Fase 26C; owners and admins).</summary>
    [HttpPut("{id:guid}/credit-term")]
    public async Task<ActionResult<CustomerDto>> SetCreditTerm(Guid id, [FromBody] SetCustomerCreditTermRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new SetCustomerCreditTermCommand { CustomerId = id, CreditTermDays = request.CreditTermDays }, cancellationToken));

    /// <summary>What the customer's invoices go to (Fase 28A) and where they get them (Fase 28E); without data both are forgotten.</summary>
    [HttpPut("{id:guid}/fiscal-data")]
    public async Task<ActionResult<CustomerDto>> UpdateFiscalData(Guid id, [FromBody] UpdateCustomerFiscalDataRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new UpdateCustomerFiscalDataCommand(id, request.FiscalData, request.InvoiceEmail), cancellationToken));

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

    private ActionResult<CustomerCreditTermDto> ToResult(Result<CustomerCreditTermDto> result)
        => result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);

    private ActionResult<CreditRemindersDto> ToResult(Result<CreditRemindersDto> result)
        => result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
}

public record SetCustomerActiveRequest(bool IsActive);

public record UpdateCustomerNotesRequest(string? Notes);

public record UpdateCustomerCreditLimitRequest(decimal? CreditLimit);

public record SetCustomerCreditTermRequest(int? CreditTermDays);

public record CustomerAddressRequest(string AddressLine, string? Label = null, string? References = null, bool IsDefault = false);

public class UpdateCustomerFiscalDataRequest
{
    /// <summary>RFC, name, fiscal postal code, regime and use of the CFDI; null forgets them.</summary>
    public CfdiReceiverInput? FiscalData { get; init; }

    /// <summary>The email the customer gets their invoices at, kept with the fiscal data: left out, it stays; empty, it is forgotten.</summary>
    public string? InvoiceEmail { get; init; }
}

using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
public class CashController : ControllerBase
{
    private readonly IMediator _mediator;

    public CashController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("current-session")]
    public async Task<ActionResult<CashSessionDto?>> GetCurrentSession()
    {
        var result = await _mediator.Send(new GetCurrentCashSessionQuery());
        return Ok(result);
    }

    [HttpPost("open")]
    public async Task<ActionResult<CashSessionDto>> Open([FromBody] OpenCashSessionCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<CashSessionDto>> Close(Guid id, [FromBody] CloseCashSessionRequest request)
    {
        var result = await _mediator.Send(new CloseCashSessionCommand
        {
            CashSessionId = id,
            ClosingAmount = request.ClosingAmount,
            Notes = request.Notes
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<PaginatedList<CashTransactionDto>>> GetTransactions(
        [FromQuery] Guid? cashSessionId,
        [FromQuery] CashTransactionType? transactionType,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetCashTransactionsQuery
        {
            CashSessionId = cashSessionId,
            TransactionType = transactionType,
            FromDate = fromDate,
            ToDate = toDate,
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    [HttpPost("transactions")]
    public async Task<ActionResult<CashTransactionDto>> AddTransaction([FromBody] AddCashTransactionRequest request)
    {
        var result = await _mediator.Send(new AddCashTransactionCommand
        {
            CashSessionId = request.CashSessionId,
            TransactionType = request.TransactionType,
            Amount = request.Amount,
            OccurredAt = request.OccurredAt,
            PaymentMethod = request.PaymentMethod,
            OrderId = request.OrderId,
            Reference = request.Reference,
            Notes = request.Notes
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(result.Value);
    }
}

public class CloseCashSessionRequest
{
    public decimal ClosingAmount { get; init; }
    public string? Notes { get; init; }
}

public class AddCashTransactionRequest
{
    public Guid CashSessionId { get; init; }
    public CashTransactionType TransactionType { get; init; }
    public decimal Amount { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public OrderPaymentMethod? PaymentMethod { get; init; }
    public Guid? OrderId { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

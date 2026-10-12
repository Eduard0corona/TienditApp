using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Reports.DTOs;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("operational-summary")]
    public async Task<ActionResult<OperationalReportDto>> GetOperationalSummary(
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOperationalReportQuery
        {
            FromDate = fromDate,
            ToDate = toDate
        }, cancellationToken);
        return Ok(result);
    }

    /// <summary>Sales by branch and by day between two local dates (the current month when omitted).</summary>
    [HttpGet("sales-by-branch")]
    public async Task<ActionResult<SalesByBranchReportDto>> GetSalesByBranch(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSalesByBranchQuery
        {
            From = from,
            To = to,
            BranchId = branchId
        }, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sales by where the orders came from between two local dates (the current month when omitted), with what each
    /// delivery platform charged, its commission and its IVA, what it withheld and what it should deposit.
    /// </summary>
    [HttpGet("sales-by-channel")]
    public async Task<ActionResult<SalesByChannelReportDto>> GetSalesByChannel(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSalesByChannelQuery
        {
            From = from,
            To = to,
            BranchId = branchId
        }, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The orders of one delivery platform registered between two local dates, the newest first, to compare folio by
    /// folio with its weekly statement.
    /// </summary>
    /// <param name="channel">Rappi, UberEats or DidiFood.</param>
    [HttpGet("delivery-platform-orders")]
    public async Task<ActionResult<PaginatedList<DeliveryPlatformOrderDto>>> GetDeliveryPlatformOrders(
        [FromQuery] OrderChannel? channel,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? branchId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = GetDeliveryPlatformOrdersQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetDeliveryPlatformOrdersQuery
        {
            Channel = channel,
            From = from,
            To = to,
            BranchId = branchId,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);
        return Ok(result);
    }
}

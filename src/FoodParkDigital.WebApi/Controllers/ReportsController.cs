using FoodParkDigital.Application.Reports.DTOs;
using FoodParkDigital.Application.Reports.Queries;
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
}

using FoodParkDigital.Application.Reports.DTOs;
using FoodParkDigital.Application.Reports.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
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
        [FromQuery] DateTimeOffset? toDate)
    {
        var result = await _mediator.Send(new GetOperationalReportQuery
        {
            FromDate = fromDate,
            ToDate = toDate
        });
        return Ok(result);
    }
}

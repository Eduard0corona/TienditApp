using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
// Same audience as ReportsController: TenantResolutionMiddleware requires ViewReports, which staff
// have by default. Admin-only roles here made the default landing page answer 403 to every employee.
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<DashboardMetricsDto>> GetMetrics(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to)
    {
        var result = await _mediator.Send(new GetDashboardMetricsQuery
        {
            FromDate = from,
            ToDate = to
        });
        return Ok(result);
    }
}

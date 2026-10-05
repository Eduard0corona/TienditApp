using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
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

    /// <summary>What needs someone now (the bell and "Requiere atención"), only for what the person may handle.</summary>
    [HttpGet("attention")]
    public async Task<ActionResult<AttentionDto>> GetAttention(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetAttentionQuery(), cancellationToken));
}

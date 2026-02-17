using FoodParkDigital.Application.Marketing.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN")]
public class MarketingController : ControllerBase
{
    private readonly IMediator _mediator;

    public MarketingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("promotions/send")]
    public async Task<IActionResult> SendPromotion([FromBody] SendPromotionalMessageCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return Ok(new { sent = result.Value });
    }
}

using FoodParkDigital.Application.Marketing.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
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
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(new { sent = result.Value });
    }
}

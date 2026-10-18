using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Reviews;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Reseñas (Fase 24H): what customers say about the business. Its owners and admins read them, answer them and report
/// them to the platform; a report never takes a review down.
/// </summary>
[ApiController]
[Route("api/v1/reviews")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReviewsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The business's reviews, newest first, with how customers rate it and what is waiting.</summary>
    [HttpGet]
    public async Task<ActionResult<BusinessReviewsDto>> GetReviews(
        [FromQuery] BusinessReviewsFilter filter = BusinessReviewsFilter.All,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetBusinessReviewsQuery(filter, page, pageSize), cancellationToken));

    /// <summary>Answers a review, or changes the answer.</summary>
    [HttpPut("{id:guid}/reply")]
    public async Task<ActionResult<BusinessReviewDto>> Reply(Guid id, [FromBody] ReviewReplyRequest request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new ReplyToReviewCommand(id, request.Text), cancellationToken));

    /// <summary>Takes the answer down.</summary>
    [HttpDelete("{id:guid}/reply")]
    public async Task<ActionResult<BusinessReviewDto>> RemoveReply(Guid id, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new RemoveReviewReplyCommand(id), cancellationToken));

    /// <summary>Asks the platform to look at a review, and why; it stays published meanwhile.</summary>
    [HttpPost("{id:guid}/report")]
    public async Task<ActionResult<BusinessReviewDto>> Report(Guid id, [FromBody] ReviewReportRequest request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new ReportReviewCommand(id, request.Reason), cancellationToken));

    // Another business's review, or one deleted by its author, is not found.
    private ActionResult<BusinessReviewDto> Answer(Result<BusinessReviewDto> result)
        => result.IsSuccess
            ? Ok(result.Value)
            : this.ApiError(result.Error == ReviewRules.NotFoundMessage ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, result.Error!);
}

public record ReviewReplyRequest(string? Text);

public record ReviewReportRequest(ReviewConcern Reason);

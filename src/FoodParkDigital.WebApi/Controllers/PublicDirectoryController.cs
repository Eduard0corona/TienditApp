using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Explorar (/explorar in the app): the businesses that published their menu, by category, with no sign-in and only what their
/// menu already shows. Rate limited per IP like the menu.
/// </summary>
[ApiController]
[Route("api/v1/public/directory")]
[AllowAnonymous]
public class PublicDirectoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicDirectoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <param name="category">Only the businesses of this category; all without one.</param>
    /// <param name="q">Words of the name or the description.</param>
    /// <param name="pageNumber">From 1.</param>
    /// <param name="pageSize">Up to 48.</param>
    /// <param name="postalCode">The customer's postal code: each card says whether the business delivers there and what it costs.</param>
    /// <param name="latitude">
    /// The customer's position, with <paramref name="longitude"/> («Cerca de mí»): the nearest businesses first, each card
    /// with how far it is. Read to three decimals; never kept or logged.
    /// </param>
    /// <param name="longitude">The other half of the customer's position.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    [HttpGet]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicDirectoryDto>> GetDirectory(
        [FromQuery] BusinessCategory? category,
        [FromQuery] string? q,
        [FromQuery] string? postalCode,
        [FromQuery] decimal? latitude,
        [FromQuery] decimal? longitude,
        CancellationToken cancellationToken,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = GetPublicDirectoryQuery.DefaultPageSize)
        => Ok(await _mediator.Send(
            new GetPublicDirectoryQuery(category, q, pageNumber, pageSize, postalCode, latitude, longitude), cancellationToken));
}

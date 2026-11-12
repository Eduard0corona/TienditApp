using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using FoodParkDigital.WebApi.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FoodParkDigital.WebApi.Http;

/// <summary>
/// The writes, changes and deletions of one customer account's reviews (Fase 31K, ADR 0091): a new review emails the
/// business's team, so deleting and writing it again in a loop would flood them. Per account, which the address alone
/// cannot be: many customers share one (a food park's Wi-Fi, a carrier's NAT) and one account changes it. The app's rate
/// limiter runs before the session is read, so this runs as an action filter, once it is. Per instance, as the other
/// limits; the address keeps its own, higher one (<see cref="Controllers.MeController.ReviewsRateLimitPolicy"/>).
/// </summary>
public sealed class MyReviewsAccountLimit : IAsyncActionFilter, IDisposable
{
    /// <summary>What one account may send in an hour.</summary>
    public const int PerHour = 10;

    public const string Message = "Ya cambiaste tus reseñas varias veces en la última hora. Intenta de nuevo más tarde.";

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(account =>
        RateLimitPartition.GetFixedWindowLimiter(account, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PerHour,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Without a session the endpoint already answered 401: there is no account to count.
        var account = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (account is null)
        {
            await next();
            return;
        }

        using var lease = _limiter.AttemptAcquire(account);
        if (!lease.IsAcquired)
        {
            if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            context.Result = new ObjectResult(ApiProblem.Create(context.HttpContext, StatusCodes.Status429TooManyRequests, Message, ApiErrorCodes.TooManyRequests))
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
                ContentTypes = { ApiProblem.ContentType }
            };
            return;
        }

        await next();
    }

    public void Dispose() => _limiter.Dispose();
}

namespace FoodParkDigital.WebApi.Middleware;

/// <summary>
/// Transfer receipts and service photos uploaded before they became private sit under wwwroot/uploads/orders until
/// they move: the static file middleware never serves them. The business's members open them through
/// <c>/api/v1/files</c>.
/// </summary>
public class PrivateUploadsGuardMiddleware
{
    private readonly RequestDelegate _next;

    public PrivateUploadsGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Case-insensitive, like a Windows file system.
        if (context.Request.Path.StartsWithSegments("/uploads/orders", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return _next(context);
    }
}

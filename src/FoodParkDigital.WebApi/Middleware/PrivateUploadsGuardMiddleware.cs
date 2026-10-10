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
        if (AsTheFileSystemReadsIt(context.Request.Path).StartsWithSegments("/uploads/orders", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return _next(context);
    }

    /// <summary>
    /// The path as the file provider resolves it. Kestrel removes "." and ".." but keeps empty segments and
    /// backslashes, and the provider reads "/uploads//orders" and, on Windows, "/uploads\orders" as the same folder.
    /// </summary>
    private static PathString AsTheFileSystemReadsIt(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return path;

        var segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return new PathString("/" + string.Join('/', segments));
    }
}

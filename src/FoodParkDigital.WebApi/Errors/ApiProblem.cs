using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Errors;

/// <summary>
/// The one shape of an error response: <see cref="ProblemDetails"/> with <c>code</c> (see <see cref="ApiErrorCodes"/>),
/// <c>traceId</c> and, while clients migrate, <c>error</c> with the same text as <c>detail</c>.
/// </summary>
public static class ApiProblem
{
    public const string ContentType = "application/problem+json";

    public static ProblemDetails Create(HttpContext httpContext, int status, string? detail, string? code = null, string? title = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title ?? DefaultTitle(status),
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        Complete(problem, httpContext, code);
        return problem;
    }

    /// <summary>Adds what every error carries; keeps a code or trace id already set.</summary>
    public static void Complete(ProblemDetails problem, HttpContext httpContext, string? code = null)
    {
        var status = problem.Status ?? httpContext.Response.StatusCode;
        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = code
                ?? (problem is ValidationProblemDetails ? ApiErrorCodes.ValidationFailed : ApiErrorCodes.ForStatus(status));
        }

        if (!problem.Extensions.ContainsKey("traceId"))
            problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(problem.Detail) && !problem.Extensions.ContainsKey("error"))
            problem.Extensions["error"] = problem.Detail;
    }

    public static async Task WriteAsync(HttpContext httpContext, int status, string? detail, string? code = null, string? title = null)
    {
        var problem = Create(httpContext, status, detail, code, title);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, ContentType);
    }

    private static string DefaultTitle(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        _ => status >= 500 ? "Internal Server Error" : "Error"
    };
}

public static class ApiProblemControllerExtensions
{
    /// <summary>An error answer of a controller, with the code of its status unless a more specific one is given.</summary>
    public static ObjectResult ApiError(this ControllerBase controller, int status, string? detail, string? code = null)
        => new(ApiProblem.Create(controller.HttpContext, status, detail, code))
        {
            StatusCode = status,
            ContentTypes = { ApiProblem.ContentType }
        };
}

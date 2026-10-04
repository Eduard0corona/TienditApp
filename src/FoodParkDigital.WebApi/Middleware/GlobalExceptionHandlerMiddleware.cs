using System.Diagnostics;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly bool _logSensitiveData;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        IHostEnvironment environment,
        ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        // SQL Server error messages quote row values (phone numbers, emails): only Development logs them.
        _logSensitiveData = environment.IsDevelopment();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = exception switch
        {
            InvalidOrderStateTransitionException => (StatusCodes.Status409Conflict, "Invalid State Transition"),
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Entity Not Found"),
            TenantMismatchException => (StatusCodes.Status403Forbidden, "Tenant Access Denied"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Domain Rule Violation"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation Error"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency Conflict"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        // Also returned to the client, so support can find the entry of a reported error.
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var loggableException = LoggableException(exception);
        _logger.LogError(
            loggableException,
            "Unhandled exception {ErrorType} (SQL error {SqlErrorNumber}, TraceId {TraceId}): {Message}",
            exception.GetType().Name,
            LogRedaction.GetSqlErrorNumber(exception),
            traceId,
            loggableException.Message);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = statusCode == 500 ? "An unexpected error occurred." : exception.Message,
            Instance = context.Request.Path
        };
        problemDetails.Extensions["traceId"] = traceId;

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problemDetails);
    }

    private Exception LoggableException(Exception exception)
        => _logSensitiveData ? exception : LogRedaction.RedactDatabaseMessages(exception);
}

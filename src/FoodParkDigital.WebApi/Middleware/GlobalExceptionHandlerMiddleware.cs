using System.Diagnostics;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.WebApi.Errors;
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
        var (statusCode, title, code) = exception switch
        {
            InvalidOrderStateTransitionException => (StatusCodes.Status409Conflict, "Invalid State Transition", ApiErrorCodes.InvalidStateTransition),
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Entity Not Found", ApiErrorCodes.NotFound),
            TenantMismatchException => (StatusCodes.Status403Forbidden, "Tenant Access Denied", ApiErrorCodes.TenantAccessDenied),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Domain Rule Violation", ApiErrorCodes.DomainRule),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation Error", ApiErrorCodes.ValidationFailed),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency Conflict", ApiErrorCodes.ConcurrencyConflict),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", ApiErrorCodes.Unauthorized),
            ForbiddenAccessException { ModuleDisabled: true } => (StatusCodes.Status403Forbidden, "Tenant capability denied", ApiErrorCodes.ModuleDisabled),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, "Forbidden", ApiErrorCodes.Forbidden),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", ApiErrorCodes.ServerError)
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
            // What the person can act on, in Spanish; the technical message stays in the log above.
            Detail = exception switch
            {
                _ when statusCode == 500 => "Ocurrió un error inesperado.",
                // The validators' own message, not FluentValidation's summary of every field.
                ValidationException validation => validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Revisa los datos enviados.",
                InvalidOrderStateTransitionException => "El estado cambió mientras lo veías. Recarga para ver el estado actual.",
                EntityNotFoundException => "No se encontró el registro. Es posible que ya no exista.",
                TenantMismatchException => "No tienes acceso a este registro.",
                DbUpdateConcurrencyException => "Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.",
                _ => exception.Message
            },
            Instance = context.Request.Path
        };
        problemDetails.Extensions["traceId"] = traceId;

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        ApiProblem.Complete(problemDetails, context, code);
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problemDetails, (System.Text.Json.JsonSerializerOptions?)null, ApiProblem.ContentType);
    }

    private Exception LoggableException(Exception exception)
        => _logSensitiveData ? exception : LogRedaction.RedactDatabaseMessages(exception);
}

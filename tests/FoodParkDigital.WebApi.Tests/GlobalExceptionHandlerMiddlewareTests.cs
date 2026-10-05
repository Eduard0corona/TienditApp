using FoodParkDigital.Application.Customers.Services;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.WebApi.Tests;

public class GlobalExceptionHandlerMiddlewareTests
{
    private const string Email = "cliente@example.com";
    private const string NationalNumber = "5512345678";

    // What SQL Server answers when a unique index rejects a row (captured from LocalDB).
    private const string DuplicateUserNameMessage =
        "Cannot insert duplicate key row in object 'dbo.AspNetUsers' with unique index 'UserNameIndex'. "
        + "The duplicate key value is (CLIENTE@EXAMPLE.COM).";

    private const string DuplicateTemporaryCustomerMessage =
        "Cannot insert duplicate key row in object 'dbo.TemporaryCustomers' with unique index "
        + "'IX_TemporaryCustomers_TenantId_PhoneNormalized'. The duplicate key value is "
        + "(5e8ebc9a-52d5-47d9-9dbd-c07a7c7268a2, +525512345678).";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task InvokeAsync_WhenSqlServerRejectsSave_LogsErrorNumberAndTraceIdButNotTheValuesOutsideDevelopment(string environmentName)
    {
        using var activity = new Activity("request").Start();
        var logger = new CapturingLogger<GlobalExceptionHandlerMiddleware>();
        var context = NewHttpContext();
        var middleware = CreateMiddleware(environmentName, logger,
            _ => throw SqlExceptionFactory.SaveFailure(2601, DuplicateUserNameMessage));

        await middleware.InvokeAsync(context);

        Assert.Null(logger.FirstMentioning(Email));
        var error = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Equal(nameof(DbUpdateException), error["ErrorType"]);
        Assert.Equal(2601, error["SqlErrorNumber"]);
        Assert.Equal(activity.Id, error["TraceId"]);
        // Support keeps every exception type and the stack trace, only the messages are left out.
        var details = error.Exception!.ToString();
        Assert.Contains(typeof(DbUpdateException).FullName!, details);
        Assert.Contains($"{typeof(SqlException).FullName} (SQL error 2601)", details);
        Assert.Contains($"{nameof(GlobalExceptionHandlerMiddleware)}.{nameof(GlobalExceptionHandlerMiddleware.InvokeAsync)}", details);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var problem = ReadProblem(context);
        Assert.Equal("Ocurrió un error inesperado.", problem.GetProperty("detail").GetString());
        Assert.Equal("server_error", problem.GetProperty("code").GetString());
        Assert.Equal(activity.Id, problem.GetProperty("traceId").GetString());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task InvokeAsync_WhenTemporaryCustomerCannotBeCreated_DoesNotLogThePhoneOutsideDevelopment(string environmentName)
    {
        // The insert fails and the re-read finds no concurrent winner, so CustomerLookupService rethrows
        // the DbUpdateException, whose SQL Server message quotes the phone number, as an inner exception.
        await using var database = InMemoryApplicationDbContext.Create(
            new RejectingSaveChangesInterceptor(() => SqlExceptionFactory.SaveFailure(2601, DuplicateTemporaryCustomerMessage)));
        var customerLookup = new CustomerLookupService(
            database,
            new PhoneNormalizationService(),
            NullLogger<CustomerLookupService>.Instance);
        var logger = new CapturingLogger<GlobalExceptionHandlerMiddleware>();
        var context = NewHttpContext();
        var middleware = CreateMiddleware(environmentName, logger, async _ =>
        {
            await customerLookup.ResolveForOrderAsync(Guid.NewGuid(), "55 1234 5678", "Ana", "MX", CancellationToken.None);
        });

        await middleware.InvokeAsync(context);

        Assert.Null(logger.FirstMentioning(NationalNumber));
        var error = Assert.Single(logger.Entries);
        Assert.Equal(nameof(InvalidOperationException), error["ErrorType"]);
        Assert.Equal(2601, error["SqlErrorNumber"]);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain(NationalNumber, ReadBody(context));
    }

    [Fact]
    public async Task InvokeAsync_InDevelopment_LogsTheFullExceptionForDebugging()
    {
        var logger = new CapturingLogger<GlobalExceptionHandlerMiddleware>();
        var failure = SqlExceptionFactory.SaveFailure(2601, DuplicateUserNameMessage);
        var middleware = CreateMiddleware(Environments.Development, logger, _ => throw failure);

        await middleware.InvokeAsync(NewHttpContext());

        var error = Assert.Single(logger.Entries);
        Assert.Same(failure, error.Exception);
        Assert.Equal(2601, error["SqlErrorNumber"]);
        Assert.NotNull(logger.FirstMentioning(Email));
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionDoesNotComeFromTheDatabase_LogsItWithItsMessageOutsideDevelopment()
    {
        var logger = new CapturingLogger<GlobalExceptionHandlerMiddleware>();
        var failure = new InvalidOperationException("Sequence contains no elements");
        var middleware = CreateMiddleware(Environments.Production, logger, _ => throw failure);

        await middleware.InvokeAsync(NewHttpContext());

        var error = Assert.Single(logger.Entries);
        Assert.Same(failure, error.Exception);
        Assert.Equal(failure.Message, error["Message"]);
        Assert.Equal(nameof(InvalidOperationException), error["ErrorType"]);
        Assert.Null(error["SqlErrorNumber"]);
    }

    [Fact]
    public async Task InvokeAsync_WhenDomainRuleFails_ReturnsTheTraceIdWithTheProblemDetails()
    {
        using var activity = new Activity("request").Start();
        var context = NewHttpContext();
        var middleware = CreateMiddleware(Environments.Production, new CapturingLogger<GlobalExceptionHandlerMiddleware>(),
            _ => throw new DomainException("La orden ya fue pagada."));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        var problem = ReadProblem(context);
        Assert.Equal("La orden ya fue pagada.", problem.GetProperty("detail").GetString());
        Assert.Equal(activity.Id, problem.GetProperty("traceId").GetString());
    }

    // UseMiddleware builds it the same way: the next delegate plus constructor services from DI.
    private static GlobalExceptionHandlerMiddleware CreateMiddleware(
        string environmentName,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        RequestDelegate next)
    {
        var services = new ServiceCollection()
            .AddSingleton(HostEnvironment(environmentName))
            .AddSingleton(logger)
            .BuildServiceProvider();

        return ActivatorUtilities.CreateInstance<GlobalExceptionHandlerMiddleware>(services, next);
    }

    private static IHostEnvironment HostEnvironment(string environmentName)
        => new HostingEnvironment { EnvironmentName = environmentName };

    private static DefaultHttpContext NewHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadBody(HttpContext context)
        => Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static JsonElement ReadProblem(HttpContext context)
    {
        using var document = JsonDocument.Parse(ReadBody(context));
        return document.RootElement.Clone();
    }

    /// <summary>Fails every save with the given exception before it reaches the database.</summary>
    private sealed class RejectingSaveChangesInterceptor(Func<Exception> failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw failure();
    }
}

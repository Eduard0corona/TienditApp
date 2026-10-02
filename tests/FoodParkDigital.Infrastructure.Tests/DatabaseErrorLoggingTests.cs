using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// EF Core logs a failed save or query itself, provider exception included, before rethrowing it.
/// These tests use the DbContext registration of <see cref="DependencyInjection.AddInfrastructureServices"/>.
/// </summary>
public class DatabaseErrorLoggingTests
{
    private const string PhoneNumber = "+525512345678";
    private const string NationalNumber = "5512345678";

    // What SQL Server answers when the unique index rejects the row (captured from LocalDB).
    private const string DuplicateKeyMessage =
        "Cannot insert duplicate key row in object 'dbo.TemporaryCustomers' with unique index "
        + "'IX_TemporaryCustomers_TenantId_PhoneNormalized'. The duplicate key value is "
        + "(5e8ebc9a-52d5-47d9-9dbd-c07a7c7268a2, +525512345678).";

    // SQL Server error 245 quotes the value it could not convert.
    private const string ConversionFailedMessage =
        "Conversion failed when converting the nvarchar value '+525512345678' to data type int.";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task SaveChangesAsync_WhenSqlServerRejectsRow_DoesNotLogItsMessageOutsideDevelopment(string environmentName)
    {
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(environmentName, logs, () => SqlExceptionFactory.Create(2601, DuplicateKeyMessage));
        await using var scope = services.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => SaveTemporaryCustomerAsync(scope.ServiceProvider));

        // The exception still reaches the caller, which decides how to log it.
        Assert.Equal(2601, Assert.IsType<SqlException>(exception.InnerException).Number);
        Assert.Null(logs.FirstMentioning(NationalNumber));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task QueryAsync_WhenSqlServerFails_DoesNotLogItsMessageOutsideDevelopment(string environmentName)
    {
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(environmentName, logs, () => SqlExceptionFactory.Create(245, ConversionFailedMessage));
        await using var scope = services.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<SqlException>(() => FindTemporaryCustomerAsync(scope.ServiceProvider));

        Assert.Equal(245, exception.Number);
        Assert.Null(logs.FirstMentioning(NationalNumber));
    }

    [Fact]
    public async Task SaveChangesAndQueryAsync_InDevelopment_LogTheSqlServerMessageForDebugging()
    {
        var logs = new CapturingLoggerProvider();
        await using var services = BuildServices(Environments.Development, logs, () => SqlExceptionFactory.Create(2601, DuplicateKeyMessage));
        await using var scope = services.CreateAsyncScope();

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveTemporaryCustomerAsync(scope.ServiceProvider));
        await Assert.ThrowsAsync<SqlException>(() => FindTemporaryCustomerAsync(scope.ServiceProvider));

        Assert.Contains(logs.Entries, entry =>
            entry.Category == DbLoggerCategory.Update.Name && entry.Level == LogLevel.Error && entry.Mentions(NationalNumber));
        Assert.Contains(logs.Entries, entry =>
            entry.Category == DbLoggerCategory.Query.Name && entry.Level == LogLevel.Error && entry.Mentions(NationalNumber));
    }

    private static Task<int> SaveTemporaryCustomerAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.TemporaryCustomers.Add(new TemporaryCustomer(Guid.NewGuid(), PhoneNumber, "Ana"));
        return context.SaveChangesAsync();
    }

    private static Task<TemporaryCustomer?> FindTemporaryCustomerAsync(IServiceProvider services)
    {
        // A captured variable, so the phone travels as a parameter and not inside the SQL text.
        var phoneNumber = PhoneNumber;
        return services.GetRequiredService<ApplicationDbContext>().TemporaryCustomers
            .FirstOrDefaultAsync(x => x.PhoneNormalized == phoneNumber);
    }

    private static ServiceProvider BuildServices(
        string environmentName,
        CapturingLoggerProvider logs,
        Func<Exception> sqlServerFailure)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never opened: FailingSqlServerInterceptor answers instead of a server.
                ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused;Trusted_Connection=True"
            })
            .Build();

        var services = new ServiceCollection();
        // Every level, so a passing test means the events are dropped and not only below the threshold.
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddSingleton<IMediator>(provider => new Mediator(provider));
        services.AddInfrastructureServices(configuration);
        services.ConfigureDbContext<ApplicationDbContext>(options =>
            options.AddInterceptors(new FailingSqlServerInterceptor(sqlServerFailure)));

        return services.BuildServiceProvider();
    }
}

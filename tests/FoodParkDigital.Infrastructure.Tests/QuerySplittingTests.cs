using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The API's context reads each collection of a query in its own statement: an order with its lines, payments, refunds
/// and history in a single join would repeat the order row once per combination. Uses the registration of
/// <see cref="DependencyInjection.AddInfrastructureServices"/>.
/// </summary>
public class QuerySplittingTests
{
    [Fact]
    public async Task TheApisContext_ShouldReadEachCollectionInItsOwnStatement()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never opened: the test only reads the options of the context.
                ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused;Trusted_Connection=True"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production));
        services.AddSingleton<IMediator>(provider => new Mediator(provider));
        services.AddInfrastructureServices(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());

        Assert.Equal(QuerySplittingBehavior.SplitQuery, relational.QuerySplittingBehavior);
    }
}

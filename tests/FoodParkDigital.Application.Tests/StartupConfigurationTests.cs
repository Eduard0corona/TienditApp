using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Seeds;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

public class StartupConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("demasiado-corta")]
    public void JwtSecretPolicy_ShouldRejectMissingOrShortSecrets(string? secret)
    {
        Assert.NotNull(JwtSecretPolicy.GetValidationError(secret));
    }

    [Fact]
    public void JwtSecretPolicy_ShouldAcceptRandomSecretOfMinimumLength()
    {
        Assert.Null(JwtSecretPolicy.GetValidationError(new string('k', JwtSecretPolicy.MinimumSecretBytes)));
        Assert.Null(JwtSecretPolicy.GetValidationError(JwtSecretPolicy.CreateEphemeralSecret()));
    }

    [Fact]
    public void JwtSecretPolicy_ShouldCreateDifferentEphemeralSecrets()
    {
        Assert.NotEqual(JwtSecretPolicy.CreateEphemeralSecret(), JwtSecretPolicy.CreateEphemeralSecret());
    }

    [Theory]
    [InlineData("Server=localhost;Database=TienditApp;Trusted_Connection=True", true)]
    [InlineData("Server=localhost,1433;Database=TienditApp;User Id=sa;Password=x", true)]
    [InlineData("Server=127.0.0.1;Database=TienditApp;Trusted_Connection=True", true)]
    [InlineData("Server=.;Database=TienditApp;Trusted_Connection=True", true)]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=TienditApp;Trusted_Connection=True", true)]
    [InlineData("Server=EDUARDO\\EDUARDOSERVER;Database=TienditApp;Trusted_Connection=True", true)]
    [InlineData("Server=sqlserver;Database=FoodParkDigital;User Id=sa;Password=x", true)]
    [InlineData("Server=tcp:demo.database.windows.net,1433;Initial Catalog=demo;User ID=u;Password=p", false)]
    [InlineData("Server=10.0.0.15;Database=TienditApp;User Id=sa;Password=x", false)]
    [InlineData("Server=sql.example.com;Database=TienditApp;User Id=sa;Password=x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DatabaseStartupOptions_ShouldOnlyTreatLocalServersAsLocal(string? connectionString, bool expected)
    {
        Assert.Equal(expected, DatabaseStartupOptions.IsLocalDataSource(connectionString));
    }

    [Fact]
    public void DatabaseStartupOptions_GetHost_ShouldNotExposeCredentials()
    {
        var host = DatabaseStartupOptions.GetHost(
            "Server=tcp:demo.database.windows.net,1433;Initial Catalog=demo;User ID=admin;Password=S3cret!");

        Assert.Equal("demo.database.windows.net", host);
    }

    [Fact]
    public async Task SeedData_ShouldNotCreateSuperAdmin_WhenPasswordIsNotConfigured()
    {
        await using var provider = BuildServices(new Dictionary<string, string?>());
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await SeedData.SeedAsync(context, scope.ServiceProvider);

        Assert.False(await context.Users.AnyAsync());
        Assert.True(await context.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == "system"));
        Assert.True(await context.Roles.AnyAsync(r => r.Name == "SUPER_ADMIN"));
    }

    [Fact]
    public async Task SeedData_ShouldCreateSuperAdmin_WithConfiguredCredentials()
    {
        await using var provider = BuildServices(new Dictionary<string, string?>
        {
            ["Seed:SuperAdminEmail"] = "root@example.com",
            ["Seed:SuperAdminPassword"] = "Local-Passw0rd"
        });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedData.SeedAsync(context, scope.ServiceProvider);

        var admin = await userManager.FindByEmailAsync("root@example.com");
        Assert.NotNull(admin);
        Assert.True(await userManager.CheckPasswordAsync(admin!, "Local-Passw0rd"));
        Assert.True(await userManager.IsInRoleAsync(admin!, "SUPER_ADMIN"));
        Assert.True(await context.TenantUsers.IgnoreQueryFilters().AnyAsync(x => x.UserId == admin!.Id));
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddScoped<ICurrentTenantService, TestCurrentTenantService>();
        services.AddSingleton<IMediator, NullMediator>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
    }
}

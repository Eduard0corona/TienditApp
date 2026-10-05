using System.Reflection;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.WebApi.Controllers;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The architecture rules of AGENTS.md, checked on the compiled assemblies: dependencies point inwards
/// (Domain ← Application ← Infrastructure ← WebApi), use cases live in Application, controllers stay thin and every
/// entity of a business is filtered by it. A change that breaks a rule fails here; an exception is added to its list
/// with the reason.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(BaseEntity).Assembly;
    private static readonly Assembly Application = typeof(Result).Assembly;
    private static readonly Assembly Infrastructure = typeof(ApplicationDbContext).Assembly;
    private static readonly Assembly WebApi = typeof(OrdersController).Assembly;

    [Fact]
    public void Layers_ShouldOnlyDependInwards()
    {
        Assert.Empty(ProjectReferences(Domain));
        Assert.Equal(["FoodParkDigital.Domain"], ProjectReferences(Application));
        Assert.DoesNotContain("FoodParkDigital.WebApi", ProjectReferences(Infrastructure));
    }

    [Fact]
    public void Domain_ShouldNotKnowPersistenceWebOrProviders()
    {
        // MediatR.Contracts (domain events) and Identity stores (ApplicationUser) are the known technical
        // dependencies of the domain (layers-and-dependencies.md); nothing else may join them.
        var allowed = new[] { "MediatR.Contracts", "Microsoft.Extensions.Identity.Stores", "Microsoft.Extensions.Identity.Core" };

        var offending = ExternalReferences(Domain).Where(name => !allowed.Contains(name)).ToList();

        Assert.True(offending.Count == 0, "Domain references: " + string.Join(", ", offending));
    }

    [Fact]
    public void Application_ShouldNotKnowHttpOrDatabaseProviders()
    {
        string[] forbiddenPrefixes =
        [
            "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Hosting",
            "Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Data.SqlClient", "Azure.", "Swashbuckle", "Serilog", "SixLabors"
        ];

        var offending = ExternalReferences(Application)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(offending.Count == 0, "Application references: " + string.Join(", ", offending));
    }

    [Fact]
    public void UseCases_ShouldLiveInApplication()
    {
        var misplaced = new[] { Infrastructure, WebApi }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(i =>
                i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                                    || i.GetGenericTypeDefinition() == typeof(IRequestHandler<>))))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(misplaced.Count == 0, "Request handlers outside Application: " + string.Join(", ", misplaced));
    }

    [Fact]
    public void Controllers_ShouldNotUseTheDatabase()
    {
        // The webhook still resolves the business and the statuses itself (technical-debt.md, "Media").
        var knownExceptions = new HashSet<string> { "WhatsAppWebhookController" };

        var offending = WebApi.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => !knownExceptions.Contains(type.Name))
            .Where(type => type.GetConstructors().SelectMany(c => c.GetParameters()).Any(parameter =>
                typeof(DbContext).IsAssignableFrom(parameter.ParameterType)
                || parameter.ParameterType == typeof(IApplicationDbContext)))
            .Select(type => type.Name)
            .ToList();

        Assert.True(offending.Count == 0, "Controllers with database access: " + string.Join(", ", offending));
    }

    [Fact]
    public void EntitiesWithATenant_ShouldBeFilteredByIt()
    {
        // One row per business read before there is a business in context (login, the WhatsApp webhook): they are
        // queried explicitly by TenantId instead.
        var readWithoutTenantContext = new HashSet<string> { "TenantSettings", "WhatsAppConfig" };

        using var context = NewContext();
        var entityTypes = context.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList();

        var notScoped = entityTypes
            .Where(e => e.ClrType.GetProperty("TenantId")?.PropertyType == typeof(Guid))
            .Where(e => !typeof(ITenantScoped).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType.Name)
            .Where(name => !readWithoutTenantContext.Contains(name))
            .ToList();
        var unfiltered = entityTypes
            .Where(e => typeof(ITenantScoped).IsAssignableFrom(e.ClrType) && e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(notScoped.Count == 0, "Entities with TenantId that are not ITenantScoped: " + string.Join(", ", notScoped));
        Assert.True(unfiltered.Count == 0, "ITenantScoped entities without the tenant filter: " + string.Join(", ", unfiltered));
    }

    [Fact]
    public void EntitiesWithoutATenant_ShouldBeAKnownCase()
    {
        // Platform-wide data, accounts, or children reached only through a tenant-scoped parent. A new entity without
        // TenantId must be added here on purpose (and its parent must be filtered).
        var known = new HashSet<string>
        {
            // The platform: businesses, plans, its own WhatsApp number and the delivery records of the outbox and inbox.
            "Tenant", "Plan", "OwnerWhatsAppConfig", "OutboxMessageConsumer", "InboxMessage",
            // Accounts (Identity), shared by the businesses of a person, and their sessions and customer profile.
            "ApplicationUser", "ApplicationRole", "IdentityUserRole`1", "IdentityUserClaim`1", "IdentityUserLogin`1",
            "IdentityRoleClaim`1", "IdentityUserToken`1", "RefreshToken", "CustomerProfile",
            // Children of a tenant-scoped aggregate, reached only through it.
            "OrderItem", "OrderStatusHistory", "WhatsAppMessage"
        };

        using var context = NewContext();
        var unknown = context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType)
            .Where(e => e.ClrType.GetProperty("TenantId") is null)
            .Select(e => e.ClrType.Name)
            .Where(name => !known.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0, "Entities without TenantId to review: " + string.Join(", ", unknown));
    }

    private static List<string> ProjectReferences(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("FoodParkDigital.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static List<string> ExternalReferences(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("FoodParkDigital.", StringComparison.Ordinal)
                           && !name.StartsWith("System", StringComparison.Ordinal)
                           && name is not "netstandard" and not "mscorlib")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static ApplicationDbContext NewContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant(),
            new NoMediator());

    private sealed class NoTenant : ICurrentTenantService
    {
        public Guid TenantId => Guid.Empty;
        public void SetTenant(Guid tenantId) { }
    }

    private sealed class NoMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
    }
}

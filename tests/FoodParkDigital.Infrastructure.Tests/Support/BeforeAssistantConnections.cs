using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Assistants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 32E added (the assistants' OAuth clients, codes, connections and tokens): the app as deployed
/// before it, and what a test needs on a database migrated to an earlier migration, where its tables do not exist yet.
/// </summary>
internal static class BeforeAssistantConnections
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<AssistantToken>();
        modelBuilder.Ignore<AssistantAuthorizationCode>();
        modelBuilder.Ignore<AssistantConnection>();
        modelBuilder.Ignore<AssistantClient>();
    }

    private sealed class Context(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            Ignore(modelBuilder);
        }
    }
}

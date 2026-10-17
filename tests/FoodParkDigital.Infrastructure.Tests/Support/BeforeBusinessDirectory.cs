using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 24A added (the category and the description of a business, and whether it shows in
/// Explorar) nor what came after it (<see cref="BeforeOrderTracking"/>): the app as deployed before it, and what a test needs to seed or read businesses on a database migrated to an
/// earlier migration, where those columns do not exist yet.
/// </summary>
internal static class BeforeBusinessDirectory
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().Ignore(t => t.DirectoryCategory);
        modelBuilder.Entity<Tenant>().Ignore(t => t.PublicDescription);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.ListedInDirectory);
        BeforeOrderTracking.Ignore(modelBuilder);
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

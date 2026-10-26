using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 28F added (self-invoicing and the key of the tickets' codes): the app as deployed before
/// it, and what a test needs to seed or read invoicing profiles on a database migrated to an earlier migration, where the
/// columns do not exist yet.
/// </summary>
internal static class BeforeSelfInvoicing
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvoicingProfile>().Ignore(p => p.SelfInvoicing);
        modelBuilder.Entity<InvoicingProfile>().Ignore(p => p.SelfInvoiceKey);
        BeforeInvoiceSubstitution.Ignore(modelBuilder);
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

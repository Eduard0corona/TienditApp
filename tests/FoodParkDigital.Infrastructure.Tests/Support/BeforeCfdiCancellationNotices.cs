using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 28N added (the notice by email that the SAT cancelled a CFDI): the app as deployed before
/// it, and what a test needs to seed or read the emails of the CFDI on a database migrated to an earlier migration, where
/// the column does not exist yet.
/// </summary>
internal static class BeforeCfdiCancellationNotices
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CfdiEmail>().Ignore(e => e.Kind);
        BeforeAccountantCfdiEmails.Ignore(modelBuilder);
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

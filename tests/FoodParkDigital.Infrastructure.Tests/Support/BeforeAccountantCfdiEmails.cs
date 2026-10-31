using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 28O added (the month's CFDI to the accountant by email): the app as deployed before it, and
/// what a test needs to seed or read the invoicing profiles on a database migrated to an earlier migration, where the
/// columns and the table do not exist yet.
/// </summary>
internal static class BeforeAccountantCfdiEmails
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<CfdiMonthEmail>();
        var profile = modelBuilder.Entity<InvoicingProfile>();
        profile.Ignore(p => p.AccountantEmail);
        profile.Ignore(p => p.SendsMonthToAccountant);
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

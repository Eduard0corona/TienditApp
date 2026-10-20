using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Reporting.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 24I added to existing tables (what was paid online in each day of the sales report), nor
/// what came after it: the app as deployed before it, and what a test needs to read the sales report on a database
/// migrated to an earlier migration, where the column does not exist yet. Its new tables are not read there.
/// </summary>
internal static class BeforeOnlinePayments
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DailySalesSnapshot>().Ignore(s => s.OnlineAmount);
        BeforeCustomerCredit.Ignore(modelBuilder);
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

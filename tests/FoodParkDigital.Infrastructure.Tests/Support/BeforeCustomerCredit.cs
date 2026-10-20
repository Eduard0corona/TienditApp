using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 26A added (how much a business lets each customer owe, and the sales closed on credit):
/// the app as deployed before it, and what a test needs to seed or read customers and orders on a database migrated to an
/// earlier migration, where the columns do not exist yet.
/// </summary>
internal static class BeforeCustomerCredit
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Ignore(c => c.CreditLimit);
        modelBuilder.Entity<Order>().Ignore(o => o.SoldOnCredit);
        BeforeCustomerWriteOffs.Ignore(modelBuilder);
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

using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 26C added (a customer's own term and the reminders of what is overdue): the app as deployed
/// before it, and what a test needs to seed or read customers and settings on a database migrated to an earlier migration,
/// where the columns do not exist yet.
/// </summary>
internal static class BeforeCustomerCreditReminders
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>A context as the app configures it in production, of the version before Fase 26C.</summary>
    public static ApplicationDbContext CreateProductionLikeContext(
        SqlServerTestDatabase database, ICurrentTenantService tenant, IMediator mediator, params IInterceptor[] interceptors)
        => database.CreateProductionLikeContext(tenant, mediator, (options, current, sender) => new Context(options, current, sender), interceptors);

    /// <summary>For the context of an earlier version, which does not know it either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Ignore(c => c.CreditTermDays);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.CreditRemindersEnabled);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.CreditReminderEveryDays);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.CreditReminderTemplateId);
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

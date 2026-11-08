using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 30B added (whether a member gets an email with each online order its customer cancels, and how
/// long before its limit the business is reminded of a request): the app as deployed before it, and what a test needs on a
/// database migrated to an earlier migration, where the columns do not exist yet.
/// </summary>
internal static class BeforeOrderCancellationEmails
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantUser>().Ignore(m => m.OrderCancellationEmails);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.AnswerReminderMinutes);
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

using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 29D added (the quotes sent from TienditApp: their emails, the quote a WhatsApp carries, and the
/// template of a business's quotes): the app as deployed before it, and what a test needs to seed or read on a database
/// migrated to an earlier migration, where the table and the columns do not exist yet.
/// </summary>
internal static class BeforeQuoteDeliveries
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<QuoteEmail>();
        // The relationship to the quote first: left alone, it would keep QuoteId as a shadow property.
        var messages = modelBuilder.Entity<OutgoingMessage>();
        foreach (var foreignKey in messages.Metadata.GetForeignKeys().Where(k => k.PrincipalEntityType.ClrType == typeof(Quote)).ToList())
            messages.Metadata.RemoveForeignKey(foreignKey);
        messages.Ignore(m => m.QuoteId);
        modelBuilder.Entity<TenantSettings>().Ignore(s => s.QuoteWhatsAppTemplateId);
        BeforeQuoteConversion.Ignore(modelBuilder);
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

using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// The model without what Fase 31L added (the photo or audio a customer sent by WhatsApp): the app as deployed before it, and
/// what a test needs on a database migrated to an earlier migration, where its columns do not exist yet.
/// </summary>
internal static class BeforeWhatsAppMessageMedia
{
    public static ApplicationDbContext CreateContext(SqlServerTestDatabase database, Guid? tenantId = null)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new Context(options, tenant, mediator));

    /// <summary>For the context of an earlier version, which does not know them either.</summary>
    public static void Ignore(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WhatsAppMessage>(message =>
        {
            message.Ignore(m => m.MediaKind);
            message.Ignore(m => m.MediaId);
            message.Ignore(m => m.MediaPath);
            message.Ignore(m => m.MediaState);
        });
        BeforeQuoteDeposits.Ignore(modelBuilder);
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

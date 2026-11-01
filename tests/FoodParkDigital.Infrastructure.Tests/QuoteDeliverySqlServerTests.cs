using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The sends of a quote on SQL Server (Fase 29D, ADR 0053 point 16), inside the transaction of the pipeline: «Mandar por
/// correo» pressed eight times at once queues five emails of the version and tells the rest the limit was reached, since
/// the business's lock of quote sends puts them in line.
/// </summary>
public class QuoteDeliverySqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task MandarPorCorreoAtOnce_ShouldQueueFiveEmailsOfTheVersion_AndTellTheRestTheLimitWasReached()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, quoteId) = await SeedAsync(database);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => SendAsync(database, tenantId, quoteId, $"cliente{i}@correo.mx")));

        Assert.Equal(QuoteRules.MaxSendsPerQuoteVersion, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(QuoteRules.TooManySendsMessage, r.Error));
        await using var check = database.CreateContext(tenantId);
        Assert.Equal(QuoteRules.MaxSendsPerQuoteVersion, await check.QuoteEmails.CountAsync());
    }

    /// <summary>One «Mandar por correo» in its own context, through the transaction of the pipeline, as production does.</summary>
    private static async Task<Result<QuoteEmailDto>> SendAsync(SqlServerTestDatabase database, Guid tenantId, Guid quoteId, string email)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        await using var context = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        var handler = new QuoteDeliveryHandler(
            context, tenant, new Owner(), new PhoneNormalizationService(), new QuoteSendingLock(context), new Links(), new Clock());
        var command = new SendQuoteByEmailCommand(quoteId, email);
        return await new TransactionBehavior<SendQuoteByEmailCommand, Result<QuoteEmailDto>>(context)
            .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
    }

    /// <summary>A trade with a quote sent today.</summary>
    private static async Task<(Guid TenantId, Guid QuoteId)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(1), new QuoteCustomer(null, "Valeria", "5522107765", null), null, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        // The business's day: Mexico City, six hours behind.
        quote.Send(DateOnly.FromDateTime(Now.AddHours(-6).DateTime), new string('q', 43), null, Now);
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, quote);
        await seed.SaveChangesAsync();
        return (tenant.Id, quote.Id);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Links : IAppLinks
    {
        public string? BaseUrl => "https://app.tienditapp.mx";
    }

    private sealed class Owner : ICurrentUserService
    {
        public string? UserId => Guid.NewGuid().ToString();
        public string? UserName => "dueño";
        public string? Role => "TenantAdmin";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }
}

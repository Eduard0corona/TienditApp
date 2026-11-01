using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The quotes in SQL Server (Fase 29C, ADR 0053): a version keeps its lines and totals, a correction replaces the sent one
/// and both stay; a folio has one draft at a time, two edits of one draft collide on its version, the folios start after the
/// highest the business has, and deleting a product or a customer of the list leaves the quote with what it says.
/// </summary>
public class QuoteSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);

    [SqlServerFact]
    public async Task AQuote_ShouldKeepItsLinesAndTotals_AndACorrectionReplacesTheSentOne()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, productId) = await SeedAsync(database);
        Guid firstId, secondId;
        await using (var context = database.CreateContext(tenantId))
        {
            var quote = await StartAsync(context, tenantId);
            quote.ReplaceLines([
                new QuoteLineInput(productId, "Cambio de calentador", 1m, 1160m, VatRate.General),
                new QuoteLineInput(null, "Tubo de cobre", 2.5m, 100m, VatRate.Exempt)], Now);
            quote.Send(Today, new string('a', 43), null, Now);
            context.Quotes.Add(quote);
            await context.SaveChangesAsync();
            firstId = quote.Id;
        }

        await using (var context = database.CreateContext(tenantId))
        {
            var sent = await context.Quotes.Include(q => q.Lines).SingleAsync();
            var revision = sent.Revise(2, Now.AddDays(1));
            revision.ReplaceLines([new QuoteLineInput(productId, "Cambio de calentador", 1m, 990m, VatRate.General)], Now.AddDays(1));
            revision.Send(Today.AddDays(1), new string('b', 43), sent, Now.AddDays(1));
            context.Quotes.Add(revision);
            await context.SaveChangesAsync();
            secondId = revision.Id;
        }

        await using var check = database.CreateContext(tenantId);
        var versions = await check.Quotes.Include(q => q.Lines).OrderBy(q => q.Revision).ToListAsync();
        Assert.Equal([(firstId, 1, QuoteStatus.Superseded), (secondId, 2, QuoteStatus.Sent)],
            versions.Select(q => (q.Id, q.Revision, q.Status)));
        // The replaced one keeps what it said: 1,160 with IVA inside plus 250 exempt.
        Assert.Equal((1250m, 160m, 1410m), (versions[0].SubTotal, versions[0].Tax, versions[0].Total));
        Assert.Equal([("Cambio de calentador", 1160m), ("Tubo de cobre", 250m)],
            versions[0].Lines.OrderBy(l => l.Position).Select(l => (l.Description, l.Total)));
        Assert.Equal((990m, new DateOnly(2026, 11, 18)), (versions[1].Total, versions[1].ValidUntil!.Value));
    }

    [SqlServerFact]
    public async Task AFolio_ShouldHaveOneDraftAtATime()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, _) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            var quote = await StartAsync(context, tenantId);
            quote.ReplaceLines([new QuoteLineInput(null, "Revisión", 1m, 500m, VatRate.General)], Now);
            quote.Send(Today, new string('a', 43), null, Now);
            context.Quotes.Add(quote);
            await context.SaveChangesAsync();
        }

        // Two people correct it at the same time: the second correction collides.
        await using var first = database.CreateContext(tenantId);
        await using var second = database.CreateContext(tenantId);
        first.Quotes.Add((await first.Quotes.Include(q => q.Lines).SingleAsync()).Revise(2, Now));
        second.Quotes.Add((await second.Quotes.Include(q => q.Lines).SingleAsync()).Revise(2, Now));
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task AVisit_ShouldHaveOneQuote_UnlessItsDraftWasDiscarded()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, _) = await SeedAsync(database);
        var visit = new Domain.Appointments.Entities.Appointment(tenantId, "Valeria", "5522107765", "Visita", Now, Now.AddHours(1));
        await using (var context = database.CreateContext(tenantId))
        {
            context.Appointments.Add(visit);
            await context.SaveChangesAsync();
        }

        Quote ForTheVisit(int number) => Quote.Start(tenantId, Folios.QuoteNumber(number), Customer(), visit.Id, true, 15, Now);

        // Two «Cotizar» at the same time: the second quote of the visit collides.
        await using (var first = database.CreateContext(tenantId))
        await using (var second = database.CreateContext(tenantId))
        {
            first.Quotes.Add(ForTheVisit(1));
            second.Quotes.Add(ForTheVisit(2));
            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        }

        // Once its draft is discarded, the visit can be quoted again.
        await using (var context = database.CreateContext(tenantId))
        {
            (await context.Quotes.SingleAsync()).Discard(Now);
            context.Quotes.Add(ForTheVisit(3));
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenantId);
        Assert.Equal([("COT-000001", QuoteStatus.Discarded), ("COT-000003", QuoteStatus.Draft)],
            await check.Quotes.OrderBy(q => q.Number).Select(q => new ValueTuple<string, QuoteStatus>(q.Number, q.Status)).ToListAsync());
    }

    [SqlServerFact]
    public async Task TwoEditsOfADraft_ShouldCollideOnItsVersion()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, _) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            context.Quotes.Add(await StartAsync(context, tenantId));
            await context.SaveChangesAsync();
        }

        await using var first = database.CreateContext(tenantId);
        await using var second = database.CreateContext(tenantId);
        var mine = await first.Quotes.Include(q => q.Lines).SingleAsync();
        var theirs = await second.Quotes.Include(q => q.Lines).SingleAsync();
        mine.ReplaceLines([new QuoteLineInput(null, "Revisión", 1m, 500m, VatRate.General)], Now);
        theirs.UpdateDetails(new QuoteCustomer(null, "Valeria", "+525522107765", null), 30, null, Now);
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task QuoteFolios_ShouldStartAfterTheHighestTheBusinessHas_AndCountWithoutEnd()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, _) = await SeedAsync(database);
        var (otherId, _) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenantId))
        {
            context.Quotes.Add(Quote.Start(tenantId, Folios.QuoteNumber(41), Customer(), null, true, 15, Now));
            await context.SaveChangesAsync();
        }

        await using var folios = database.CreateContext(tenantId);
        var generator = new FolioGenerator(folios);
        Assert.Equal(["COT-000042", "COT-000043"], new[]
        {
            await generator.NextQuoteNumberAsync(tenantId, CancellationToken.None),
            await generator.NextQuoteNumberAsync(tenantId, CancellationToken.None)
        });
        // Each business counts its own.
        Assert.Equal("COT-000001", await generator.NextQuoteNumberAsync(otherId, CancellationToken.None));
    }

    [SqlServerFact]
    public async Task DeletingAProductOrACustomer_ShouldLeaveTheQuoteWithWhatItSays()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, productId) = await SeedAsync(database);
        var customer = new Customer(tenantId, "Valeria Ruiz", "+525522107765", null);
        await using (var context = database.CreateContext(tenantId))
        {
            context.Customers.Add(customer);
            var quote = Quote.Start(tenantId, Folios.QuoteNumber(1),
                new QuoteCustomer(customer.Id, customer.Name, customer.Phone!, null), null, true, 15, Now);
            quote.ReplaceLines([new QuoteLineInput(productId, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
            quote.Send(Today, new string('a', 43), null, Now);
            context.Quotes.Add(quote);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenantId))
        {
            context.Products.Remove(await context.Products.SingleAsync(p => p.Id == productId));
            context.Customers.Remove(await context.Customers.SingleAsync(c => c.Id == customer.Id));
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenantId);
        var stored = await check.Quotes.Include(q => q.Lines).SingleAsync();
        Assert.Equal(((Guid?)null, "Valeria Ruiz", 1160m), (stored.CustomerId, stored.CustomerName, stored.Total));
        Assert.Equal(((Guid?)null, "Cambio de calentador"), (stored.Lines.Single().ProductId, stored.Lines.Single().Description));
    }

    private static QuoteCustomer Customer() => new(null, "Valeria Ruiz", "+525522107765", "valeria@correo.mx");

    private static async Task<Quote> StartAsync(ApplicationDbContext context, Guid tenantId)
        => Quote.Start(tenantId, await new FolioGenerator(context).NextQuoteNumberAsync(tenantId, CancellationToken.None),
            Customer(), null, true, 15, Now);

    /// <summary>A trade with a service in its catalog.</summary>
    private static async Task<(Guid TenantId, Guid ProductId)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var category = new Category(tenant.Id, "Servicios", null);
        var product = new Product(tenant.Id, category.Id, "Cambio de calentador", null, 1160m, "MXN");
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, category, product);
        await seed.SaveChangesAsync();
        return (tenant.Id, product.Id);
    }
}

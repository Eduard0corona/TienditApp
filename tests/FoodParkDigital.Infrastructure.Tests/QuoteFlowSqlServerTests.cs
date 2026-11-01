using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The quotes through their handlers on SQL Server (Fase 29C, ADR 0053), inside the transaction of the pipeline: «Cotizar»
/// pressed at once by several people starts one quote for the visit and gives back the folios of the others, «Corregir» at
/// once starts one correction, and a draft whose lines are replaced keeps only the new ones.
/// </summary>
public class QuoteFlowSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task CotizarAtOnce_ShouldStartOneQuoteForTheVisit_AndGiveBackTheOtherFolios()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, appointmentId) = await SeedAsync(database);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunAsync(database, tenantId,
            (context, tenant) => new CreateQuoteCommandHandler(context, tenant, new Owner(), new FolioGenerator(context), new Clock()),
            new CreateQuoteCommand(appointmentId, null, null, null, null))));

        // Each one either started it, opened the one already started, or was told someone else was starting it.
        var quoteIds = results.Where(r => r.IsSuccess).Select(r => r.Value.Id).Distinct().ToList();
        Assert.Single(quoteIds);
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(QuotesHandler.AlreadyQuotingMessage, r.Error));
        await using var check = database.CreateContext(tenantId);
        Assert.Equal("COT-000001", (await check.Quotes.SingleAsync()).Number);
        // The refused ones gave their folio back: the next quote follows the one that stayed.
        Assert.Equal("COT-000002", await new FolioGenerator(check).NextQuoteNumberAsync(tenantId, CancellationToken.None));
    }

    [SqlServerFact]
    public async Task CorregirAtOnce_ShouldStartOneCorrection_AndTellTheOthersSomeoneIsCorrectingIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, appointmentId) = await SeedAsync(database);
        var created = await RunAsync(database, tenantId,
            (context, tenant) => new CreateQuoteCommandHandler(context, tenant, new Owner(), new FolioGenerator(context), new Clock()),
            new CreateQuoteCommand(appointmentId, null, null, null, null));
        await UpdateAsync(database, tenantId, created.Value.Id, [Line("Revisión", 500m)]);
        var sent = await RunAsync(database, tenantId,
            (context, tenant) => new QuotesHandler(context, tenant, new Owner(), new Clock()), new SendQuoteCommand(created.Value.Id));
        Assert.True(sent.IsSuccess, sent.Error);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunAsync(database, tenantId,
            (context, tenant) => new QuotesHandler(context, tenant, new Owner(), new Clock()), new ReviseQuoteCommand(sent.Value.Id))));

        // Each one either started it, opened the one already started, or was told someone else was starting it.
        Assert.Single(results.Where(r => r.IsSuccess).Select(r => r.Value.Id).Distinct());
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(QuotesHandler.AlreadyRevisingMessage, r.Error));
        await using var check = database.CreateContext(tenantId);
        Assert.Equal([(1, QuoteStatus.Sent), (2, QuoteStatus.Draft)],
            await check.Quotes.OrderBy(q => q.Revision).Select(q => new ValueTuple<int, QuoteStatus>(q.Revision, q.Status)).ToListAsync());
    }

    [SqlServerFact]
    public async Task ReplacingTheLinesOfADraft_ShouldKeepOnlyTheNewOnes()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, appointmentId) = await SeedAsync(database);
        var created = await RunAsync(database, tenantId,
            (context, tenant) => new CreateQuoteCommandHandler(context, tenant, new Owner(), new FolioGenerator(context), new Clock()),
            new CreateQuoteCommand(appointmentId, null, null, null, null));
        Assert.True(created.IsSuccess, created.Error);

        await UpdateAsync(database, tenantId, created.Value.Id, [Line("Revisión", 500m), Line("Mano de obra", 300m)]);
        var replaced = await UpdateAsync(database, tenantId, created.Value.Id,
            [Line("Cambio de calentador", 2800m), Line("Tubería", 180m, 3m), Line("Mano de obra", 700m)]);

        Assert.True(replaced.IsSuccess, replaced.Error);
        await using var check = database.CreateContext(tenantId);
        var lines = await check.Quotes.SelectMany(q => q.Lines).OrderBy(l => l.Position).ToListAsync();
        Assert.Equal([(1, "Cambio de calentador", 2800m), (2, "Tubería", 540m), (3, "Mano de obra", 700m)],
            lines.Select(l => (l.Position, l.Description, l.Total)));
        // Prices without IVA (the business's policy by default): 4,040 plus 16 %.
        Assert.Equal((4040m, 4686.40m), ((await check.Quotes.SingleAsync()).SubTotal, (await check.Quotes.SingleAsync()).Total));
    }

    private static QuoteLineRequest Line(string description, decimal unitPrice, decimal quantity = 1m)
        => new() { Description = description, Quantity = quantity, UnitPrice = unitPrice };

    private static Task<Result<QuoteDto>> UpdateAsync(SqlServerTestDatabase database, Guid tenantId, Guid quoteId, List<QuoteLineRequest> lines)
        => RunAsync(database, tenantId,
            (context, tenant) => new QuotesHandler(context, tenant, new Owner(), new Clock()),
            new UpdateQuoteDraftCommand(quoteId, null, "Valeria Ruiz", "5522107765", null, 15, null, lines));

    /// <summary>Runs a request in its own context, as production does: through the transaction of the pipeline when it has one.</summary>
    private static async Task<Result<QuoteDto>> RunAsync<TRequest>(
        SqlServerTestDatabase database,
        Guid tenantId,
        Func<ApplicationDbContext, ICurrentTenantService, MediatR.IRequestHandler<TRequest, Result<QuoteDto>>> handlerFor,
        TRequest command)
        where TRequest : MediatR.IRequest<Result<QuoteDto>>
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        await using var context = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        var handler = handlerFor(context, tenant);
        return command switch
        {
            CreateQuoteCommand create => await new TransactionBehavior<CreateQuoteCommand, Result<QuoteDto>>(context)
                .Handle(create, () => handler.Handle(command, CancellationToken.None), CancellationToken.None),
            SendQuoteCommand send => await new TransactionBehavior<SendQuoteCommand, Result<QuoteDto>>(context)
                .Handle(send, () => handler.Handle(command, CancellationToken.None), CancellationToken.None),
            ReviseQuoteCommand revise => await new TransactionBehavior<ReviseQuoteCommand, Result<QuoteDto>>(context)
                .Handle(revise, () => handler.Handle(command, CancellationToken.None), CancellationToken.None),
            _ => await handler.Handle(command, CancellationToken.None)
        };
    }

    /// <summary>A trade with a visit its team scheduled at 72000.</summary>
    private static async Task<(Guid TenantId, Guid AppointmentId)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var zone = new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 50m, 0m, null, ["72000"]));
        var visit = new Appointment(tenant.Id, "Valeria Ruiz", "5522107765", "Visita y diagnóstico", Now, Now.AddHours(1));
        visit.ArrangeVisit(DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null), zone, Now);
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, zone, visit);
        await seed.SaveChangesAsync();
        return (tenant.Id, visit.Id);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
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

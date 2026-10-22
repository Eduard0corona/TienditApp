using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The CFDI de retenciones of the platforms (Fase 22J): the table keeps what its XML says with the cents, its checks keep
/// every row one the domain could write, one CFDI counts once in a business (also when two people upload it at once, and a
/// removed one can come back), another business never sees it, and two removals at once leave one. When someone else's save
/// lands between what registering or removing read and what it writes, the use case answers 409 and saves nothing.
/// </summary>
public class PlatformWithholdingCertificatesSqlServerTests
{
    private static readonly DateOnly CurrentMonth = new(2026, 10, 1);
    private static readonly Guid Uuid = Guid.Parse("7874d580-da5b-558f-b8ec-d92c57197242");

    [SqlServerFact]
    public async Task Table_ShouldHaveItsShape()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        Assert.Equal(
            new[]
            {
                "IsrBase decimal 18 2 1", "IsrWithheld decimal 18 2 0", "OperationTotal decimal 18 2 0",
                "TotalWithheld decimal 18 2 0", "VatBase decimal 18 2 1", "VatWithheld decimal 18 2 0"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT c.[name] + N' ' + t.[name] + N' ' + CAST(c.[precision] AS nvarchar(3)) + N' ' + CAST(c.[scale] AS nvarchar(3))
                    + N' ' + CAST(c.[is_nullable] AS nvarchar(1)) AS [Value]
                FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
                WHERE c.[object_id] = OBJECT_ID(N'dbo.PlatformWithholdingCertificates') AND t.[name] = N'decimal'
                ORDER BY [Value]
                """).ToListAsync());
        Assert.Equal(
            new[] { "IssuerName nvarchar 600", "IssuerRfc varchar 13", "ReceiverRfc varchar 13", "RemovalReason nvarchar 1000" },
            await context.Database.SqlQuery<string>($"""
                SELECT c.[name] + N' ' + t.[name] + N' ' + CAST(c.[max_length] AS nvarchar(5)) AS [Value]
                FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
                WHERE c.[object_id] = OBJECT_ID(N'dbo.PlatformWithholdingCertificates') AND t.[name] IN (N'varchar', N'nvarchar')
                    AND c.[name] NOT IN (N'CreatedBy', N'UpdatedBy')
                ORDER BY [Value]
                """).ToListAsync());
        Assert.Equal(1, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.columns c INNER JOIN sys.types t ON t.[user_type_id] = c.[user_type_id]
            WHERE c.[object_id] = OBJECT_ID(N'dbo.PlatformWithholdingCertificates') AND c.[name] = N'IssuedOn'
                AND t.[name] = N'datetime2' AND c.[scale] = 0 AND c.[is_nullable] = 0
            """));
        Assert.Equal(
            new[]
            {
                "CK_PlatformWithholdingCertificates_Amounts", "CK_PlatformWithholdingCertificates_Channel",
                "CK_PlatformWithholdingCertificates_Period", "CK_PlatformWithholdingCertificates_Removed"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] AS [Value] FROM sys.check_constraints
                WHERE [parent_object_id] = OBJECT_ID(N'dbo.PlatformWithholdingCertificates')
                ORDER BY [name]
                """).ToListAsync());
        Assert.Equal(
            new[]
            {
                "IX_PlatformWithholdingCertificates_TenantId_Channel_Year_MonthTo 0 ",
                "IX_PlatformWithholdingCertificates_TenantId_Uuid_Active 1 ([RemovedAt] IS NULL)"
            },
            await context.Database.SqlQuery<string>($"""
                SELECT [name] + N' ' + CAST([is_unique] AS nvarchar(1)) + N' ' + ISNULL([filter_definition], N'') AS [Value]
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.PlatformWithholdingCertificates') AND [name] LIKE N'IX[_]%'
                ORDER BY [name]
                """).ToListAsync());
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuseRowsTheDomainCannotWrite()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        await using var context = database.CreateContext(tenant.Id);

        await RefusedAsync("CK_PlatformWithholdingCertificates_Channel", () => InsertAsync(context, tenant.Id, channel: 2));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Channel", () => InsertAsync(context, tenant.Id, channel: 6));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Period", () => InsertAsync(context, tenant.Id, monthFrom: 0));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Period", () => InsertAsync(context, tenant.Id, monthTo: 13));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Period", () => InsertAsync(context, tenant.Id, monthFrom: 10, monthTo: 9));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Period", () => InsertAsync(context, tenant.Id, year: 2018));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Amounts", () => InsertAsync(context, tenant.Id, operationTotal: -1m));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Amounts", () => InsertAsync(context, tenant.Id, isrBase: -1m));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Amounts", () => InsertAsync(context, tenant.Id, totalWithheld: 1708.29m));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Removed",
            () => InsertAsync(context, tenant.Id, removedAt: DateTimeOffset.UtcNow));
        await RefusedAsync("CK_PlatformWithholdingCertificates_Removed", () => InsertAsync(context, tenant.Id, removalReason: "Otro"));

        // What the domain writes goes in: without the bases, two cents short of rounding, and one removed alongside.
        await InsertAsync(context, tenant.Id, isrBase: null, vatBase: null);
        await InsertAsync(context, tenant.Id, totalWithheld: 1708.30m);
        await InsertAsync(context, tenant.Id, removedAt: DateTimeOffset.UtcNow, removalReason: "Era de otra sucursal");
    }

    [SqlServerFact]
    public async Task Certificate_ShouldBeStoredAsItSays_CountOnceInItsBusiness_AndComeBackOnceRemoved()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        Guid certificateId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var certificate = new PlatformWithholdingCertificate(tenant.Id, OrderChannel.UberEats, Data(), CurrentMonth);
            context.PlatformWithholdingCertificates.Add(certificate);
            await context.SaveChangesAsync();
            certificateId = certificate.Id;
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var stored = await context.PlatformWithholdingCertificates.AsNoTracking().SingleAsync();
            Assert.Equal((certificateId, Uuid, OrderChannel.UberEats), (stored.Id, stored.Uuid, stored.Channel));
            Assert.Equal(("UBV121024TN8", "UBER B.V.", "CLA150101AB1"), (stored.IssuerRfc, stored.IssuerName, stored.ReceiverRfc));
            Assert.Equal((2026, 9, 9, new DateTime(2026, 10, 2, 22, 28, 50)), (stored.Year, stored.MonthFrom, stored.MonthTo, stored.IssuedOn));
            Assert.Equal(
                (16269.76m, (decimal?)16269.76m, 406.74m, (decimal?)2603.16m, 1301.58m, 1708.32m),
                (stored.OperationTotal, stored.IsrBase, stored.IsrWithheld, stored.VatBase, stored.VatWithheld, stored.TotalWithheld));

            // The same CFDI again in the same business: the database keeps one.
            context.PlatformWithholdingCertificates.Add(new PlatformWithholdingCertificate(tenant.Id, OrderChannel.UberEats, Data(), CurrentMonth));
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            var sql = Assert.IsType<SqlException>(duplicate.InnerException);
            Assert.Equal(2601, sql.Number);
            Assert.Contains("IX_PlatformWithholdingCertificates_TenantId_Uuid_Active", sql.Message);
        }

        // Another business sees none, and its own with the same UUID is another row.
        await using (var context = database.CreateContext(other.Id))
        {
            Assert.Empty(await context.PlatformWithholdingCertificates.ToListAsync());
            context.PlatformWithholdingCertificates.Add(new PlatformWithholdingCertificate(other.Id, OrderChannel.UberEats, Data(), CurrentMonth));
            await context.SaveChangesAsync();
        }

        // Removed, it can be uploaded again; the removed one stays, with its reason.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var certificate = await context.PlatformWithholdingCertificates.SingleAsync();
            certificate.Remove("Lo subí con la plataforma equivocada", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
            context.PlatformWithholdingCertificates.Add(new PlatformWithholdingCertificate(tenant.Id, OrderChannel.DidiFood, Data(), CurrentMonth));
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(
                new[] { (OrderChannel.UberEats, true), (OrderChannel.DidiFood, false) },
                (await context.PlatformWithholdingCertificates.AsNoTracking().OrderBy(c => c.Channel).ToListAsync())
                    .Select(c => (c.Channel, c.IsRemoved)));
        }
    }

    [SqlServerFact]
    public async Task RemovalsAtTheSameTime_ShouldKeepTheFirst_AndRefuseTheSecond()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.PlatformWithholdingCertificates.Add(new PlatformWithholdingCertificate(tenant.Id, OrderChannel.Rappi, Data(), CurrentMonth));
            await context.SaveChangesAsync();
        }

        await using (var first = database.CreateContext(tenant.Id))
        await using (var second = database.CreateContext(tenant.Id))
        {
            var mine = await first.PlatformWithholdingCertificates.SingleAsync();
            var theirs = await second.PlatformWithholdingCertificates.SingleAsync();
            mine.Remove("Duplicado", DateTimeOffset.UtcNow);
            theirs.Remove("Era de otra sucursal", DateTimeOffset.UtcNow);

            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal("Duplicado", (await context.PlatformWithholdingCertificates.AsNoTracking().SingleAsync()).RemovalReason);
        }
    }

    [SqlServerFact]
    public async Task Reconciliation_ShouldReadOnSqlServer_ByMonthOfTheBusiness_TheOrdersThePlatformCharged()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            var uber = new DeliveryPlatformSetting(tenant.Id, OrderChannel.UberEats, isActive: true, PlatformTerms.WithFiscalDefaults(30m));
            seed.DeliveryPlatformSettings.Add(uber);
            // In Mexico City: the last night of 2025 is out of 2026; the first minutes of 2026 are January's.
            NewPlatformOrder(seed, tenant.Id, uber, "ORD-000001", "U1", Local(2025, 12, 31, 23, 30));
            NewPlatformOrder(seed, tenant.Id, uber, "ORD-000002", "U2", Local(2026, 1, 1, 0, 30));
            NewPlatformOrder(seed, tenant.Id, uber, "ORD-000003", "U3", Local(2026, 8, 31, 23, 30));
            NewPlatformOrder(seed, tenant.Id, uber, "ORD-000004", "U4", Local(2026, 9, 10, 12, 0));
            // Charged by the business: nothing withheld.
            NewPlatformOrder(seed, tenant.Id, uber, "ORD-000005", "U5", Local(2026, 9, 11, 12, 0), PlatformCollectedBy.Business);
            // The platform gave back half of one and the whole of another: half of the first counts, nothing of the second.
            NewPlatformOrder(seed, tenant.Id, uber, "ORD-000006", "U6", Local(2026, 9, 12, 12, 0)).RegisterRefund(
                500m, OrderPaymentMethod.Platform, "La plataforma devolvió la mitad", Local(2026, 9, 13, 12, 0), "duena", null, null, false, []);
            NewPlatformOrder(seed, tenant.Id, uber, "ORD-000007", "U7", Local(2026, 9, 14, 12, 0)).RegisterRefund(
                1000m, OrderPaymentMethod.Platform, "La plataforma lo canceló", Local(2026, 9, 15, 12, 0), "duena", null, null, false, []);
            // August to September in one CFDI, and one of July removed.
            seed.PlatformWithholdingCertificates.Add(new PlatformWithholdingCertificate(
                tenant.Id, OrderChannel.UberEats, Data() with { MonthFrom = 8, MonthTo = 9 }, CurrentMonth));
            var removed = new PlatformWithholdingCertificate(
                tenant.Id, OrderChannel.UberEats, Data() with { Uuid = Guid.NewGuid(), MonthFrom = 7, MonthTo = 7 }, CurrentMonth);
            removed.Remove("Era de Rappi", DateTimeOffset.UtcNow);
            seed.PlatformWithholdingCertificates.Add(removed);
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext(tenant.Id);
        var tenantService = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenantService.SetTenant(tenant.Id);
        var report = await new GetPlatformWithholdingsQueryHandler(context, tenantService)
            .Handle(new GetPlatformWithholdingsQuery { Channel = OrderChannel.UberEats, Year = 2026 }, default);

        // $1,000 with IVA included: $21.55 of ISR and $68.97 of IVA withheld each; half of one, $10.78 and $34.48 (to even).
        Assert.Equal(
            [(1, 1, 21.55m, 68.97m), (8, 1, 21.55m, 68.97m), (9, 2, 32.33m, 103.45m)],
            report.Months.Where(m => m.OrderCount > 0).Select(m => (m.Month!.Value, m.OrderCount, m.CalculatedIsr, m.CalculatedVat)));
        var september = report.Months[8];
        // The CFDI counts in September and is compared with August and September; August says where it is compared.
        Assert.Equal(
            (1, (decimal?)406.74m, (decimal?)(406.74m - 53.88m), (decimal?)(1301.58m - 172.42m), (int?)8),
            (september.CertificateCount, september.CertificateIsr, september.IsrDifference, september.VatDifference, september.CoveredFromMonth));
        Assert.Equal((0, (int?)9), (report.Months[7].CertificateCount, report.Months[7].ComparedInMonth));
        Assert.Equal((0, (int?)null), (report.Months[6].CertificateCount, report.Months[6].ComparedInMonth));
        // The year: four orders calculated, and the difference only over August and September (January has no CFDI).
        Assert.Equal((4, 75.43m, september.IsrDifference), (report.Total.OrderCount, report.Total.CalculatedIsr, report.Total.IsrDifference));
        Assert.Equal([(7, true), (8, false)], report.Certificates.Select(c => (c.MonthFrom, c.RemovedAt is not null)));
        Assert.Equal((2.5m, 50m), (report.IsrWithholdingPercent!.Value, report.VatWithholdingPercent!.Value));
    }

    [SqlServerFact]
    public async Task RegisteringTheSameCfdiAtTheSameTime_ShouldSayWhereTheOtherIs_AndUndoThisOne()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        // Someone registers the same CFDI after this registration read that it was not there, and before it writes.
        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenant.Id);
                other.PlatformWithholdingCertificates.Add(new PlatformWithholdingCertificate(tenant.Id, OrderChannel.DidiFood, Data(), CurrentMonth));
                await other.SaveChangesAsync();
            }));

        var result = await Register(racing, tenant.Id).Handle(
            new RegisterPlatformWithholdingCertificateCommand(OrderChannel.UberEats, Xml(Uuid)), CancellationToken.None);

        // The unique index keeps the other one; this one is undone whole, its activity entry included, and is not a 500.
        Assert.Equal("Este CFDI ya está registrado en DiDi Food, septiembre de 2026.", result.Error);
        Assert.True(PlatformWithholdingErrors.IsConflict(result.Error));
        await using var context = database.CreateContext(tenant.Id);
        Assert.Equal(OrderChannel.DidiFood, Assert.Single(await context.PlatformWithholdingCertificates.ToListAsync()).Channel);
        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task RegisteringAReplacement_WhileTheOneItReplacesIsRemoved_ShouldBeStale_AndSaveNothing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.PlatformWithholdingCertificates.Add(new PlatformWithholdingCertificate(tenant.Id, OrderChannel.UberEats, Data(), CurrentMonth));
            await seed.SaveChangesAsync();
        }

        // Someone removes the original after this registration read it, and before it writes.
        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenant.Id);
                (await other.PlatformWithholdingCertificates.SingleAsync()).Remove("Era de Rappi", DateTimeOffset.UtcNow);
                await other.SaveChangesAsync();
            }));
        var replacement = Guid.Parse("22222222-2222-4333-8444-555555555555");

        var result = await Register(racing, tenant.Id).Handle(
            new RegisterPlatformWithholdingCertificateCommand(OrderChannel.UberEats, Xml(replacement, replaces: Uuid)), CancellationToken.None);

        Assert.Equal(PlatformWithholdingErrors.Stale, result.Error);
        await using var context = database.CreateContext(tenant.Id);
        // Theirs stands, with their reason; the replacement and its activity entries went with the refused save.
        var stored = Assert.Single(await context.PlatformWithholdingCertificates.ToListAsync());
        Assert.Equal((Uuid, "Era de Rappi"), (stored.Uuid, stored.RemovalReason));
        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task Removing_WhileSomeoneElseRemovesIt_ShouldBeStale_AndKeepTheirReason()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var certificate = new PlatformWithholdingCertificate(tenant.Id, OrderChannel.UberEats, Data(), CurrentMonth);
        await using (var seed = database.CreateContext(tenant.Id))
        {
            seed.PlatformWithholdingCertificates.Add(certificate);
            await seed.SaveChangesAsync();
        }

        await using var racing = database.CreateProductionLikeContext(Tenant(tenant.Id), new InMemoryApplicationDbContext.NullMediator(),
            new SavedMeanwhile(async () =>
            {
                await using var other = database.CreateContext(tenant.Id);
                (await other.PlatformWithholdingCertificates.SingleAsync()).Remove("Era de Rappi", DateTimeOffset.UtcNow);
                await other.SaveChangesAsync();
            }));

        var result = await new RemovePlatformWithholdingCertificateCommandHandler(racing, Tenant(tenant.Id), new Owner(), new Clock())
            .Handle(new RemovePlatformWithholdingCertificateCommand(certificate.Id, "Duplicado"), CancellationToken.None);

        Assert.Equal(PlatformWithholdingErrors.Stale, result.Error);
        await using var context = database.CreateContext(tenant.Id);
        Assert.Equal("Era de Rappi", (await context.PlatformWithholdingCertificates.SingleAsync()).RemovalReason);
        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    private static RegisterPlatformWithholdingCertificateCommandHandler Register(ApplicationDbContext context, Guid tenantId)
        => new(context, Tenant(tenantId), new Owner(), new Clock());

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    /// <summary>The XML of <see cref="Data"/>, with its folio and the one it replaces.</summary>
    private static string Xml(Guid uuid, Guid? replaces = null)
    {
        var related = replaces is { } replaced ? $"""<retenciones:CfdiRetenRelacionados TipoRelacion="04" UUID="{replaced}"/>""" : "";
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <retenciones:Retenciones xmlns:retenciones="http://www.sat.gob.mx/esquemas/retencionpago/2" Version="2.0" FechaExp="2026-10-02T22:28:50" CveRetenc="26" LugarExpRetenc="03810">
              {related}
              <retenciones:Emisor RfcE="UBV121024TN8" NomDenRazSocE="UBER B.V." RegimenFiscalE="601"/>
              <retenciones:Receptor NacionalidadR="Nacional"><retenciones:Nacional RfcR="CLA150101AB1"/></retenciones:Receptor>
              <retenciones:Periodo MesIni="09" MesFin="09" Ejercicio="2026"/>
              <retenciones:Totales MontoTotOperacion="16269.76" MontoTotGrav="16269.76" MontoTotExent="0.00" MontoTotRet="1708.32">
                <retenciones:ImpRetenidos BaseRet="16269.76" ImpuestoRet="001" MontoRet="406.74" TipoPagoRet="03"/>
                <retenciones:ImpRetenidos BaseRet="2603.16" ImpuestoRet="002" MontoRet="1301.58" TipoPagoRet="01"/>
              </retenciones:Totales>
              <retenciones:Complemento>
                <tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="1.1" UUID="{uuid}" FechaTimbrado="2026-10-02T22:28:50"/>
              </retenciones:Complemento>
            </retenciones:Retenciones>
            """;
    }

    private sealed class Owner : ICurrentUserService
    {
        public string? UserId => "4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10";
        public string? UserName => "duena@demo.com";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);
    }

    private static readonly TimeZoneInfo Mexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
        => new(TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0), Mexico), TimeSpan.Zero);

    /// <summary>A paquete of $1,000 with IVA included sold through Uber Eats at a time; added to the context, not saved.</summary>
    private static Order NewPlatformOrder(
        ApplicationDbContext context, Guid tenantId, DeliveryPlatformSetting setting, string number, string folio, DateTimeOffset at,
        PlatformCollectedBy collectedBy = PlatformCollectedBy.Platform)
    {
        var order = new Order(
            tenantId, null, null, true, number, BusinessType.Food, OrderType.Retail, "Juan", null, null, null,
            pricesIncludeTax: true, channel: OrderChannel.UberEats);
        order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
        order.RegisterPlatformOrder(setting, PlatformOrderNumber.From(folio), collectedBy, at, null);
        order.MarkAsCreated();
        context.Orders.Add(order);
        return order;
    }

    internal static PlatformWithholdingCertificateData Data() => new(
        Uuid: Uuid,
        IssuerRfc: "UBV121024TN8",
        IssuerName: "UBER B.V.",
        ReceiverRfc: "CLA150101AB1",
        Year: 2026,
        MonthFrom: 9,
        MonthTo: 9,
        IssuedOn: new DateTime(2026, 10, 2, 22, 28, 50),
        OperationTotal: 16269.76m,
        IsrBase: 16269.76m,
        IsrWithheld: 406.74m,
        VatBase: 2603.16m,
        VatWithheld: 1301.58m,
        TotalWithheld: 1708.32m);

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        var other = new Tenant("Taquería", slug + "b", BusinessType.Food, $"{slug}b@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    // Rows written straight to the table, past the app's own checks.
    private static Task<int> InsertAsync(
        ApplicationDbContext context, Guid tenantId, int channel = 4, int year = 2026, int monthFrom = 9, int monthTo = 9,
        decimal operationTotal = 16269.76m, decimal? isrBase = 16269.76m, decimal? vatBase = 2603.16m, decimal totalWithheld = 1708.32m,
        DateTimeOffset? removedAt = null, string? removalReason = null)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PlatformWithholdingCertificates (Id, TenantId, Channel, Uuid, IssuerRfc, IssuerName, ReceiverRfc, Year, MonthFrom,
                MonthTo, IssuedOn, OperationTotal, IsrBase, IsrWithheld, VatBase, VatWithheld, TotalWithheld, RemovedAt, RemovalReason, CreatedAt)
            VALUES ({Guid.NewGuid()}, {tenantId}, {channel}, {Guid.NewGuid()}, 'UBV121024TN8', N'UBER B.V.', 'CLA150101AB1', {year}, {monthFrom},
                {monthTo}, '2026-10-02T22:28:50', {operationTotal}, {isrBase}, 406.74, {vatBase}, 1301.58, {totalWithheld}, {removedAt},
                {removalReason}, {DateTimeOffset.UtcNow})
            """);

    private static async Task RefusedAsync(string constraint, Func<Task> insert)
    {
        var error = await Assert.ThrowsAsync<SqlException>(insert);
        Assert.Equal(547, error.Number);
        Assert.Contains(constraint, error.Message);
    }

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}

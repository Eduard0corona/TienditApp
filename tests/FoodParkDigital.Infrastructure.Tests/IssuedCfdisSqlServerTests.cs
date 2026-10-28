using System.IO.Compression;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Reportes › CFDI emitidos on SQL Server (Fase 28H, ADR 0052): the month's invoices of orders, global invoices, payment
/// complements and credit notes as one UNION ALL, each table with its business filter, in the business's time zone; and the
/// ZIP with their XML and the CSV. The set operation runs as SQL Server translates it, which SQLite does not show.
/// </summary>
public class IssuedCfdisSqlServerTests
{
    private const string IssuerRfc = "EKU9003173C9";
    private const string ReceiverRfc = "XIQB891116QE4";

    // Mexico City is six hours behind UTC all year: October starts there at 06:00 UTC on the 1st.
    private static DateTimeOffset October(int day, int hour = 12) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task List_ShouldJoinTheMonthsCfdiOfTheFourKinds_NewestFirst_WithTheirReceiverOrderAndRelation_OnlyOfTheBusiness()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var book = await SeedAsync(database);
        await using var context = database.CreateContext(book.TenantId);

        var all = await ListAsync(context, book.TenantId);

        // The September one and the other business's are not in it; the refused one of the 31st at night is.
        Assert.Equal(9, all.TotalCount);
        Assert.Equal(
            [
                (IssuedCfdiKind.OrderInvoice, "F-5", OrderInvoiceStatus.Refused),
                (IssuedCfdiKind.OrderInvoice, "F-7", OrderInvoiceStatus.Stamping),
                (IssuedCfdiKind.OrderInvoice, "F-6", OrderInvoiceStatus.Cancelling),
                (IssuedCfdiKind.GlobalInvoice, "F-4", OrderInvoiceStatus.Stamped),
                (IssuedCfdiKind.PaymentComplement, "P-1", OrderInvoiceStatus.Stamped),
                (IssuedCfdiKind.OrderInvoice, "F-3", OrderInvoiceStatus.Stamped),
                (IssuedCfdiKind.CreditNote, "NC-1", OrderInvoiceStatus.Stamped),
                (IssuedCfdiKind.OrderInvoice, "F-2", OrderInvoiceStatus.Stamped),
                (IssuedCfdiKind.OrderInvoice, "F-1", OrderInvoiceStatus.Cancelled)
            ],
            all.Items.Select(c => (c.Kind, c.Number, c.Status)));

        var replaced = all.Items.Single(c => c.Number == "F-1");
        Assert.Equal((book.OrderNumber, "F-2", (string?)null, CfdiCancellationMotive.ErrorsWithReplacement, true),
            (replaced.OrderNumber, replaced.ReplacedByNumber, replaced.RelatedNumber, replaced.CancellationMotive, replaced.HasXml));
        Assert.Equal(("F-1", (string?)null), (all.Items.Single(c => c.Number == "F-2").RelatedNumber, all.Items.Single(c => c.Number == "F-2").ReplacedByNumber));
        var complement = all.Items.Single(c => c.Kind == IssuedCfdiKind.PaymentComplement);
        Assert.Equal((ReceiverRfc, "F-3", (decimal?)null, 120m, "01", (string?)null),
            (complement.ReceiverRfc, complement.RelatedNumber, complement.Total, complement.PaidAmount, complement.PaymentForm, complement.PaymentMethod));
        var note = all.Items.Single(c => c.Kind == IssuedCfdiKind.CreditNote);
        Assert.Equal(("F-2", book.OrderNumber, 17.24m, 2.76m, 20m, "PUE"),
            (note.RelatedNumber, note.OrderNumber, note.SubTotal, note.TaxTotal, note.Total, note.PaymentMethod));
        var cancelling = all.Items.Single(c => c.Number == "F-6");
        Assert.Equal((CfdiCancellationMotive.ErrorsWithoutReplacement, (DateTimeOffset?)null, true),
            (cancelling.CancellationMotive, cancelling.CancelledAt, cancelling.HasXml));
        var global = all.Items.Single(c => c.Kind == IssuedCfdiKind.GlobalInvoice);
        Assert.Equal((SatCatalog.GeneralPublicRfc, SatCatalog.GeneralPublicName, (Guid?)null, GlobalInvoicePeriodicity.Daily,
                new DateOnly(2026, 10, 8), 1, "PUE"),
            (global.ReceiverRfc, global.ReceiverName, global.OrderId, global.Periodicity, global.PeriodFrom, global.OrderCount, global.PaymentMethod));
        var refused = all.Items[0];
        Assert.Equal(("El RFC del receptor no existe.", false, (string?)null), (refused.FailureMessage, refused.HasXml, refused.Uuid));

        // A page at a time, the same order.
        var second = await ListAsync(context, book.TenantId, pageNumber: 2, pageSize: 3);
        Assert.Equal(["F-4", "P-1", "F-3"], second.Items.Select(c => c.Number));
        Assert.Equal(9, second.TotalCount);

        // By kind and by state.
        Assert.Equal(["NC-1"], (await ListAsync(context, book.TenantId, kind: IssuedCfdiKind.CreditNote)).Items.Select(c => c.Number));
        Assert.Equal(["F-1"], (await ListAsync(context, book.TenantId, status: OrderInvoiceStatus.Cancelled)).Items.Select(c => c.Number));
        Assert.Equal(["F-6"], (await ListAsync(context, book.TenantId, status: OrderInvoiceStatus.Cancelling)).Items.Select(c => c.Number));
        Assert.Equal(["F-7"], (await ListAsync(context, book.TenantId, status: OrderInvoiceStatus.Stamping)).Items.Select(c => c.Number));
        Assert.Empty((await ListAsync(context, book.TenantId, kind: IssuedCfdiKind.GlobalInvoice, status: OrderInvoiceStatus.Refused)).Items);

        // By folio (of any series without one), UUID, RFC, part of the name, the order's folio or a sale of the global invoice.
        Assert.Equal(["F-2"], await NumbersAsync(context, book.TenantId, "f-2"));
        Assert.Equal(["P-1", "NC-1", "F-1"], await NumbersAsync(context, book.TenantId, "1"));
        Assert.Equal(["P-1"], await NumbersAsync(context, book.TenantId, book.ComplementUuid.ToLowerInvariant()));
        Assert.Equal(["F-4"], await NumbersAsync(context, book.TenantId, "en general"));
        Assert.Equal(["F-5", "F-7", "F-6", "P-1", "F-3", "NC-1", "F-2", "F-1"], await NumbersAsync(context, book.TenantId, ReceiverRfc.ToLowerInvariant()));
        Assert.Equal(["NC-1", "F-2", "F-1"], await NumbersAsync(context, book.TenantId, book.OrderNumber));
        Assert.Equal(["F-4"], await NumbersAsync(context, book.TenantId, "ord-000003"));
        Assert.Equal(["F-5", "F-7", "F-6", "P-1", "F-3", "NC-1", "F-2", "F-1"], await NumbersAsync(context, book.TenantId, "fuente"));
    }

    [SqlServerFact]
    public async Task Summary_ShouldAddUpTheOnesInForceByKind_AndCountTheRest()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var book = await SeedAsync(database);
        await using var context = database.CreateContext(book.TenantId);

        var summary = await new GetIssuedCfdiSummaryQueryHandler(context, Tenant(book.TenantId))
            .Handle(new GetIssuedCfdiSummaryQuery(2026, 10), CancellationToken.None);

        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (summary.From, summary.To));
        Assert.Equal(
            [
                // F-2, F-3 and F-6, whose cancellation waits: the SAT has it in force until then.
                (IssuedCfdiKind.OrderInvoice, 3, 517.23m, 82.77m, 600m, 1, 1, 1, 1),
                (IssuedCfdiKind.GlobalInvoice, 1, 86.21m, 13.79m, 100m, 0, 0, 0, 0),
                (IssuedCfdiKind.PaymentComplement, 1, 0m, 0m, 120m, 0, 0, 0, 0),
                (IssuedCfdiKind.CreditNote, 1, 17.24m, 2.76m, 20m, 0, 0, 0, 0)
            ],
            summary.Kinds.Select(k => (k.Kind, k.InForce, k.SubTotal, k.TaxTotal, k.Total, k.Cancelling, k.Cancelled, k.Stamping, k.Refused)));
    }

    [SqlServerFact]
    public async Task Export_ShouldZipTheXmlOfTheStampedOnesByKind_TheCancelledApart_WithTheListOfAll_SayingWhichXmlWasNotFound()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var book = await SeedAsync(database);
        await using var context = database.CreateContext(book.TenantId);
        var storage = new Files();
        foreach (var key in book.XmlKeys.Where(key => key != "cfdi/f4.xml"))
            storage.Saved[key] = Encoding.UTF8.GetBytes($"<cfdi:Comprobante key=\"{key}\"/>");

        var exported = await new ExportIssuedCfdisQueryHandler(
                context, Tenant(book.TenantId), storage, NullLogger<ExportIssuedCfdisQueryHandler>.Instance)
            .Handle(new ExportIssuedCfdisQuery(2026, 10), CancellationToken.None);

        Assert.True(exported.IsSuccess, exported.Error);
        Assert.Equal(("application/zip", "CFDI_2026-10.zip"), (exported.Value.ContentType, exported.Value.FileName));
        using var zip = new ZipArchive(exported.Value.Content, ZipArchiveMode.Read);
        Assert.Equal(
            [
                $"ingresos/cancelados/F-1_{book.Uuids["F-1"]}.xml",
                $"ingresos/F-2_{book.Uuids["F-2"]}.xml",
                $"egresos/NC-1_{book.Uuids["NC-1"]}.xml",
                $"ingresos/F-3_{book.Uuids["F-3"]}.xml",
                $"pagos/P-1_{book.ComplementUuid}.xml",
                $"ingresos/F-6_{book.Uuids["F-6"]}.xml",
                "CFDI_2026-10.csv"
            ],
            zip.Entries.Select(e => e.FullName));
        using (var reader = new StreamReader(zip.GetEntry($"pagos/P-1_{book.ComplementUuid}.xml")!.Open()))
            Assert.Equal("<cfdi:Comprobante key=\"cfdi/p1.xml\"/>", await reader.ReadToEndAsync());

        var lines = await CsvLinesAsync(zip.GetEntry("CFDI_2026-10.csv")!);
        Assert.Equal(10, lines.Length);
        Assert.StartsWith("Tipo de comprobante,Documento,Serie,Folio,UUID,Fecha,", lines[0]);
        // By date: the CFDI's own date, in the business's time zone.
        Assert.Equal(
            $"I,Factura,F,1,{book.Uuids["F-1"]},2026-10-02 06:00:00,2026-10-02 06:00:00,Cancelado,2026-10-04 06:00:00,01," +
            $"{ReceiverRfc},Karla Fuente Nolasco,G03,PUE,01,172.41,27.59,200.00,,,{book.OrderNumber},ingresos/cancelados/F-1_{book.Uuids["F-1"]}.xml",
            lines[1]);
        // The credit note, paid in one exhibition.
        Assert.Equal(
            $"E,Nota de crédito,NC,1,{book.Uuids["NC-1"]},2026-10-05 06:00:00,2026-10-05 06:00:00,Vigente,,,{ReceiverRfc},Karla Fuente Nolasco,G02,PUE," +
            $"01,17.24,2.76,20.00,,{book.Uuids["F-2"]},{book.OrderNumber},egresos/NC-1_{book.Uuids["NC-1"]}.xml",
            lines[3]);
        Assert.Equal(
            $"P,Complemento de pago,P,1,{book.ComplementUuid},2026-10-07 06:00:00,2026-10-07 06:00:00,Vigente,,,{ReceiverRfc},Karla Fuente Nolasco,CP01,," +
            $"01,,,,120.00,{book.Uuids["F-3"]},ORD-000002,pagos/P-1_{book.ComplementUuid}.xml",
            lines[5]);
        Assert.Equal(
            $"I,Factura global,F,4,{book.Uuids["F-4"]},2026-10-09 06:00:00,2026-10-09 06:00:00,Vigente,,,{SatCatalog.GeneralPublicRfc}," +
            $"{SatCatalog.GeneralPublicName},S01,PUE,01,86.21,13.79,100.00,,,,{ExportIssuedCfdisQueryHandler.MissingXml}",
            lines[6]);
        Assert.Equal(
            $"I,Factura,F,6,{book.Uuids["F-6"]},2026-10-20 06:00:00,2026-10-20 06:00:00,En proceso de cancelación,,02,{ReceiverRfc},Karla Fuente Nolasco," +
            $"G03,PUE,01,172.41,27.59,200.00,,,ORD-000006,ingresos/F-6_{book.Uuids["F-6"]}.xml",
            lines[7]);
        Assert.Equal(
            $"I,Factura,F,7,,2026-10-21 06:00:00,,Timbrando,,,{ReceiverRfc},Karla Fuente Nolasco,G03,PUE,01,172.41,27.59,200.00,,,ORD-000007,",
            lines[8]);
        Assert.Equal(
            $"I,Factura,F,5,,2026-10-31 23:30:00,,Rechazado por el PAC,,,{ReceiverRfc},Karla Fuente Nolasco,G03,PUE,01,172.41,27.59,200.00,,,ORD-000005,",
            lines[9]);

        // The list alone, with the file each XML has in the ZIP.
        var csv = await new ExportIssuedCfdisQueryHandler(
                context, Tenant(book.TenantId), storage, NullLogger<ExportIssuedCfdisQueryHandler>.Instance)
            .Handle(new ExportIssuedCfdisQuery(2026, 10, IssuedCfdiExportFormat.Csv), CancellationToken.None);
        Assert.Equal(("text/csv; charset=utf-8", "CFDI_2026-10.csv"), (csv.Value.ContentType, csv.Value.FileName));
        var bytes = ((MemoryStream)csv.Value.Content).ToArray();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.EndsWith($",F-4_{book.Uuids["F-4"]}.xml", Encoding.UTF8.GetString(bytes).Split("\r\n")[6]);
    }

    [SqlServerFact]
    public async Task TheMonth_ShouldBeTheOneOfEachBusinesssTimeZone_InTheListTheSummaryAndTheDownload()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        // Two CFDI of each business at the same instants: 1 October at 06:30 UTC is still 30 September in Tijuana (UTC-7 in
        // summer) and already October in Mexico City (UTC-6); 1 November at 06:30 UTC is still October in Tijuana and already
        // November in Mexico City.
        var early = new DateTimeOffset(2026, 10, 1, 6, 30, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero);
        var tijuana = NewTenant();
        var tijuanaSettings = new TenantSettings(tijuana.Id, TenantCapability.Retail);
        tijuanaSettings.SetTimeZone("America/Tijuana");
        var mexicoCity = NewTenant();
        var entities = new List<object>
        {
            tijuana, tijuanaSettings, mexicoCity, new TenantSettings(mexicoCity.Id, TenantCapability.Retail)
        };
        foreach (var tenantId in new[] { tijuana.Id, mexicoCity.Id })
        foreach (var (folio, at) in new[] { (1L, early), (2L, late) })
        {
            var order = PaidOrder(tenantId, $"ORD-00000{folio}", at);
            var invoice = Invoice(tenantId, order.Id, folio, at);
            Stamp(invoice, new Dictionary<string, string>(), at, $"cfdi/{tenantId:N}-{folio}.xml");
            entities.AddRange([order, invoice]);
        }
        await using (var context = database.CreateContext(tijuana.Id))
        {
            context.AddRange(entities);
            await context.SaveChangesAsync();
        }

        foreach (var (tenantId, number, stampedAt) in new[]
                 {
                     (tijuana.Id, "F-2", "2026-10-31 23:30:00"),
                     (mexicoCity.Id, "F-1", "2026-10-01 00:30:00")
                 })
        {
            await using var context = database.CreateContext(tenantId);

            Assert.Equal([number], (await ListAsync(context, tenantId)).Items.Select(c => c.Number));
            var summary = await new GetIssuedCfdiSummaryQueryHandler(context, Tenant(tenantId))
                .Handle(new GetIssuedCfdiSummaryQuery(2026, 10), CancellationToken.None);
            Assert.Equal(1, summary.Kinds.Single(k => k.Kind == IssuedCfdiKind.OrderInvoice).InForce);
            var csv = await new ExportIssuedCfdisQueryHandler(
                    context, Tenant(tenantId), new Files(), NullLogger<ExportIssuedCfdisQueryHandler>.Instance)
                .Handle(new ExportIssuedCfdisQuery(2026, 10, IssuedCfdiExportFormat.Csv), CancellationToken.None);
            var lines = Encoding.UTF8.GetString(((MemoryStream)csv.Value.Content).ToArray())
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            // The header and the one CFDI, stamped at that hour of the business.
            Assert.Equal(2, lines.Length);
            Assert.Equal(stampedAt, lines[1].Split(',')[6]);
        }
    }

    private static async Task<string[]> CsvLinesAsync(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return (await reader.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static Task<FoodParkDigital.Application.Common.Models.PaginatedList<IssuedCfdiDto>> ListAsync(
        ApplicationDbContext context, Guid tenantId, IssuedCfdiKind? kind = null, OrderInvoiceStatus? status = null,
        string? search = null, int pageNumber = 1, int pageSize = 20)
        => new GetIssuedCfdisQueryHandler(context, Tenant(tenantId))
            .Handle(new GetIssuedCfdisQuery(2026, 10, kind, status, search, pageNumber, pageSize), CancellationToken.None);

    private static async Task<List<string>> NumbersAsync(ApplicationDbContext context, Guid tenantId, string search)
        => (await ListAsync(context, tenantId, search: search)).Items.Select(c => c.Number).ToList();

    private static ICurrentTenantService Tenant(Guid tenantId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        return tenant;
    }

    private sealed record Book(
        Guid TenantId, string OrderNumber, IReadOnlyDictionary<string, string> Uuids, string ComplementUuid, IReadOnlyList<string> XmlKeys);

    /// <summary>
    /// A business in Mexico City with, in October 2026: F-1, replaced by F-2 and cancelled with motive 01, and the credit note
    /// of a refund of that order (NC-1); F-3 in installments with the complement of a payment (P-1); the global invoice of the
    /// 8th (F-4); F-6, whose cancellation waits for the receiver; F-7, still stamping; and F-5, refused by the PAC, asked on the
    /// 31st at 23:30. Also an invoice of 30 September at 23:59 and another business's invoice of October.
    /// </summary>
    private static async Task<Book> SeedAsync(SqlServerTestDatabase database)
    {
        var tenant = NewTenant();
        var other = NewTenant();
        var uuids = new Dictionary<string, string>();
        var userId = Guid.NewGuid();

        var first = PaidOrder(tenant.Id, "ORD-000001", October(2));
        var f1 = Invoice(tenant.Id, first.Id, 1, October(2));
        Stamp(f1, uuids, October(2), "cfdi/f1.xml");
        var second = CreditOrder(tenant.Id, "ORD-000002", October(6));
        var f3 = Invoice(tenant.Id, second.Id, 3, October(6), installments: true);
        Stamp(f3, uuids, October(6), "cfdi/f3.xml");
        var payment = second.RegisterPayment(120m, OrderPaymentMethod.Cash, October(7), null, null, null, null);
        var publicSale = PaidOrder(tenant.Id, "ORD-000003", October(8), total: 100m);
        var refused = PaidOrder(tenant.Id, "ORD-000005", October(31));
        // 23:30 of the 31st in Mexico City is already November in UTC: it is October's.
        var f5 = Invoice(tenant.Id, refused.Id, 5, new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero));
        f5.Refuse("El RFC del receptor no existe.", new DateTimeOffset(2026, 11, 1, 5, 31, 0, TimeSpan.Zero));
        var waiting = PaidOrder(tenant.Id, "ORD-000006", October(20));
        var f6 = Invoice(tenant.Id, waiting.Id, 6, October(20));
        Stamp(f6, uuids, October(20), "cfdi/f6.xml");
        f6.RequestCancellation(CfdiCancellationMotive.ErrorsWithoutReplacement, userId, October(22));
        var stamping = PaidOrder(tenant.Id, "ORD-000007", October(21));
        var f7 = Invoice(tenant.Id, stamping.Id, 7, October(21));
        // 23:59 of 30 September in Mexico City is already October in UTC: it is September's.
        var september = PaidOrder(tenant.Id, "ORD-000000", October(1, 5));
        var f9 = Invoice(tenant.Id, september.Id, 9, new DateTimeOffset(2026, 10, 1, 5, 59, 0, TimeSpan.Zero));
        Stamp(f9, uuids, new DateTimeOffset(2026, 10, 1, 5, 59, 30, TimeSpan.Zero), "cfdi/f9.xml");
        var otherOrder = PaidOrder(other.Id, "ORD-000001", October(10));
        var otherInvoice = Invoice(other.Id, otherOrder.Id, 1, October(10));
        Stamp(otherInvoice, new Dictionary<string, string>(), October(10), "cfdi/other.xml");

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.AddRange(
                tenant, new TenantSettings(tenant.Id, TenantCapability.Retail), other, new TenantSettings(other.Id, TenantCapability.Retail),
                first, second, publicSale, refused, waiting, stamping, september, otherOrder, f1, f3, f5, f6, f7, f9, otherInvoice);
            await context.SaveChangesAsync();
        }

        // F-2 replaces F-1 (as a substitution saves them: F-1 stops counting before F-2 is added), and F-1 is cancelled with 01.
        var f2 = Invoice(tenant.Id, first.Id, 2, October(3));
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.OrderInvoices.Attach(f1);
            f1.ReplaceWith(f2);
            await context.SaveChangesAsync();
            Stamp(f2, uuids, October(3), "cfdi/f2.xml");
            context.OrderInvoices.Add(f2);
            await context.SaveChangesAsync();
            f1.RequestCancellation(CfdiCancellationMotive.ErrorsWithReplacement, userId, October(4));
            f1.Cancel(October(4));
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.Orders.Attach(first);
            var refund = first.RegisterRefund(20m, OrderPaymentMethod.Cash, "Devolución de un café", October(5), "cajero", null, null, false, []);
            var note = CreditNote.ForRefund(f2, refund, "NC", 1, 17.24m, 2.76m, 0, Document("NC", 1, October(5), "E", "G02"), null, October(5));
            Stamp(note, uuids, October(5), "cfdi/nc1.xml");
            var complement = PaymentComplement.Request(f3, payment, "P", 1, 1, 200m, Document("P", 1, October(7), "P", "CP01"), null, October(7));
            Stamp(complement, uuids, October(7), "cfdi/p1.xml");
            var global = GlobalInvoice.Request(
                tenant.Id, GlobalInvoicePeriod.Containing(GlobalInvoicePeriodicity.Daily, new DateOnly(2026, 10, 8)), "F", 4, "01",
                Document("F", 4, October(9), "I", "S01", SatCatalog.GeneralPublicRfc, SatCatalog.GeneralPublicName),
                86.21m, 13.79m, 100m, [new GlobalInvoiceSale(publicSale.Id, publicSale.OrderNumber, 100m)], userId, October(9));
            Stamp(global, uuids, October(9), "cfdi/f4.xml");
            context.AddRange(note, complement, global);
            await context.SaveChangesAsync();
        }

        return new Book(tenant.Id, first.OrderNumber, uuids, uuids["P-1"],
            ["cfdi/f1.xml", "cfdi/f2.xml", "cfdi/nc1.xml", "cfdi/f3.xml", "cfdi/p1.xml", "cfdi/f4.xml", "cfdi/f6.xml"]);
    }

    private static Tenant NewTenant()
    {
        var slug = $"tienda-{Guid.NewGuid():N}"[..20];
        return new Tenant("Tienda Luna", slug, BusinessType.Retail, $"{slug}@demo.com");
    }

    private static Order PaidOrder(Guid tenantId, string number, DateTimeOffset paidAt, decimal total = 200m)
    {
        var order = new Order(tenantId, null, null, true, number, BusinessType.Retail, OrderType.Retail, "Karla", "+525512345678",
            null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", total / 100m, 100m);
        order.RegisterPayment(total, OrderPaymentMethod.Cash, paidAt, null, null, null, null);
        return order;
    }

    private static Order CreditOrder(Guid tenantId, string number, DateTimeOffset at)
    {
        var order = new Order(tenantId, null, null, true, number, BusinessType.Retail, OrderType.Retail, "Karla", "+525512345678",
            null, null, pricesIncludeTax: true);
        order.AddItem(Guid.NewGuid(), "Café", 2, 100m);
        return order;
    }

    private static void Stamp(Cfdi cfdi, Dictionary<string, string> uuids, DateTimeOffset at, string xmlKey)
    {
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        cfdi.Stamp(uuid, $"pac-{xmlKey}", at, xmlKey);
        uuids[cfdi.Number] = uuid;
    }

    private static OrderInvoice Invoice(Guid tenantId, Guid orderId, long folio, DateTimeOffset requestedAt, bool installments = false)
    {
        var receiver = CfdiReceiver.Create(ReceiverRfc, "Karla Fuente Nolasco", "26015", "612", "G03");
        return OrderInvoice.Request(
            tenantId, orderId, "F", folio, receiver, installments ? "99" : "01", Document("F", folio, requestedAt, "I", "G03"),
            172.41m, 27.59m, 200m, Guid.NewGuid(), requestedAt,
            installments ? CfdiInvoice.PaidInInstallments : CfdiInvoice.PaidInOneExhibition);
    }

    /// <summary>A CFDI asked for at that instant, whose date is Mexico City's.</summary>
    private static string Document(
        string serie, long folio, DateTimeOffset at, string voucherType, string use, string rfc = ReceiverRfc, string name = "Karla Fuente Nolasco")
        => new CfdiDocument
        {
            Serie = serie,
            Folio = folio.ToString(),
            Date = at.ToOffset(TimeSpan.FromHours(-6)).ToString(CfdiDocument.DateFormat),
            PaymentMethod = voucherType == "I" ? "PUE" : null,
            ExpeditionPostalCode = "06000",
            VoucherType = voucherType,
            SubTotal = 0m,
            TotalTransferred = 0m,
            Total = 0m,
            Issuer = new CfdiDocumentIssuer(IssuerRfc, "Tienda Luna", "601"),
            Receiver = new CfdiDocumentReceiver(rfc, name, "26015", "612", use),
            Concepts = [],
            Transfers = []
        }.ToJson();

    /// <summary>The private files: what each key holds, if anything.</summary>
    private sealed class Files : IPrivateFileStorage
    {
        public Dictionary<string, byte[]> Saved { get; } = [];

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(Saved.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Infrastructure.Invoicing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// What TienditApp sends to SW Sapien, the platform's PAC, and how it reads the answers (Fase 28A, ADR 0045), against a
/// recorded HTTP handler: every amount goes as TienditApp computed it, the invoice's key goes as the customid, a stamp
/// already made comes back instead of a second one, and an answer that is not a clear yes is never read as one.
/// </summary>
public class SwSapienGatewayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Key = Guid.Parse("6f1c2b8e-3d4a-4b5c-9e7f-0a1b2c3d4e5f");
    private const string Uuid = "5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B";
    private const string Token = "T2lYQ0t4L0R";

    [Fact]
    public async Task Stamp_ShouldAuthenticate_AndSendTheCfdiWithItsAmounts_AndTheKeyAsCustomId()
    {
        var (gateway, http) = Create(Answer);

        var stamped = await gateway.StampAsync(Document(), Key, CancellationToken.None);

        Assert.Equal(Uuid, stamped.Uuid);
        Assert.Equal(StampedXml, stamped.Xml);
        // FechaTimbrado is written in Mexico City's time (UTC−6 in October 2026).
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 12, 0, 5, TimeSpan.FromHours(-6)), stamped.StampedAt);

        Assert.Equal(2, http.Requests.Count);
        var login = http.Requests[0];
        Assert.Equal("https://services.sw.com.mx/v2/security/authenticate", login.Url);
        using (var credentials = JsonDocument.Parse(login.Body!))
            Assert.Equal(("demo@negocio.mx", "secreta"), (credentials.RootElement.GetProperty("user").GetString(), credentials.RootElement.GetProperty("password").GetString()));

        var stamp = http.Requests[1];
        Assert.Equal((HttpMethod.Post, "https://services.sw.com.mx/v4/cfdi33/issue/json/v4"), (stamp.Method, stamp.Url));
        Assert.Equal(($"Bearer {Token}", Key.ToString("N"), "application/jsontoxml"), (stamp.Authorization, stamp.CustomId, stamp.ContentType));
        using var cfdi = JsonDocument.Parse(stamp.Body!);
        var root = cfdi.RootElement;
        Assert.Equal(("4.0", "F", "7", "2026-10-23T11:59:00", "04", "PUE", "I", "01", "86991"),
            (root.Str("Version"), root.Str("Serie"), root.Str("Folio"), root.Str("Fecha"), root.Str("FormaPago"), root.Str("MetodoPago"),
                root.Str("TipoDeComprobante"), root.Str("Exportacion"), root.Str("LugarExpedicion")));
        Assert.Equal(("190.00", "MXN", "220.40"), (root.Str("SubTotal"), root.Str("Moneda"), root.Str("Total")));
        // SW seals it with the CSD it keeps.
        Assert.Equal(("", "", ""), (root.Str("Sello"), root.Str("NoCertificado"), root.Str("Certificado")));
        Assert.Equal(("EKU9003173C9", "ESCUELA KEMPER URGATE", "601"),
            (root.GetProperty("Emisor").Str("Rfc"), root.GetProperty("Emisor").Str("Nombre"), root.GetProperty("Emisor").Str("RegimenFiscal")));
        var receiver = root.GetProperty("Receptor");
        Assert.Equal(("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03"),
            (receiver.Str("Rfc"), receiver.Str("Nombre"), receiver.Str("DomicilioFiscalReceptor"), receiver.Str("RegimenFiscalReceptor"), receiver.Str("UsoCFDI")));

        var concept = root.GetProperty("Conceptos")[0];
        Assert.Equal(("50202301", "1.5", "KGM", "Café molido", "126.666667", "190.00", "02"),
            (concept.Str("ClaveProdServ"), concept.Str("Cantidad"), concept.Str("ClaveUnidad"), concept.Str("Descripcion"),
                concept.Str("ValorUnitario"), concept.Str("Importe"), concept.Str("ObjetoImp")));
        var transfer = concept.GetProperty("Impuestos").GetProperty("Traslados")[0];
        Assert.Equal(("190.00", "002", "Tasa", "0.160000", "30.40"),
            (transfer.Str("Base"), transfer.Str("Impuesto"), transfer.Str("TipoFactor"), transfer.Str("TasaOCuota"), transfer.Str("Importe")));
        var taxes = root.GetProperty("Impuestos");
        Assert.Equal("30.40", taxes.Str("TotalImpuestosTrasladados"));
        Assert.Equal("30.40", taxes.GetProperty("Traslados")[0].Str("Importe"));
    }

    [Fact]
    public async Task Stamp_ShouldDeclareExemptAndNotSubjectConcepts_TheWayTheSatTakesThem()
    {
        var (gateway, http) = Create(Answer);
        var exempt = new CfdiDocumentTransfer(50m, "002", "Exento", null, null);
        var document = Document() with
        {
            SubTotal = 80m,
            TotalTransferred = 0m,
            Total = 80m,
            Concepts =
            [
                new CfdiDocumentConcept("51101500", "H87", "Medicina", 1m, 50m, 50m, "02", exempt),
                new CfdiDocumentConcept("01010101", "E48", "Propina del servicio", 1m, 30m, 30m, "01", null)
            ],
            Transfers = [exempt]
        };

        await gateway.StampAsync(document, Key, CancellationToken.None);

        using var cfdi = JsonDocument.Parse(http.Requests[1].Body!);
        var concepts = cfdi.RootElement.GetProperty("Conceptos");
        var exemptTransfer = concepts[0].GetProperty("Impuestos").GetProperty("Traslados")[0];
        Assert.Equal(("50.00", "Exento"), (exemptTransfer.Str("Base"), exemptTransfer.Str("TipoFactor")));
        Assert.False(exemptTransfer.TryGetProperty("TasaOCuota", out _));
        Assert.False(exemptTransfer.TryGetProperty("Importe", out _));
        Assert.False(concepts[1].TryGetProperty("Impuestos", out _));
        // Only exempt transfers: no total transferred.
        Assert.False(cfdi.RootElement.GetProperty("Impuestos").TryGetProperty("TotalImpuestosTrasladados", out _));

        // Nothing subject to taxes: no taxes node at all.
        await gateway.StampAsync(
            document with { Transfers = [], Concepts = [new CfdiDocumentConcept("01010101", "E48", "Servicio", 1m, 80m, 80m, "01", null)] },
            Key, CancellationToken.None);
        using var withoutTaxes = JsonDocument.Parse(http.Requests[^1].Body!);
        Assert.False(withoutTaxes.RootElement.TryGetProperty("Impuestos", out _));
    }

    [Fact]
    public async Task Stamp_OfAGlobalInvoice_ShouldSendInformacionGlobalBeforeTheIssuer_AndEachSaleWithItsFolioAndRates()
    {
        var (gateway, http) = Create(Answer);
        var sixteen = new CfdiDocumentTransfer(86.21m, "002", "Tasa", 0.16m, 13.79m);
        var zero = new CfdiDocumentTransfer(20m, "002", "Tasa", 0m, 0m);
        var document = Document() with
        {
            SubTotal = 106.21m,
            TotalTransferred = 13.79m,
            Total = 120m,
            Receiver = new CfdiDocumentReceiver("XAXX010101000", "PUBLICO EN GENERAL", "06000", "616", "S01"),
            Concepts = [new CfdiDocumentConcept("01010101", "ACT", "Venta", 1m, 106.21m, 106.21m, "02", null, "ORD-000041", [sixteen, zero])],
            Transfers = [sixteen, zero],
            GlobalInformation = new CfdiDocumentGlobalInformation("04", "10", 2026)
        };

        await gateway.StampAsync(document, Key, CancellationToken.None);

        using var cfdi = JsonDocument.Parse(http.Requests[1].Body!);
        var root = cfdi.RootElement;
        var global = root.GetProperty("InformacionGlobal");
        Assert.Equal(("04", "10", "2026"), (global.Str("Periodicidad"), global.Str("Meses"), global.Str("Año")));
        // The SAT's schema puts InformacionGlobal before Emisor; SW writes the XML in the order it reads.
        var names = root.EnumerateObject().Select(property => property.Name).ToList();
        Assert.True(names.IndexOf("InformacionGlobal") < names.IndexOf("Emisor"));
        var concept = root.GetProperty("Conceptos")[0];
        Assert.Equal(("ORD-000041", "ACT", "106.21"), (concept.Str("NoIdentificacion"), concept.Str("ClaveUnidad"), concept.Str("Importe")));
        var transfers = concept.GetProperty("Impuestos").GetProperty("Traslados");
        Assert.Equal(2, transfers.GetArrayLength());
        Assert.Equal(("86.21", "0.160000", "13.79"), (transfers[0].Str("Base"), transfers[0].Str("TasaOCuota"), transfers[0].Str("Importe")));
        Assert.Equal(("20.00", "0.000000", "0.00"), (transfers[1].Str("Base"), transfers[1].Str("TasaOCuota"), transfers[1].Str("Importe")));
        // An order's invoice has neither.
        await gateway.StampAsync(Document(), Key, CancellationToken.None);
        using var order = JsonDocument.Parse(http.Requests[^1].Body!);
        Assert.False(order.RootElement.TryGetProperty("InformacionGlobal", out _));
        Assert.False(order.RootElement.GetProperty("Conceptos")[0].TryGetProperty("NoIdentificacion", out _));
    }

    [Fact]
    public async Task Stamp_OfAPaymentComplement_ShouldSendATypePWorthZero_WithItsPagos20Complement()
    {
        var (gateway, http) = Create(Answer);
        var sixteen = new CfdiDocumentTransfer(43.10m, "002", "Tasa", 0.16m, 6.90m);
        var exempt = new CfdiDocumentTransfer(25m, "002", "Exento", null, null);
        var payment = new CfdiDocumentPayment(
            "2026-10-20T13:15:00", "03", 75m,
            new CfdiDocumentPaidInvoice(Uuid, "F", "7", 2, 150m, 75m, 75m, "02", [sixteen, exempt]));
        var document = CfdiDocument.ForPayment(
            new CfdiDocumentIssuer("EKU9003173C9", "Escuela Kemper Urgate", "601"),
            new CfdiDocumentReceiver("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03"),
            "86991", "P", 3, new DateTime(2026, 10, 23, 11, 59, 0), payment);

        await gateway.StampAsync(document, Key, CancellationToken.None);

        using var cfdi = JsonDocument.Parse(http.Requests[1].Body!);
        var root = cfdi.RootElement;
        // Worth zero in no currency (which takes no decimals), without form or method of payment.
        Assert.Equal(("P", "3", "P", "0", "XXX", "0", "01"),
            (root.Str("Serie"), root.Str("Folio"), root.Str("TipoDeComprobante"), root.Str("SubTotal"), root.Str("Moneda"), root.Str("Total"),
                root.Str("Exportacion")));
        Assert.False(root.TryGetProperty("FormaPago", out _));
        Assert.False(root.TryGetProperty("MetodoPago", out _));
        Assert.False(root.TryGetProperty("Impuestos", out _));
        // The invoice's receiver, with the use «Pagos».
        Assert.Equal(("URE180429TM6", "CP01"), (root.GetProperty("Receptor").Str("Rfc"), root.GetProperty("Receptor").Str("UsoCFDI")));
        var concept = root.GetProperty("Conceptos")[0];
        Assert.Equal(("84111506", "1", "ACT", "Pago", "0", "0", "01"),
            (concept.Str("ClaveProdServ"), concept.Str("Cantidad"), concept.Str("ClaveUnidad"), concept.Str("Descripcion"),
                concept.Str("ValorUnitario"), concept.Str("Importe"), concept.Str("ObjetoImp")));
        Assert.False(concept.TryGetProperty("Impuestos", out _));

        var pagos = root.GetProperty("Complemento").GetProperty("Any")[0].GetProperty("Pago20:Pagos");
        Assert.Equal("2.0", pagos.Str("Version"));
        var totals = pagos.GetProperty("Totales");
        Assert.Equal(("43.10", "6.90", "25.00", "75.00"),
            (totals.Str("TotalTrasladosBaseIVA16"), totals.Str("TotalTrasladosImpuestoIVA16"), totals.Str("TotalTrasladosBaseIVAExento"),
                totals.Str("MontoTotalPagos")));
        Assert.False(totals.TryGetProperty("TotalTrasladosBaseIVA8", out _));
        var pago = pagos.GetProperty("Pago")[0];
        Assert.Equal(("2026-10-20T13:15:00", "03", "MXN", "1", "75.00"),
            (pago.Str("FechaPago"), pago.Str("FormaDePagoP"), pago.Str("MonedaP"), pago.Str("TipoCambioP"), pago.Str("Monto")));
        var related = pago.GetProperty("DoctoRelacionado")[0];
        Assert.Equal((Uuid, "F", "7", "MXN", "1", "2", "150.00", "75.00", "75.00", "02"),
            (related.Str("IdDocumento"), related.Str("Serie"), related.Str("Folio"), related.Str("MonedaDR"), related.Str("EquivalenciaDR"),
                related.Str("NumParcialidad"), related.Str("ImpSaldoAnt"), related.Str("ImpPagado"), related.Str("ImpSaldoInsoluto"),
                related.Str("ObjetoImpDR")));
        var transferDr = related.GetProperty("ImpuestosDR").GetProperty("TrasladosDR");
        Assert.Equal(("43.10", "002", "Tasa", "0.160000", "6.90"),
            (transferDr[0].Str("BaseDR"), transferDr[0].Str("ImpuestoDR"), transferDr[0].Str("TipoFactorDR"), transferDr[0].Str("TasaOCuotaDR"),
                transferDr[0].Str("ImporteDR")));
        Assert.Equal(("25.00", "Exento"), (transferDr[1].Str("BaseDR"), transferDr[1].Str("TipoFactorDR")));
        Assert.False(transferDr[1].TryGetProperty("TasaOCuotaDR", out _));
        var transferP = pago.GetProperty("ImpuestosP").GetProperty("TrasladosP");
        Assert.Equal(("43.10", "0.160000", "6.90"), (transferP[0].Str("BaseP"), transferP[0].Str("TasaOCuotaP"), transferP[0].Str("ImporteP")));
        // The SAT's schema puts the related invoice before the payment's taxes.
        var names = pago.EnumerateObject().Select(property => property.Name).ToList();
        Assert.True(names.IndexOf("DoctoRelacionado") < names.IndexOf("ImpuestosP"));

        // An invoice not subject to tax: no taxes, only the amount of the payments.
        await gateway.StampAsync(
            document with { Payment = payment with { Invoice = payment.Invoice with { TaxObject = "01", Transfers = [] } } },
            Key, CancellationToken.None);
        using var withoutTaxes = JsonDocument.Parse(http.Requests[^1].Body!);
        var plain = withoutTaxes.RootElement.GetProperty("Complemento").GetProperty("Any")[0].GetProperty("Pago20:Pagos");
        Assert.Equal(["MontoTotalPagos"], plain.GetProperty("Totales").EnumerateObject().Select(property => property.Name));
        Assert.False(plain.GetProperty("Pago")[0].TryGetProperty("ImpuestosP", out _));
        Assert.False(plain.GetProperty("Pago")[0].GetProperty("DoctoRelacionado")[0].TryGetProperty("ImpuestosDR", out _));
    }

    [Fact]
    public async Task Token_ShouldBeReused_UntilItIsAboutToExpire_AndATokenWithoutExpiryNeedsNoLogin()
    {
        var clock = new TestClock(Now);
        var (gateway, http) = Create(Answer, clock: clock);
        int Logins() => http.Requests.Count(r => r.Url.EndsWith("/v2/security/authenticate", StringComparison.Ordinal));
        await gateway.StampAsync(Document(), Key, CancellationToken.None);
        await gateway.StampAsync(Document(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(1, Logins());

        // expires_in is the moment it expires (two hours from now), not a duration: six minutes before it the token is
        // still used, and four minutes before it (inside the five of margin) a new one is asked for.
        clock.UtcNow = Now.AddHours(2).AddMinutes(-6);
        await gateway.StampAsync(Document(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(1, Logins());
        clock.UtcNow = Now.AddHours(2).AddMinutes(-4);
        await gateway.StampAsync(Document(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(2, Logins());

        var (fixedToken, fixedHttp) = Create(Answer, options => options.Token = "infinito");
        await fixedToken.StampAsync(Document(), Key, CancellationToken.None);
        Assert.Equal("Bearer infinito", Assert.Single(fixedHttp.Requests).Authorization);
    }

    [Fact]
    public async Task Stamp_WithAKeyAlreadyStamped_ShouldGiveBackThatStamp_ReadingItsXmlFromWhatSwKeeps()
    {
        // As SW documents the answer to a repeated customid: the stamp alone in «tfd», an empty «cfdi» and no «uuid».
        var duplicate = JsonSerializer.Serialize(new
        {
            message = "CFDI3307 - Timbre duplicado. El customId proporcionado está duplicado.",
            messageDetail = (string?)null,
            data = new
            {
                tfd = $"""<?xml version="1.0" encoding="utf-8"?><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="1.1" UUID="{Uuid}" FechaTimbrado="2026-10-23T12:00:05" SelloCFD="AAAABBBBCCCCDDDD" NoCertificadoSAT="30001000000400002495" />""",
                cfdi = ""
            },
            status = "error"
        });
        var (gateway, http) = Create(request => request.RequestUri!.AbsolutePath switch
        {
            "/v2/security/authenticate" => Login(),
            "/v4/cfdi33/issue/json/v4" => Json(HttpStatusCode.BadRequest, duplicate),
            "/datawarehouse/v1/live/" + Uuid => Json(HttpStatusCode.OK,
                """{"data":{"records":[{"status":"Vigente","urlXml":"https://swstorage.s3.amazonaws.com/xml/f-7.xml?signature=abc"}]},"status":"success"}"""),
            "/xml/f-7.xml" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StampedXml) },
            _ => throw new InvalidOperationException(request.RequestUri!.ToString())
        });

        var stamped = await gateway.StampAsync(Document(), Key, CancellationToken.None);

        Assert.Equal((Uuid, StampedXml), (stamped.Uuid, stamped.Xml));
        Assert.Equal("https://api.sw.com.mx/datawarehouse/v1/live/" + Uuid, http.Requests[2].Url);
        // The signed link to SW's storage goes without the platform's token, and outside the breaker of stamping.
        Assert.Null(http.Requests[3].Authorization);
        Assert.Equal(
            [SwSapienGateway.HttpClientName, SwSapienGateway.HttpClientName, SwSapienGateway.HttpClientName, SwSapienGateway.SatHttpClientName],
            http.Requests.Select(r => r.Client));
    }

    [Fact]
    public async Task Stamp_OfTheSameCfdiAgain_ShouldGiveBackTheStampItAlreadyHad()
    {
        var (gateway, _) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.BadRequest, StampAnswer("307. El comprobante contiene un timbre previo.", "error")));

        Assert.Equal(Uuid, (await gateway.StampAsync(Document(), Key, CancellationToken.None)).Uuid);
    }

    [Fact]
    public async Task Stamp_RefusedByTheSat_ShouldGiveItsReason_WithWhatWasExpected()
    {
        var (gateway, _) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.BadRequest, """
                {"message":"CFDI40145 - El campo Nombre del receptor, debe pertenecer al nombre asociado al RFC registrado en el campo Rfc del Receptor.","messageDetail":"Nombre: UNIVERSIDAD","data":null,"status":"error"}
                """));

        var error = await Assert.ThrowsAsync<InvoicingGatewayRefusedException>(() => gateway.StampAsync(Document(), Key, CancellationToken.None));

        Assert.StartsWith("CFDI40145 - El campo Nombre del receptor", error.Message);
        Assert.EndsWith("Nombre: UNIVERSIDAD", error.Message);
    }

    [Fact]
    public async Task Stamp_WithoutBalance_ShouldWait_NotRefuseTheInvoice_AfterAskingForAnotherToken()
    {
        var (gateway, http) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.Unauthorized, """{"message":"AU2001 - El usuario no tiene saldo en timbres","data":null,"status":"error"}"""));

        await Assert.ThrowsAsync<InvoicingGatewayUnavailableException>(() => gateway.StampAsync(Document(), Key, CancellationToken.None));
        Assert.Equal(2, http.Requests.Count(r => r.Url.EndsWith("/v2/security/authenticate", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Stamp_WhenSwFails_ShouldBeTriedAgainLater(HttpStatusCode status)
    {
        var (gateway, _) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(status, """{"message":"CO1000 - Error No Controlado","status":"error"}"""));

        await Assert.ThrowsAsync<InvoicingGatewayUnavailableException>(() => gateway.StampAsync(Document(), Key, CancellationToken.None));
    }

    [Fact]
    public async Task Stamp_WhenSwDoesNotAnswer_ShouldBeTriedAgainLater()
    {
        var (gateway, _) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : throw new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<InvoicingGatewayUnavailableException>(() => gateway.StampAsync(Document(), Key, CancellationToken.None));
    }

    [Fact]
    public async Task Certificate_ShouldGoToSwAsAStampingCsd_AndARefusalSaysWhy()
    {
        var (gateway, http) = Create(request => request.RequestUri!.AbsolutePath switch
        {
            "/v2/security/authenticate" => Login(),
            "/certificates/save" => Json(HttpStatusCode.OK, """{"data":"CSD Guardados Correctamente.","status":"success"}"""),
            _ => Json(HttpStatusCode.OK, """{"status":"success"}""")
        });

        await gateway.SaveCertificateAsync([1, 2, 3], [4, 5], "12345678a", CancellationToken.None);
        await gateway.RemoveCertificateAsync("30001000000400002434", CancellationToken.None);

        using var body = JsonDocument.Parse(http.Requests[1].Body!);
        Assert.Equal(("stamp", "AQID", "BAU=", "12345678a"),
            (body.RootElement.Str("type"), body.RootElement.Str("b64Cer"), body.RootElement.Str("b64Key"), body.RootElement.Str("password")));
        Assert.Equal((HttpMethod.Delete, "https://services.sw.com.mx/certificates/30001000000400002434"), (http.Requests[2].Method, http.Requests[2].Url));

        var (refusing, _) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.BadRequest, """{"message":"Certificados","messageDetail":"El certificado es incorrecto, esta incompleto o contiene espacios.","status":"error"}"""));
        var error = await Assert.ThrowsAsync<InvoicingGatewayRefusedException>(() => refusing.SaveCertificateAsync([1], [2], "x", CancellationToken.None));
        Assert.Contains("El certificado es incorrecto", error.Message);
    }

    [Theory]
    [InlineData("201", CfdiCancellationOutcome.Requested)]
    [InlineData("202", CfdiCancellationOutcome.Cancelled)]
    public async Task Cancel_ShouldTellWhetherItIsCancelled_OrWaitsForTheReceiver(string code, CfdiCancellationOutcome expected)
    {
        var (gateway, http) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.OK, $$$"""{"data":{"acuse":"<Acuse/>","uuid":{"{{{Uuid}}}":"{{{code}}}"}},"status":"success"}"""));

        var outcome = await gateway.CancelAsync("EKU9003173C9", Uuid, CfdiCancellationMotive.ErrorsWithoutReplacement, CancellationToken.None);

        Assert.Equal(expected, outcome);
        Assert.Equal($"https://services.sw.com.mx/cfdi33/cancel/EKU9003173C9/{Uuid}/02", http.Requests[1].Url);
    }

    [Fact]
    public async Task Cancel_ThatTheSatRefuses_OrDoesNotHaveYet_ShouldSayWhy()
    {
        var (notCancellable, _) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.OK, $$$"""{"data":{"acuse":"","uuid":{"{{{Uuid}}}":"204"}},"status":"success"}"""));
        var refused = await Assert.ThrowsAsync<InvoicingGatewayRefusedException>(() =>
            notCancellable.CancelAsync("EKU9003173C9", Uuid, CfdiCancellationMotive.NotCarriedOut, CancellationToken.None));
        Assert.Contains("no se puede cancelar", refused.Message);

        var (notYet, _) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.OK, $$$"""{"data":{"acuse":"","uuid":{"{{{Uuid}}}":"205"}},"status":"success"}"""));
        await Assert.ThrowsAsync<InvoicingGatewayUnavailableException>(() =>
            notYet.CancelAsync("EKU9003173C9", Uuid, CfdiCancellationMotive.NotCarriedOut, CancellationToken.None));
    }

    [Fact]
    public async Task Pdf_ShouldComeFromTheStampedXml()
    {
        var pdf = "%PDF-1.7"u8.ToArray();
        var (gateway, http) = Create(request => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
            ? Login()
            : Json(HttpStatusCode.OK, $$"""{"data":{"contentB64":"{{Convert.ToBase64String(pdf)}}","uuid":"{{Uuid}}"},"status":"success"}"""));

        Assert.Equal(pdf, await gateway.RenderPdfAsync(StampedXml, CancellationToken.None));
        using var body = JsonDocument.Parse(http.Requests[1].Body!);
        Assert.Equal(("https://api.sw.com.mx/pdf/v1/api/GeneratePdf", StampedXml, "cfdi40"),
            (http.Requests[1].Url, body.RootElement.Str("xmlContent"), body.RootElement.Str("templateId")));
    }

    [Theory]
    [InlineData("Cancelado", "Cancelado sin aceptación", CfdiSatState.Cancelled, false)]
    [InlineData("Vigente", "Solicitud rechazada", CfdiSatState.InForce, true)]
    [InlineData("Vigente", "En proceso", CfdiSatState.InForce, false)]
    [InlineData("No Encontrado", "", CfdiSatState.NotFound, false)]
    public async Task Status_ShouldReadWhatTheSatSays(string state, string cancellation, CfdiSatState expected, bool refused)
    {
        var (gateway, http) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"""
                <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><ConsultaResponse xmlns="http://tempuri.org/">
                <ConsultaResult xmlns:a="http://schemas.datacontract.org/2004/07/Sat.Cfdi.Negocio.ConsultaCfdi.Servicio">
                <a:CodigoEstatus>S - Comprobante obtenido satisfactoriamente.</a:CodigoEstatus><a:EsCancelable>Cancelable con aceptación</a:EsCancelable>
                <a:Estado>{state}</a:Estado><a:EstatusCancelacion>{cancellation}</a:EstatusCancelacion></ConsultaResult></ConsultaResponse></s:Body></s:Envelope>
                """, Encoding.UTF8, "text/xml")
        });

        var status = await gateway.GetSatStatusAsync(new CfdiStatusQuery("EKU9003173C9", "URE180429TM6", 220.4m, Uuid, "ABCDEFGH"), CancellationToken.None);

        Assert.Equal(new CfdiSatStatus(expected, refused), status);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://consultaqr.facturaelectronica.sat.gob.mx/ConsultaCFDIService.svc", request.Url);
        // The SAT failing often never opens the breaker that stamping goes through.
        Assert.Equal(SwSapienGateway.SatHttpClientName, request.Client);
        Assert.Contains($"?re=EKU9003173C9&amp;rr=URE180429TM6&amp;tt=220.400000&amp;id={Uuid}&amp;fe=ABCDEFGH", request.Body);
    }

    [Fact]
    public async Task Status_OfAnRfcWithAnAmpersand_ShouldWriteItAsTheSatAsks()
    {
        var (gateway, http) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><a:Estado xmlns:a="x">Vigente</a:Estado></s:Body></s:Envelope>""",
                Encoding.UTF8, "text/xml")
        });

        await gateway.GetSatStatusAsync(new CfdiStatusQuery("A&B010101AB1", "M&M8001019Z3", 100m, Uuid, "ABCDEFGH"), CancellationToken.None);

        // «&amp;» inside the expression, which the XML of the request escapes once more.
        var expression = XDocument.Parse(Assert.Single(http.Requests).Body!).Descendants().Single(e => e.Name.LocalName == "expresionImpresa").Value;
        Assert.Equal($"?re=A&amp;B010101AB1&rr=M&amp;M8001019Z3&tt=100.000000&id={Uuid}&fe=ABCDEFGH", expression);
    }

    [Fact]
    public void Configuration_ShouldNeedAnAccount_AndTheTestEnvironmentUsesItsHosts()
    {
        Assert.False(Create(Answer, options => (options.User, options.Password) = (null, null)).Gateway.IsConfigured);
        Assert.False(Create(Answer, options => options.Password = " ").Gateway.IsConfigured);
        Assert.True(Create(Answer, options => (options.User, options.Password, options.Token) = (null, null, "infinito")).Gateway.IsConfigured);
        Assert.True(Create(Answer, options => options.Sandbox = true).Gateway.Sandbox);
    }

    [Fact]
    public async Task TestEnvironment_ShouldStampOnSwsTestHost()
    {
        var (gateway, http) = Create(Answer, options => options.Sandbox = true);

        await gateway.StampAsync(Document(), Key, CancellationToken.None);

        Assert.All(http.Requests, request => Assert.StartsWith("https://services.test.sw.com.mx/", request.Url));
    }

    private const string StampedXml = """
        <?xml version="1.0" encoding="utf-8"?><cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Serie="F" Folio="7" Sello="AAAABBBBCCCCDDDD"><cfdi:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="1.1" UUID="5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B" FechaTimbrado="2026-10-23T12:00:05" /></cfdi:Complemento></cfdi:Comprobante>
        """;

    private static CfdiDocument Document()
    {
        var transfer = new CfdiDocumentTransfer(190m, "002", "Tasa", 0.16m, 30.40m);
        return new CfdiDocument
        {
            Serie = "F",
            Folio = "7",
            Date = "2026-10-23T11:59:00",
            PaymentForm = "04",
            PaymentMethod = "PUE",
            ExpeditionPostalCode = "86991",
            SubTotal = 190m,
            TotalTransferred = 30.40m,
            Total = 220.40m,
            Issuer = new CfdiDocumentIssuer("EKU9003173C9", "Escuela Kemper Urgate", "601"),
            Receiver = new CfdiDocumentReceiver("URE180429TM6", "UNIVERSIDAD ROBOTICA ESPAÑOLA", "86991", "601", "G03"),
            Concepts = [new CfdiDocumentConcept("50202301", "KGM", "Café molido", 1.5m, 126.666667m, 190m, "02", transfer)],
            Transfers = [transfer]
        };
    }

    private static HttpResponseMessage Answer(HttpRequestMessage request) => request.RequestUri!.AbsolutePath == "/v2/security/authenticate"
        ? Login()
        : Json(HttpStatusCode.OK, StampAnswer(null, "success"));

    private static string StampAnswer(string? message, string status) => JsonSerializer.Serialize(new
    {
        message,
        data = new { cfdi = StampedXml, uuid = Uuid, fechaTimbrado = "2026-10-23T12:00:05", noCertificadoSAT = "30001000000500003456" },
        status
    });

    private static HttpResponseMessage Login() => Json(HttpStatusCode.OK,
        $$"""{"data":{"token":"{{Token}}","expires_in":{{Now.AddHours(2).ToUnixTimeSeconds()}},"tokeny_type":"Bearer"},"status":"success"}""");

    private static (SwSapienGateway Gateway, RecordingHandler Http) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, Action<SwSapienOptions>? configure = null, TestClock? clock = null)
    {
        var http = new RecordingHandler(respond);
        var options = new SwSapienOptions { User = "demo@negocio.mx", Password = "secreta" };
        configure?.Invoke(options);
        var gateway = new SwSapienGateway(
            new SingleClientFactory(http), Options.Create(options), clock ?? new TestClock(Now), NullLogger<SwSapienGateway>.Instance);
        return (gateway, http);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(
        HttpMethod Method, string Url, string? Authorization, string? CustomId, string? ContentType, string? Body, string? Client = null);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>The named client the next request goes through (each request asks for its client).</summary>
        public string? NextClient { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("customid", out var ids) ? ids.Single() : null,
                request.Content?.Headers.ContentType?.MediaType,
                body,
                NextClient));
            return respond(request);
        }
    }

    /// <summary>One handler behind both clients; the handler notes which client sent each request.</summary>
    private sealed class SingleClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Contains(name, new[] { SwSapienGateway.HttpClientName, SwSapienGateway.SatHttpClientName });
            handler.NextClient = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}

internal static class SwJsonAssertions
{
    public static string? Str(this JsonElement element, string name) => element.GetProperty(name).GetString();
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace FoodParkDigital.Infrastructure.Invoicing;

/// <summary>
/// The only client of SW Sapien, the platform's PAC (Fase 28A, ADR 0045). The businesses' CSD are kept by SW; TienditApp
/// sends the CFDI with every amount it computed (SW checks them against the SAT's rules and refuses the ones that do not
/// add up, it never recomputes them), with the invoice's key as SW's customid, so another attempt gets back the stamp it
/// already made. CSD files, passwords and tokens never reach the logs: only the operation, the status and SW's code.
/// </summary>
public sealed class SwSapienGateway : IInvoicingGateway
{
    public const string HttpClientName = "SwSapien";
    /// <summary>The SAT's status service and the signed links to SW's storage, with a breaker of their own.</summary>
    public const string SatHttpClientName = "SatCfdiStatus";
    // SW's generic templates of CFDI 4.0 (the only ones that take a logo): payments have their own (Fase 28L).
    private const string PdfTemplate = "cfdi40";
    private const string PaymentPdfTemplate = "payment20";
    private const string ProductionStatusUrl = "https://consultaqr.facturaelectronica.sat.gob.mx/ConsultaCFDIService.svc";
    private const string TestStatusUrl = "https://api.test.sw.com.mx/ConsultaCFDIService.svc";
    private static readonly TimeSpan TokenMargin = TimeSpan.FromMinutes(5);
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Tempuri = "http://tempuri.org/";
    private static readonly XNamespace StampNamespace = "http://www.sat.gob.mx/TimbreFiscalDigital";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SwSapienOptions _options;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SwSapienGateway> _logger;
    private readonly ICfdiPdfLogos _logos;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public SwSapienGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<SwSapienOptions> options,
        IDateTimeProvider clock,
        ICfdiPdfLogos logos,
        ILogger<SwSapienGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clock = clock;
        _logos = logos;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Token)
        || (!string.IsNullOrWhiteSpace(_options.User) && !string.IsNullOrWhiteSpace(_options.Password));

    public bool Sandbox => _options.Sandbox;

    private string ServicesUrl => Base(_options.ServicesUrl, _options.Sandbox ? "https://services.test.sw.com.mx" : "https://services.sw.com.mx");
    private string ApiUrl => Base(_options.ApiUrl, _options.Sandbox ? "https://api.test.sw.com.mx" : "https://api.sw.com.mx");
    private string StatusUrl => Base(_options.StatusUrl, _options.Sandbox ? TestStatusUrl : ProductionStatusUrl);

    public async Task SaveCertificateAsync(byte[] certificate, byte[] privateKey, string password, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["type"] = "stamp",
            ["b64Cer"] = Convert.ToBase64String(certificate),
            ["b64Key"] = Convert.ToBase64String(privateKey),
            ["password"] = password,
            ["isActive"] = true
        };
        using var response = await SendAsync(
            HttpMethod.Post, $"{ServicesUrl}/certificates/save", "certificate", JsonContent.Create(body), cancellationToken: cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var error = await ReadErrorAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("SW refused a CSD: {Status} {Code}", (int)response.StatusCode, error.Code);
            throw new InvoicingGatewayRefusedException($"El PAC no aceptó el certificado: {error.Text}");
        }
        throw Unavailable(response, error, "certificate");
    }

    public async Task RemoveCertificateAsync(string certificateNumber, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Delete, $"{ServicesUrl}/certificates/{Uri.EscapeDataString(certificateNumber)}", "certificate removal",
            cancellationToken: cancellationToken);
        // One the PAC no longer has is already gone.
        if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            return;
        throw Unavailable(response, await ReadErrorAsync(response, cancellationToken), "certificate removal");
    }

    public async Task<StampedCfdi> StampAsync(CfdiDocument document, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        var content = new StringContent(SwJson(document).ToJsonString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/jsontoxml");
        using var response = await SendAsync(
            HttpMethod.Post, $"{ServicesUrl}/v4/cfdi33/issue/json/v4", "stamp", content,
            request => request.Headers.Add("customid", idempotencyKey.ToString("N")), cancellationToken);

        var error = response.IsSuccessStatusCode ? default : await ReadErrorAsync(response, cancellationToken);
        if (response.IsSuccessStatusCode || IsAlreadyStamped(error.Code))
        {
            // A stamp the key already made comes back as an error that carries it.
            if (!response.IsSuccessStatusCode)
                _logger.LogInformation("SW gave back the stamp the invoice already had ({Code})", error.Code);
            var data = response.IsSuccessStatusCode ? await ReadDataAsync(response, cancellationToken) : error.Data;
            return await StampedAsync(data, cancellationToken);
        }

        // Without balance or without a valid token the platform cannot stamp now: the invoice waits, it is not wrong.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogError("SW did not take the platform's account to stamp: {Status} {Code}", (int)response.StatusCode, error.Code);
            throw new InvoicingGatewayUnavailableException("No se pudo timbrar por ahora. Intenta de nuevo en unos minutos.");
        }
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("SW refused a CFDI: {Code}", error.Code);
            throw new InvoicingGatewayRefusedException(error.Text);
        }
        throw Unavailable(response, error, "stamp");
    }

    public async Task<byte[]> RenderPdfAsync(string stampedXml, Guid tenantId, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["xmlContent"] = stampedXml, ["templateId"] = PdfTemplateFor(stampedXml) };
        // The generic template takes the logo in base64; without one, it leaves its box empty.
        if (await _logos.PngAsync(tenantId, cancellationToken) is { } logo)
            body["logo"] = Convert.ToBase64String(logo);
        using var response = await SendAsync(
            HttpMethod.Post, $"{ApiUrl}/pdf/v1/api/GeneratePdf", "pdf", JsonContent.Create(body), cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Unavailable(response, default, "pdf");

        var data = await ReadDataAsync(response, cancellationToken);
        var pdf = Text(data?["contentB64"]);
        if (string.IsNullOrWhiteSpace(pdf))
            throw new InvoicingGatewayUnavailableException("El PAC no entregó el PDF de la factura.");
        return Convert.FromBase64String(pdf);
    }

    /// <summary>The template of a payment complement (TipoDeComprobante P) is SW's of Pagos 2.0; the rest, CFDI 4.0's.</summary>
    internal static string PdfTemplateFor(string stampedXml)
    {
        try
        {
            return XDocument.Parse(stampedXml).Root?.Attribute("TipoDeComprobante")?.Value == CfdiDocument.PaymentVoucherType
                ? PaymentPdfTemplate
                : PdfTemplate;
        }
        catch (XmlException)
        {
            return PdfTemplate;
        }
    }

    public const string ReplacementNotRelatedMessage =
        "El SAT todavía no relaciona esta factura con la que la sustituye: puede que aún no reciba la nueva (tarda hasta dos días).";

    public async Task<CfdiCancellationOutcome> CancelAsync(
        string issuerRfc, string uuid, CfdiCancellationMotive motive, string? replacementUuid, CancellationToken cancellationToken)
    {
        var replaced = motive == CfdiCancellationMotive.ErrorsWithReplacement;
        if (replaced != replacementUuid is not null)
            throw new InvalidOperationException("Motive 01, and only it, goes with the UUID of the invoice that replaces it.");

        // With motive 01 the folio de sustitución goes last (Fase 28G); SW refuses it with any other motive (209).
        var url = $"{ServicesUrl}/cfdi33/cancel/{Uri.EscapeDataString(issuerRfc)}/{Uri.EscapeDataString(uuid)}/{(int)motive:00}"
            + (replacementUuid is null ? string.Empty : $"/{Uri.EscapeDataString(replacementUuid)}");
        using var response = await SendAsync(HttpMethod.Post, url, "cancellation", cancellationToken: cancellationToken);
        var error = response.IsSuccessStatusCode ? default : await ReadErrorAsync(response, cancellationToken);
        var data = response.IsSuccessStatusCode ? await ReadDataAsync(response, cancellationToken) : error.Data;
        var code = (data?["uuid"] as JsonObject)?.Select(entry => Text(entry.Value)).FirstOrDefault();

        switch (code)
        {
            case "201":
                return CfdiCancellationOutcome.Requested;
            case "202":
                return CfdiCancellationOutcome.Cancelled;
            case "205":
                // The SAT does not have it yet (it takes up to two days).
                throw new InvoicingGatewayUnavailableException("El SAT todavía no recibe esta factura: intenta cancelarla en unas horas.");
            case "203":
                throw new InvoicingGatewayRefusedException("El SAT no encuentra esta factura entre las del negocio.");
            case "204":
                throw new InvoicingGatewayRefusedException("El SAT dice que esta factura no se puede cancelar.");
            case "207" or "208" or "212" or "312" when replaced:
                // The replacement it names does not exist for the SAT yet (it takes up to two days), was cancelled, or is not
                // related to this one.
                throw new InvoicingGatewayRefusedException(ReplacementNotRelatedMessage);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("SW refused a cancellation: {Code}", error.Code ?? code);
            throw new InvoicingGatewayRefusedException(error.Text);
        }
        throw Unavailable(response, error, "cancellation");
    }

    public async Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken)
    {
        // The SAT asks for an «&» inside an RFC written as «&amp;» in the expression (the XML element escapes it once more).
        static string Rfc(string rfc) => rfc.Replace("&", "&amp;", StringComparison.Ordinal);
        var expression = string.Create(CultureInfo.InvariantCulture,
            $"?re={Rfc(query.IssuerRfc)}&rr={Rfc(query.ReceiverRfc)}&tt={query.Total:0.000000}&id={query.Uuid}&fe={query.SealTail}");
        var envelope = new XDocument(new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tem", Tempuri.NamespaceName),
            new XElement(Soap + "Header"),
            new XElement(Soap + "Body", new XElement(Tempuri + "Consulta", new XElement(Tempuri + "expresionImpresa", expression)))));
        var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, StatusUrl) { Content = content };
            request.Headers.Add("SOAPAction", "http://tempuri.org/IConsultaCFDIService/Consulta");
            response = await _httpClientFactory.CreateClient(SatHttpClientName).SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("The SAT's status service did not answer: {Error}", ex.GetType().Name);
            throw new InvoicingGatewayUnavailableException("El SAT no respondió. Intenta de nuevo más tarde.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw Unavailable(response, default, "status");
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                var answer = XDocument.Parse(text);
                var state = answer.Descendants().FirstOrDefault(e => e.Name.LocalName == "Estado")?.Value.Trim();
                var cancellation = answer.Descendants().FirstOrDefault(e => e.Name.LocalName == "EstatusCancelacion")?.Value.Trim();
                return new CfdiSatStatus(
                    state switch
                    {
                        "Cancelado" => CfdiSatState.Cancelled,
                        "Vigente" => CfdiSatState.InForce,
                        _ => CfdiSatState.NotFound
                    },
                    string.Equals(cancellation, "Solicitud rechazada", StringComparison.OrdinalIgnoreCase));
            }
            catch (XmlException)
            {
                throw new InvoicingGatewayUnavailableException("El SAT no respondió el estado de la factura.");
            }
        }
    }

    /// <summary>
    /// The CFDI in SW's JSON: the SAT's attribute names, amounts as decimal text, the seal left for SW. A payment complement
    /// (Fase 28C) is worth zero in no currency, which takes no decimals, and carries its Pagos 2.0 complement.
    /// </summary>
    internal static JsonObject SwJson(CfdiDocument document)
    {
        string Amount(decimal value) => document.Currency == CfdiDocument.NoCurrency ? "0" : Money(value);
        var concepts = new JsonArray();
        foreach (var concept in document.Concepts)
        {
            var node = new JsonObject
            {
                ["ClaveProdServ"] = concept.ProductCode,
                ["Cantidad"] = Number(concept.Quantity),
                ["ClaveUnidad"] = concept.UnitCode,
                ["Descripcion"] = concept.Description.Length > 1000 ? concept.Description[..1000] : concept.Description,
                ["ValorUnitario"] = Number(concept.UnitValue),
                ["Importe"] = Amount(concept.Amount),
                ["ObjetoImp"] = concept.TaxObject
            };
            // The sale a global invoice's concept stands for (its folio).
            if (concept.Identification is { } identification)
                node["NoIdentificacion"] = identification;
            if (concept.AllTransfers.Count > 0)
            {
                node["Impuestos"] = new JsonObject
                {
                    ["Traslados"] = new JsonArray(concept.AllTransfers.Select(transfer => (JsonNode)Transfer(transfer)).ToArray())
                };
            }
            concepts.Add(node);
        }

        var json = new JsonObject
        {
            ["Version"] = "4.0",
            ["Serie"] = document.Serie,
            ["Folio"] = document.Folio,
            ["Fecha"] = document.Date,
            ["Sello"] = "",
            ["NoCertificado"] = "",
            ["Certificado"] = ""
        };
        // A payment complement declares neither: its payment says how it was paid.
        if (document.PaymentForm is { } paymentForm)
            json["FormaPago"] = paymentForm;
        if (document.PaymentMethod is { } paymentMethod)
            json["MetodoPago"] = paymentMethod;
        json["SubTotal"] = Amount(document.SubTotal);
        json["Moneda"] = document.Currency;
        json["Total"] = Amount(document.Total);
        json["TipoDeComprobante"] = document.VoucherType;
        json["Exportacion"] = document.Exportation;
        json["LugarExpedicion"] = document.ExpeditionPostalCode;

        // The period a global invoice covers, where the SAT's schema puts it: before the issuer.
        if (document.GlobalInformation is { } global)
        {
            json["InformacionGlobal"] = new JsonObject
            {
                ["Periodicidad"] = global.Periodicity,
                ["Meses"] = global.Months,
                ["Año"] = global.Year.ToString(CultureInfo.InvariantCulture)
            };
        }

        // The invoice a credit note lowers (Fase 28D), where the SAT's schema puts it: after InformacionGlobal, before the
        // issuer. The node repeats per relation type, and each one lists its CFDI.
        if (document.Related is { } related)
        {
            json["CfdiRelacionados"] = new JsonArray(new JsonObject
            {
                ["TipoRelacion"] = related.RelationType,
                ["CfdiRelacionado"] = new JsonArray(related.Uuids.Select(uuid => (JsonNode)new JsonObject { ["UUID"] = uuid }).ToArray())
            });
        }

        json["Emisor"] = new JsonObject
        {
            ["Rfc"] = document.Issuer.Rfc,
            ["Nombre"] = document.Issuer.Name.ToUpperInvariant(),
            ["RegimenFiscal"] = document.Issuer.FiscalRegime
        };
        json["Receptor"] = new JsonObject
        {
            ["Rfc"] = document.Receiver.Rfc,
            ["Nombre"] = document.Receiver.Name,
            ["DomicilioFiscalReceptor"] = document.Receiver.PostalCode,
            ["RegimenFiscalReceptor"] = document.Receiver.FiscalRegime,
            ["UsoCFDI"] = document.Receiver.CfdiUse
        };
        json["Conceptos"] = concepts;

        // The invoice's taxes, when a concept has them; the total transferred only when some IVA is charged (exempt
        // transfers carry no amount).
        if (document.Transfers.Count > 0)
        {
            var taxes = new JsonObject();
            if (document.Transfers.Any(transfer => transfer.Amount is not null))
                taxes["TotalImpuestosTrasladados"] = Money(document.TotalTransferred);
            taxes["Traslados"] = new JsonArray(document.Transfers.Select(transfer => (JsonNode)Transfer(transfer)).ToArray());
            json["Impuestos"] = taxes;
        }

        if (document.Payment is { } payment)
            json["Complemento"] = new JsonObject { ["Any"] = new JsonArray(new JsonObject { ["Pago20:Pagos"] = Payments(payment) }) };
        return json;
    }

    /// <summary>
    /// Pagos 2.0 with one payment of one invoice, in pesos: its taxes (TrasladosDR and TrasladosP) are the same, and its
    /// totals add them up per rate.
    /// </summary>
    private static JsonObject Payments(CfdiDocumentPayment payment)
    {
        var invoice = payment.Invoice;
        var related = new JsonObject
        {
            ["IdDocumento"] = invoice.Uuid,
            ["Serie"] = invoice.Serie,
            ["Folio"] = invoice.Folio,
            ["MonedaDR"] = "MXN",
            ["EquivalenciaDR"] = "1",
            ["NumParcialidad"] = invoice.Installment.ToString(CultureInfo.InvariantCulture),
            ["ImpSaldoAnt"] = Money(invoice.PreviousBalance),
            ["ImpPagado"] = Money(invoice.Paid),
            ["ImpSaldoInsoluto"] = Money(invoice.RemainingBalance),
            ["ObjetoImpDR"] = invoice.TaxObject
        };
        var pago = new JsonObject
        {
            ["FechaPago"] = payment.Date,
            ["FormaDePagoP"] = payment.PaymentForm,
            ["MonedaP"] = "MXN",
            ["TipoCambioP"] = "1",
            ["Monto"] = Money(payment.Amount),
            ["DoctoRelacionado"] = new JsonArray(related)
        };
        if (invoice.Transfers.Count > 0)
        {
            related["ImpuestosDR"] = new JsonObject
            {
                ["TrasladosDR"] = new JsonArray(invoice.Transfers.Select(t => (JsonNode)PaymentTransfer(t, "DR")).ToArray())
            };
            pago["ImpuestosP"] = new JsonObject
            {
                ["TrasladosP"] = new JsonArray(invoice.Transfers.Select(t => (JsonNode)PaymentTransfer(t, "P")).ToArray())
            };
        }

        var totals = new JsonObject();
        foreach (var transfer in invoice.Transfers)
        {
            if (transfer.Rate is not { } rate)
            {
                totals["TotalTrasladosBaseIVAExento"] = Money(transfer.Base);
                continue;
            }
            var name = (rate * 100m).ToString("0", CultureInfo.InvariantCulture);
            totals[$"TotalTrasladosBaseIVA{name}"] = Money(transfer.Base);
            totals[$"TotalTrasladosImpuestoIVA{name}"] = Money(transfer.Amount ?? 0m);
        }
        totals["MontoTotalPagos"] = Money(payment.Amount);

        return new JsonObject
        {
            ["Version"] = "2.0",
            ["Totales"] = totals,
            ["Pago"] = new JsonArray(pago)
        };
    }

    /// <summary>A transfer of a payment's invoice (suffix DR) or of the payment (suffix P).</summary>
    private static JsonObject PaymentTransfer(CfdiDocumentTransfer transfer, string suffix)
    {
        var node = new JsonObject
        {
            [$"Base{suffix}"] = Money(transfer.Base),
            [$"Impuesto{suffix}"] = transfer.Tax,
            [$"TipoFactor{suffix}"] = transfer.FactorType
        };
        if (transfer.Rate is { } rate)
            node[$"TasaOCuota{suffix}"] = rate.ToString("0.000000", CultureInfo.InvariantCulture);
        if (transfer.Amount is { } amount)
            node[$"Importe{suffix}"] = Money(amount);
        return node;
    }

    private static JsonObject Transfer(CfdiDocumentTransfer transfer)
    {
        var node = new JsonObject
        {
            ["Base"] = Money(transfer.Base),
            ["Impuesto"] = transfer.Tax,
            ["TipoFactor"] = transfer.FactorType
        };
        if (transfer.Rate is { } rate)
            node["TasaOCuota"] = rate.ToString("0.000000", CultureInfo.InvariantCulture);
        if (transfer.Amount is { } amount)
            node["Importe"] = Money(amount);
        return node;
    }

    /// <summary>An amount with at least its cents and every decimal it has (up to the six the SAT takes).</summary>
    private static string Money(decimal value) => value.ToString("0.00####", CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private async Task<StampedCfdi> StampedAsync(JsonNode? data, CancellationToken cancellationToken)
    {
        var xml = Text(data?["cfdi"]);
        // The v4 answer names the UUID; the one to a repeated customid (CFDI3307) brings only the TimbreFiscalDigital
        // («tfd») and an empty «cfdi», so the UUID is read from the stamp.
        var uuid = Text(data?["uuid"]);
        if (string.IsNullOrWhiteSpace(uuid))
            uuid = StampUuid(xml) ?? StampUuid(Text(data?["tfd"]));
        if (string.IsNullOrWhiteSpace(uuid))
            throw new InvoicingGatewayUnavailableException("El PAC no entregó el folio fiscal de la factura.");

        // A repeated key may give back only the stamp: the XML is read from what SW keeps.
        if (string.IsNullOrWhiteSpace(xml))
            xml = await StoredXmlAsync(uuid, cancellationToken);
        return new StampedCfdi(uuid, xml, StampedAt(xml) ?? _clock.UtcNow);
    }

    public async Task<StampedCfdi?> FindStampedAsync(string uuid, CancellationToken cancellationToken)
    {
        // SW answers a folio fiscal it does not have for this account (or keeps no longer) with no records, not an error.
        var xml = await StoredXmlAsync(uuid, cancellationToken, missingIsNull: true, refuseCancelled: true);
        if (xml is null)
            return null;
        var stampedUuid = StampUuid(xml);
        if (!string.Equals(stampedUuid, uuid, StringComparison.OrdinalIgnoreCase))
            throw new InvoicingGatewayUnavailableException("El PAC entregó otro comprobante. Intenta de nuevo en unos minutos.");
        return new StampedCfdi(stampedUuid!, xml, StampedAt(xml) ?? _clock.UtcNow);
    }

    private async Task<string> StoredXmlAsync(string uuid, CancellationToken cancellationToken)
        => (await StoredXmlAsync(uuid, cancellationToken, missingIsNull: false, refuseCancelled: false))!;

    /// <param name="refuseCancelled">A CFDI SW keeps as cancelled with the SAT is refused instead of read.</param>
    private async Task<string?> StoredXmlAsync(string uuid, CancellationToken cancellationToken, bool missingIsNull, bool refuseCancelled)
    {
        using var response = await SendAsync(
            HttpMethod.Get, $"{ApiUrl}/datawarehouse/v1/live/{Uri.EscapeDataString(uuid)}", "stored xml", cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Unavailable(response, default, "stored xml");
        var data = await ReadDataAsync(response, cancellationToken);
        var records = data?["records"] as JsonArray;
        if (missingIsNull && records is { Count: 0 })
            return null;
        // «Vigente» or «Cancelado», as the SAT has it.
        var status = Text(records?.FirstOrDefault()?["status"]);
        if (refuseCancelled && status is not null && !string.Equals(status, "Vigente", StringComparison.OrdinalIgnoreCase))
            throw new InvoicingGatewayRefusedException("El SAT tiene ese folio fiscal cancelado: no puede ser el timbre de este CFDI.");
        var url = Text(records?.FirstOrDefault()?["urlXml"]);
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var xmlUri) || xmlUri.Scheme != Uri.UriSchemeHttps)
            throw new InvoicingGatewayUnavailableException("El PAC todavía no entrega el XML de la factura.");

        try
        {
            // A signed link to SW's storage: it takes no token.
            using var xmlResponse = await _httpClientFactory.CreateClient(SatHttpClientName).GetAsync(xmlUri, cancellationToken);
            if (!xmlResponse.IsSuccessStatusCode)
                throw new InvoicingGatewayUnavailableException("El PAC todavía no entrega el XML de la factura.");
            return await xmlResponse.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex) && !cancellationToken.IsCancellationRequested)
        {
            throw new InvoicingGatewayUnavailableException("El PAC no respondió. Intenta de nuevo en unos minutos.", ex);
        }
    }

    /// <summary>The UUID of the TimbreFiscalDigital in a stamped CFDI, or in the stamp alone.</summary>
    private static string? StampUuid(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;
        try
        {
            var root = XDocument.Parse(xml).Root;
            var stamp = root?.Name == StampNamespace + "TimbreFiscalDigital"
                ? root
                : root?.Descendants(StampNamespace + "TimbreFiscalDigital").FirstOrDefault();
            var uuid = stamp?.Attribute("UUID")?.Value.Trim();
            return string.IsNullOrEmpty(uuid) ? null : uuid;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>The TimbreFiscalDigital's FechaTimbrado, written in Mexico City's time.</summary>
    private static DateTimeOffset? StampedAt(string xml)
    {
        try
        {
            var stamp = XDocument.Parse(xml).Descendants(StampNamespace + "TimbreFiscalDigital").FirstOrDefault();
            if (stamp?.Attribute("FechaTimbrado")?.Value is not { } value
                || !DateTime.TryParseExact(value, CfdiDocument.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                return null;
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }
        catch (Exception ex) when (ex is XmlException or TimeZoneNotFoundException)
        {
            return null;
        }
    }

    /// <summary>SW answers a key it already stamped with 307 (the same CFDI) or CFDI3307 (the same customid).</summary>
    private static bool IsAlreadyStamped(string? code) => code is "307" or "CFDI3307";

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string operation,
        HttpContent? content = null,
        Action<HttpRequestMessage>? configure = null,
        CancellationToken cancellationToken = default)
    {
        using var body = content;
        var token = await TokenAsync(cancellationToken);
        try
        {
            var response = await SendOnceAsync(method, url, content, configure, token, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized || !string.IsNullOrWhiteSpace(_options.Token))
                return response;

            // The token expired early: SW did nothing with it, so asking again with a new one is safe.
            response.Dispose();
            ForgetToken(token);
            token = await TokenAsync(cancellationToken);
            return await SendOnceAsync(method, url, content, configure, token, cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("SW did not answer the {Operation}: {Error}", operation, ex.GetType().Name);
            throw new InvoicingGatewayUnavailableException("El PAC no respondió. Intenta de nuevo en unos minutos.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method, string url, HttpContent? content, Action<HttpRequestMessage>? configure, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        if (content is not null)
            request.Content = await CloneAsync(content, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        configure?.Invoke(request);
        return await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
    }

    /// <summary>A request's content can be sent once: a second attempt sends a copy.</summary>
    private static async Task<HttpContent> CloneAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var copy = new ByteArrayContent(await content.ReadAsByteArrayAsync(cancellationToken));
        foreach (var header in content.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }

    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SW Sapien is not configured.");
        if (!string.IsNullOrWhiteSpace(_options.Token))
            return _options.Token;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && _clock.UtcNow < _tokenExpiresAt - TokenMargin)
                return _token;

            using var response = await _httpClientFactory.CreateClient(HttpClientName).PostAsJsonAsync(
                $"{ServicesUrl}/v2/security/authenticate", new { user = _options.User, password = _options.Password }, cancellationToken);
            var data = response.IsSuccessStatusCode ? await ReadDataAsync(response, cancellationToken) : null;
            var token = Text(data?["token"]);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogError("SW did not authenticate the platform's account: {Status}", (int)response.StatusCode);
                throw new InvoicingGatewayUnavailableException("No se pudo timbrar por ahora. Intenta de nuevo en unos minutos.");
            }

            var expires = data?["expires_in"] is JsonValue value && value.TryGetValue<long>(out var epoch)
                ? DateTimeOffset.FromUnixTimeSeconds(epoch)
                : _clock.UtcNow.AddHours(2);
            _token = token;
            _tokenExpiresAt = expires;
            return token;
        }
        catch (Exception ex) when (IsTransient(ex) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("SW did not answer the authentication: {Error}", ex.GetType().Name);
            throw new InvoicingGatewayUnavailableException("El PAC no respondió. Intenta de nuevo en unos minutos.", ex);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void ForgetToken(string token)
    {
        if (_token == token)
            _token = null;
    }

    private InvoicingGatewayUnavailableException Unavailable(HttpResponseMessage response, SwError error, string operation)
    {
        _logger.LogWarning("SW failed the {Operation}: {Status} {Code}", operation, (int)response.StatusCode, error.Code);
        return new InvoicingGatewayUnavailableException("El PAC no respondió. Intenta de nuevo en unos minutos.");
    }

    private static async Task<JsonNode?> ReadDataAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return root?["data"];
        }
        catch (JsonException)
        {
            throw new InvoicingGatewayUnavailableException("El PAC respondió algo que no se pudo leer.");
        }
    }

    /// <summary>SW's error: its code (the start of the message, «CFDI40119», «307»…), its text and any data it carries.</summary>
    private static async Task<SwError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new SwError(null, "El PAC no aceptó la solicitud.", null);
        }

        try
        {
            var root = JsonNode.Parse(text);
            var message = Text(root?["message"])?.Trim();
            var detail = Text(root?["messageDetail"])?.Trim();
            var code = message?.Split([' ', '.', '-'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var full = string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}";
            return new SwError(code is { Length: > 40 } ? code[..40] : code, Clip(full) ?? "El PAC no aceptó la solicitud.", root?["data"]);
        }
        catch (JsonException)
        {
            return new SwError(null, "El PAC no aceptó la solicitud.", null);
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? Clip(string? text) => text is { Length: > 480 } ? text[..480] : text;

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or BrokenCircuitException or TimeoutRejectedException;

    private static string Base(string? configured, string fallback)
        => (string.IsNullOrWhiteSpace(configured) ? fallback : configured).TrimEnd('/');

    private readonly record struct SwError(string? Code, string Text, JsonNode? Data);
}

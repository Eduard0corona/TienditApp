using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>
/// Reads the XML of a CFDI de Retenciones e información de pagos 2.0 that a delivery platform issued to the business
/// (Fase 22J, ADR 0042): its folio, who issued it and to whom, the period and what it withheld, by tax. It reads what the
/// voucher says and nothing else: it neither checks its seal nor asks the SAT whether it is in force.
/// </summary>
public static class PlatformWithholdingXml
{
    /// <summary>The largest XML read, in characters: a monthly one of a platform is a few KB; a weekly one with every service, tens.</summary>
    public const int MaxLength = 512 * 1024;

    public const string Namespace = "http://www.sat.gob.mx/esquemas/retencionpago/2";
    public const string PreviousNamespace = "http://www.sat.gob.mx/esquemas/retencionpago/1";
    public const string StampNamespace = "http://www.sat.gob.mx/TimbreFiscalDigital";

    /// <summary>The clave of the SAT for the services of digital platforms (catálogo c_CveRetenc).</summary>
    public const string PlatformServicesKey = "26";
    public const string IsrTax = "001";
    public const string VatTax = "002";

    public const string EmptyMessage = "Sube el XML del CFDI de retenciones.";
    public const string TooLongMessage = "El archivo pasa de 512 KB: un CFDI de retenciones es mucho más chico. Revisa que sea el XML.";
    public const string NotXmlMessage = "El archivo no es un XML válido: sube el XML del CFDI de retenciones, no el PDF.";
    public const string NotRetencionesMessage = "El archivo no es un CFDI de retenciones.";
    public const string InvoiceMessage =
        "Es un CFDI de ingreso (una factura), no de retenciones: sube el CFDI de retenciones que la plataforma te emite cada mes o cada semana.";
    public const string PreviousVersionMessage =
        "Es un CFDI de retenciones 1.0. TienditApp lee la versión 2.0, la que emiten las plataformas desde 2022.";
    public const string VersionMessage = "El CFDI de retenciones no es de la versión 2.0.";
    public const string ForeignReceiverMessage = "El CFDI va a un receptor extranjero: TienditApp lee los que van a un RFC de México.";
    public const string MissingFieldMessage = "Al CFDI le falta un dato obligatorio o trae uno que no se puede leer:";

    private static readonly XNamespace Retenciones = Namespace;
    private static readonly XNamespace Stamp = StampNamespace;

    /// <summary>What the XML says, or why it cannot be read.</summary>
    public sealed record Reading(PlatformWithholdingCertificateData? Data, string? Error)
    {
        public static Reading Refused(string error) => new(null, error);
    }

    public static string NotPlatformServicesMessage(string key)
        => $"El CFDI de retenciones no es por servicios de plataformas tecnológicas (clave 26): trae la clave {key}.";

    public static Reading Read(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return Reading.Refused(EmptyMessage);
        if (xml.Length > MaxLength)
            return Reading.Refused(TooLongMessage);

        XDocument document;
        try
        {
            // No DTD and nothing resolved: an XML that points elsewhere or expands entities is not read at all.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxLength,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };
            // A byte-order mark or a blank line before the declaration, as a file read in the browser can bring, is not part of it.
            using var reader = XmlReader.Create(new StringReader(xml.TrimStart('\uFEFF', ' ', '\t', '\r', '\n')), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return Reading.Refused(NotXmlMessage);
        }

        var root = document.Root!;
        if (root.Name.LocalName == "Comprobante" && root.Name.NamespaceName.StartsWith("http://www.sat.gob.mx/cfd/", StringComparison.Ordinal))
            return Reading.Refused(InvoiceMessage);
        if (root.Name.LocalName == "Retenciones" && root.Name.NamespaceName == PreviousNamespace)
            return Reading.Refused(PreviousVersionMessage);
        if (root.Name != Retenciones + "Retenciones")
            return Reading.Refused(NotRetencionesMessage);
        if ((string?)root.Attribute("Version") != "2.0")
            return Reading.Refused(VersionMessage);

        var key = ((string?)root.Attribute("CveRetenc"))?.Trim();
        if (string.IsNullOrEmpty(key))
            return Missing("CveRetenc");
        if (key != PlatformServicesKey)
            return Reading.Refused(NotPlatformServicesMessage(key));

        var issuer = root.Element(Retenciones + "Emisor");
        var receiver = root.Element(Retenciones + "Receptor");
        var period = root.Element(Retenciones + "Periodo");
        var totals = root.Element(Retenciones + "Totales");
        if (issuer is null)
            return Missing("Emisor");
        if (receiver is null)
            return Missing("Receptor");
        if (period is null)
            return Missing("Periodo");
        if (totals is null)
            return Missing("Totales");

        if ((string?)receiver.Attribute("NacionalidadR") == "Extranjero")
            return Reading.Refused(ForeignReceiverMessage);
        var receiverRfc = (string?)receiver.Element(Retenciones + "Nacional")?.Attribute("RfcR");
        if (string.IsNullOrWhiteSpace(receiverRfc))
            return Missing("RfcR");

        var issuerRfc = (string?)issuer.Attribute("RfcE");
        if (string.IsNullOrWhiteSpace(issuerRfc))
            return Missing("RfcE");
        // The name is required since 2.0; without it, its RFC still says who issued it.
        var issuerName = (string?)issuer.Attribute("NomDenRazSocE");
        if (string.IsNullOrWhiteSpace(issuerName))
            issuerName = issuerRfc;

        if (!TryInt(period, "MesIni", out var monthFrom))
            return Missing("MesIni");
        if (!TryInt(period, "MesFin", out var monthTo))
            return Missing("MesFin");
        if (!TryInt(period, "Ejercicio", out var year))
            return Missing("Ejercicio");
        if (!TryDate(root, "FechaExp", out var issuedOn))
            return Missing("FechaExp");

        if (!TryAmount(totals, "MontoTotOperacion", out var operationTotal))
            return Missing("MontoTotOperacion");
        if (!TryAmount(totals, "MontoTotRet", out var totalWithheld))
            return Missing("MontoTotRet");

        // Each tax withheld adds up as written and is rounded to cents once; its base counts only when every line of the tax
        // writes it.
        decimal isrWithheld = 0m, vatWithheld = 0m;
        decimal? isrBase = null, vatBase = null;
        bool isrBaseComplete = true, vatBaseComplete = true;
        foreach (var line in totals.Elements(Retenciones + "ImpRetenidos"))
        {
            var tax = ((string?)line.Attribute("ImpuestoRet"))?.Trim();
            if (!TryAmount(line, "MontoRet", out var amount))
                return Missing("MontoRet");
            decimal? lineBase = null;
            if (line.Attribute("BaseRet") is not null)
            {
                if (!TryAmount(line, "BaseRet", out var parsedBase))
                    return Missing("BaseRet");
                lineBase = parsedBase;
            }

            if (tax == IsrTax)
            {
                isrWithheld += amount;
                (isrBase, isrBaseComplete) = AddBase(isrBase, isrBaseComplete, lineBase);
            }
            else if (tax == VatTax)
            {
                vatWithheld += amount;
                (vatBase, vatBaseComplete) = AddBase(vatBase, vatBaseComplete, lineBase);
            }
        }

        // The timbre may come in any of its complements.
        var stamp = root.Elements(Retenciones + "Complemento").Elements(Stamp + "TimbreFiscalDigital").FirstOrDefault();
        if (!Guid.TryParse((string?)stamp?.Attribute("UUID"), out var uuid) || uuid == Guid.Empty)
            return Reading.Refused(PlatformWithholdingCertificate.UuidRequiredMessage);

        // The one it replaces, when the platform cancelled a CFDI and issued this one in its place (TipoRelacion 04).
        Guid? replaces = null;
        var related = root.Element(Retenciones + "CfdiRetenRelacionados");
        if (related is not null)
        {
            if (!Guid.TryParse((string?)related.Attribute("UUID"), out var relatedUuid) || relatedUuid == Guid.Empty)
                return Missing("CfdiRetenRelacionados UUID");
            replaces = relatedUuid;
        }

        return new Reading(
            new PlatformWithholdingCertificateData(
                Uuid: uuid,
                IssuerRfc: issuerRfc,
                IssuerName: issuerName,
                ReceiverRfc: receiverRfc,
                Year: year,
                MonthFrom: monthFrom,
                MonthTo: monthTo,
                IssuedOn: issuedOn,
                OperationTotal: Cents(operationTotal),
                IsrBase: isrBaseComplete ? Cents(isrBase) : null,
                IsrWithheld: Cents(isrWithheld),
                VatBase: vatBaseComplete ? Cents(vatBase) : null,
                VatWithheld: Cents(vatWithheld),
                TotalWithheld: Cents(totalWithheld),
                ReplacesUuid: replaces),
            null);
    }

    private static Reading Missing(string field) => Reading.Refused($"{MissingFieldMessage} {field}.");

    private static (decimal? Sum, bool Complete) AddBase(decimal? sum, bool complete, decimal? lineBase)
        => lineBase is { } value && complete ? ((sum ?? 0m) + value, true) : (null, false);

    private static bool TryInt(XElement element, string attribute, out int value)
        => int.TryParse((string?)element.Attribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>An amount as the SAT writes it: digits and a point, never a sign or thousands; as many decimals as it writes.</summary>
    private static bool TryAmount(XElement element, string attribute, out decimal value)
        => decimal.TryParse((string?)element.Attribute(attribute), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// To cents, half away from zero, as the SAT rounds an amount to the decimals of its currency: once per figure, after
    /// adding up its lines as written.
    /// </summary>
    private static decimal Cents(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static decimal? Cents(decimal? amount) => amount is { } value ? Cents(value) : null;

    /// <summary>
    /// When it was issued, as written: the 2.0 writes the time of its place without a zone; one with a zone keeps the
    /// time it says, without converting it.
    /// </summary>
    private static bool TryDate(XElement element, string attribute, out DateTime value)
    {
        var text = (string?)element.Attribute(attribute);
        if (DateTime.TryParseExact(text, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            return true;
        if (DateTimeOffset.TryParseExact(text, "yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var withZone))
        {
            value = withZone.DateTime;
            return true;
        }
        return false;
    }
}

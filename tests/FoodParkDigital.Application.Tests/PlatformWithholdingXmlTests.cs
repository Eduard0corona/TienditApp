using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The XML of a CFDI de retenciones 2.0 of a platform (Fase 22J): what it withheld by tax, from the folio of its timbre, and
/// a clear reason for anything that is not one: an invoice, a 1.0, another clave, a PDF or an XML that points elsewhere.
/// </summary>
public class PlatformWithholdingXmlTests
{
    [Fact]
    public void Read_ShouldTakeWhatTheVoucherSays_AddingUpEachTax()
    {
        var reading = PlatformWithholdingXml.Read(Xml());

        Assert.Null(reading.Error);
        var data = reading.Data!;
        Assert.Equal(Guid.Parse("7874D580-DA5B-558F-B8EC-D92C57197242"), data.Uuid);
        Assert.Equal(("UBV121024TN8", "UBER B.V.", "CLA150101AB1"), (data.IssuerRfc, data.IssuerName, data.ReceiverRfc));
        Assert.Equal((2026, 9, 9), (data.Year, data.MonthFrom, data.MonthTo));
        Assert.Equal(new DateTime(2026, 10, 2, 22, 28, 50), data.IssuedOn);
        Assert.Equal(
            (16269.76m, (decimal?)16269.76m, 406.74m, (decimal?)2603.16m, 1301.58m, 1708.32m),
            (data.OperationTotal, data.IsrBase, data.IsrWithheld, data.VatBase, data.VatWithheld, data.TotalWithheld));
    }

    [Fact]
    public void Read_ShouldAddUpSeveralLinesOfATax_AndKeepABaseOnlyWhenEveryLineWritesIt()
    {
        var reading = PlatformWithholdingXml.Read(Xml(withheld: """
            <retenciones:ImpRetenidos BaseRet="10000.00" ImpuestoRet="001" MontoRet="250.00" TipoPagoRet="03"/>
            <retenciones:ImpRetenidos BaseRet="6269.76" ImpuestoRet="001" MontoRet="156.74" TipoPagoRet="03"/>
            <retenciones:ImpRetenidos ImpuestoRet="002" MontoRet="1000.00" TipoPagoRet="01"/>
            <retenciones:ImpRetenidos BaseRet="1000.00" ImpuestoRet="002" MontoRet="301.58" TipoPagoRet="01"/>
            <retenciones:ImpRetenidos BaseRet="100.00" ImpuestoRet="003" MontoRet="8.00" TipoPagoRet="01"/>
            """, totalWithheld: "1716.32"));

        var data = reading.Data!;
        Assert.Equal((406.74m, (decimal?)16269.76m), (data.IsrWithheld, data.IsrBase));
        Assert.Equal((1301.58m, (decimal?)null), (data.VatWithheld, data.VatBase));
        // The IEPS counts only in the total.
        Assert.Equal(1716.32m, data.TotalWithheld);
    }

    [Fact]
    public void Read_WithoutWithholdings_OrWithoutTheIssuerName_ShouldStillReadIt()
    {
        var reading = PlatformWithholdingXml.Read(Xml(withheld: "", totalWithheld: "0.00", issuerName: null));

        var data = reading.Data!;
        Assert.Equal((0m, (decimal?)null, 0m, (decimal?)null, 0m), (data.IsrWithheld, data.IsrBase, data.VatWithheld, data.VatBase, data.TotalWithheld));
        Assert.Equal("UBV121024TN8", data.IssuerName);
    }

    [Fact]
    public void Read_ShouldRoundAnAmountWithMoreThanCents_AndKeepTheTimeOfADateWithAZone()
    {
        var reading = PlatformWithholdingXml.Read(Xml(
            withheld: """<retenciones:ImpRetenidos BaseRet="16269.760000" ImpuestoRet="001" MontoRet="406.745000" TipoPagoRet="03"/>""",
            totalWithheld: "406.745000",
            issuedOn: "2026-10-02T22:28:50-06:00"));

        var data = reading.Data!;
        Assert.Equal((16269.76m, 406.75m, 406.75m), (data.IsrBase!.Value, data.IsrWithheld, data.TotalWithheld));
        Assert.Equal(new DateTime(2026, 10, 2, 22, 28, 50), data.IssuedOn);
    }

    [Fact]
    public void Read_ShouldAddUpEachTaxAsWritten_AndRoundItOnce_SoAValidTotalIsNotShort()
    {
        // 10.006 + 20.006 = 30.012: each tax rounds up to 10.01 and 20.01, the total to 30.01; still a voucher it reads.
        var reading = PlatformWithholdingXml.Read(Xml(withheld: """
            <retenciones:ImpRetenidos BaseRet="400.240000" ImpuestoRet="001" MontoRet="10.006000" TipoPagoRet="03"/>
            <retenciones:ImpRetenidos BaseRet="40.012000" ImpuestoRet="002" MontoRet="20.006000" TipoPagoRet="01"/>
            """, totalWithheld: "30.012000"));

        var data = reading.Data!;
        Assert.Equal((10.01m, 20.01m, 30.01m), (data.IsrWithheld, data.VatWithheld, data.TotalWithheld));
        // The domain takes it: two cents short is rounding.
        Assert.Equal(30.01m, new PlatformWithholdingCertificate(Guid.NewGuid(), OrderChannel.UberEats, data, new DateOnly(2026, 10, 1)).TotalWithheld);

        // Two lines of a tax add up before rounding: 0.004 + 0.004 is one cent, not none.
        var lines = PlatformWithholdingXml.Read(Xml(withheld: """
            <retenciones:ImpRetenidos ImpuestoRet="001" MontoRet="0.004" TipoPagoRet="03"/>
            <retenciones:ImpRetenidos ImpuestoRet="001" MontoRet="0.004" TipoPagoRet="03"/>
            """, totalWithheld: "0.008")).Data!;
        Assert.Equal((0.01m, 0.01m), (lines.IsrWithheld, lines.TotalWithheld));
    }

    [Fact]
    public void Read_ShouldTakeTheCfdiItReplaces_AndTheTimbreFromAnyComplement()
    {
        var xml = Xml().Replace(
            """<retenciones:Emisor""",
            """<retenciones:CfdiRetenRelacionados TipoRelacion="04" UUID="11111111-2222-4333-8444-555555555555"/><retenciones:Emisor""");
        // The timbre in a second complement, after the one of the platforms' services.
        xml = xml.Replace("<retenciones:Complemento>", "<retenciones:Complemento><otro/></retenciones:Complemento><retenciones:Complemento>");

        var data = PlatformWithholdingXml.Read(xml).Data!;

        Assert.Equal(Guid.Parse("11111111-2222-4333-8444-555555555555"), data.ReplacesUuid);
        Assert.Equal(Guid.Parse("7874D580-DA5B-558F-B8EC-D92C57197242"), data.Uuid);
        Assert.Null(PlatformWithholdingXml.Read(Xml()).Data!.ReplacesUuid);
        Assert.Equal($"{PlatformWithholdingXml.MissingFieldMessage} CfdiRetenRelacionados UUID.", PlatformWithholdingXml.Read(Xml().Replace(
            """<retenciones:Emisor""", """<retenciones:CfdiRetenRelacionados TipoRelacion="04"/><retenciones:Emisor""")).Error);
    }

    [Fact]
    public void Read_ShouldSkipAByteOrderMarkOrBlankLinesBeforeTheDeclaration()
    {
        var reading = PlatformWithholdingXml.Read("\uFEFF\r\n  " + Xml());

        Assert.Null(reading.Error);
        Assert.Equal(406.74m, reading.Data!.IsrWithheld);
    }

    [Fact]
    public void Read_ShouldSayWhatTheFileIs_WhenItIsNotAVoucherItReads()
    {
        Refused(PlatformWithholdingXml.EmptyMessage, "  ");
        Refused(PlatformWithholdingXml.EmptyMessage, null);
        Refused(PlatformWithholdingXml.TooLongMessage, new string('x', PlatformWithholdingXml.MaxLength + 1));
        Refused(PlatformWithholdingXml.NotXmlMessage, "%PDF-1.7 este es el PDF");
        Refused(PlatformWithholdingXml.NotXmlMessage, "<retenciones:Retenciones");
        Refused(PlatformWithholdingXml.InvoiceMessage, """
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" Total="100.00"/>
            """);
        Refused(PlatformWithholdingXml.PreviousVersionMessage, """
            <retenciones:Retenciones xmlns:retenciones="http://www.sat.gob.mx/esquemas/retencionpago/1" Version="1.0" CveRetenc="26"/>
            """);
        Refused(PlatformWithholdingXml.NotRetencionesMessage, "<factura><total>100</total></factura>");
        Refused(PlatformWithholdingXml.VersionMessage, Xml().Replace("Version=\"2.0\"", "Version=\"2.1\""));
        Refused(PlatformWithholdingXml.NotPlatformServicesMessage("14"), Xml().Replace("CveRetenc=\"26\"", "CveRetenc=\"14\""));
        Refused(PlatformWithholdingXml.ForeignReceiverMessage, Xml().Replace(
            """<retenciones:Receptor NacionalidadR="Nacional"><retenciones:Nacional RfcR="CLA150101AB1" NomDenRazSocR="COCINA LA ABUELA"/></retenciones:Receptor>""",
            """<retenciones:Receptor NacionalidadR="Extranjero"><retenciones:Extranjero NomDenRazSocR="OTRA"/></retenciones:Receptor>"""));
        Refused(PlatformWithholdingCertificate.UuidRequiredMessage, Xml(uuid: null));
        Refused(PlatformWithholdingCertificate.UuidRequiredMessage, Xml(uuid: "no-es-un-folio"));
    }

    [Fact]
    public void Read_ShouldNameTheDataItMisses()
    {
        Refused($"{PlatformWithholdingXml.MissingFieldMessage} Periodo.", Xml().Replace(
            """<retenciones:Periodo MesIni="09" MesFin="09" Ejercicio="2026"/>""", ""));
        Refused($"{PlatformWithholdingXml.MissingFieldMessage} Ejercicio.", Xml().Replace("Ejercicio=\"2026\"", "Ejercicio=\"dos mil\""));
        Refused($"{PlatformWithholdingXml.MissingFieldMessage} RfcR.", Xml().Replace(" RfcR=\"CLA150101AB1\"", ""));
        Refused($"{PlatformWithholdingXml.MissingFieldMessage} FechaExp.", Xml(issuedOn: "02/10/2026"));
        // A sign or thousands are not how the SAT writes an amount.
        Refused($"{PlatformWithholdingXml.MissingFieldMessage} MontoTotRet.", Xml(totalWithheld: "-1708.32"));
        Refused($"{PlatformWithholdingXml.MissingFieldMessage} MontoTotOperacion.", Xml().Replace("16269.76\" MontoTotGrav", "16,269.76\" MontoTotGrav"));
        Refused($"{PlatformWithholdingXml.MissingFieldMessage} MontoRet.", Xml().Replace("MontoRet=\"406.74\"", "MontoRet=\"\""));
    }

    [Fact]
    public void Read_ShouldNotReadADocumentType_NorAnythingItPointsTo()
    {
        // An entity defined in the file, or one that reads another file or address: neither is expanded nor fetched.
        Refused(PlatformWithholdingXml.NotXmlMessage, """
            <?xml version="1.0"?>
            <!DOCTYPE r [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <r>&b;</r>
            """);
        Refused(PlatformWithholdingXml.NotXmlMessage, """
            <?xml version="1.0"?>
            <!DOCTYPE r [<!ENTITY x SYSTEM "file:///etc/passwd">]>
            <r>&x;</r>
            """);
    }

    private static void Refused(string message, string? xml)
    {
        var reading = PlatformWithholdingXml.Read(xml);
        Assert.Null(reading.Data);
        Assert.Equal(message, reading.Error);
    }

    /// <summary>A CFDI de retenciones 2.0 like the ones Uber issues: September 2026, 2.5 % of ISR and half the IVA.</summary>
    internal static string Xml(
        string? uuid = "7874D580-DA5B-558F-B8EC-D92C57197242",
        string withheld = """
            <retenciones:ImpRetenidos BaseRet="16269.76" ImpuestoRet="001" MontoRet="406.74" TipoPagoRet="03"/>
            <retenciones:ImpRetenidos BaseRet="2603.16" ImpuestoRet="002" MontoRet="1301.58" TipoPagoRet="01"/>
            """,
        string totalWithheld = "1708.32",
        string? issuerName = "UBER B.V.",
        string issuedOn = "2026-10-02T22:28:50",
        string receiverRfc = "CLA150101AB1",
        int monthFrom = 9,
        int monthTo = 9,
        int year = 2026)
    {
        var name = issuerName is null ? "" : $" NomDenRazSocE=\"{issuerName}\"";
        var stamp = uuid is null
            ? ""
            : $"""<tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="1.1" UUID="{uuid}" FechaTimbrado="{issuedOn}" RfcProvCertif="CVD110412TF6" SelloCFD="x" NoCertificadoSAT="00001000000504204441" SelloSAT="y"/>""";
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <retenciones:Retenciones xmlns:retenciones="http://www.sat.gob.mx/esquemas/retencionpago/2" Version="2.0" FolioInt="5343935" FechaExp="{issuedOn}" CveRetenc="26" LugarExpRetenc="03810" NoCertificado="00001000000500593299" Certificado="x" Sello="y">
              <retenciones:Emisor RfcE="UBV121024TN8"{name} RegimenFiscalE="601"/>
              <retenciones:Receptor NacionalidadR="Nacional"><retenciones:Nacional RfcR="{receiverRfc}" NomDenRazSocR="COCINA LA ABUELA"/></retenciones:Receptor>
              <retenciones:Periodo MesIni="{monthFrom:00}" MesFin="{monthTo:00}" Ejercicio="{year}"/>
              <retenciones:Totales MontoTotOperacion="16269.76" MontoTotGrav="16269.76" MontoTotExent="0.00" MontoTotRet="{totalWithheld}">
                {withheld}
              </retenciones:Totales>
              <retenciones:Complemento>
                <!-- Servicios de plataformas tecnológicas: TienditApp no lo lee. -->
                {stamp}
              </retenciones:Complemento>
            </retenciones:Retenciones>
            """;
    }
}

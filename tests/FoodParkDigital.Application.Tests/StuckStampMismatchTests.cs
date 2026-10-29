using FoodParkDigital.Application.Invoicing;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// «Ya se timbró» (Fase 28K, ADR 0056) keeps the XML of the folio fiscal a person copied from the SAT's portal only if it is
/// this CFDI's: the same serie and folio, date, type, total, issuer and receiver.
/// </summary>
public class StuckStampMismatchTests
{
    private static readonly CfdiDocument Document = new()
    {
        Serie = "F",
        Folio = "12",
        Date = "2026-10-22T10:30:00",
        ExpeditionPostalCode = "26015",
        SubTotal = 172.41m,
        TotalTransferred = 27.59m,
        Total = 200m,
        Issuer = new CfdiDocumentIssuer("EKU9003173C9", "ESCUELA KEMPER URGATE", "601"),
        Receiver = new CfdiDocumentReceiver("XIQB891116QE4", "KARLA FUENTE NOLASCO", "26015", "612", "G03"),
        Concepts = [],
        Transfers = []
    };

    private static string Xml(
        string serie = "F", string folio = "12", string date = "2026-10-22T10:30:00", string type = "I", string total = "200.00",
        string issuer = "EKU9003173C9", string receiver = "XIQB891116QE4")
        => $"""
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Serie="{serie}" Folio="{folio}" Fecha="{date}" Total="{total}" TipoDeComprobante="{type}">
              <cfdi:Emisor Rfc="{issuer}" />
              <cfdi:Receptor Rfc="{receiver}" />
            </cfdi:Comprobante>
            """;

    [Fact]
    public void XmlOfThisCfdi_ShouldMatch_WhateverTheCaseOfItsSerieAndRfcs()
    {
        Assert.Null(StuckStamps.Mismatch(Document, Xml()));
        Assert.Null(StuckStamps.Mismatch(Document, Xml(serie: "f", issuer: "eku9003173c9", receiver: "xiqb891116qe4", total: "200")));
    }

    [Theory]
    [InlineData("Serie", "G", null, null, null, null, null, null)]
    [InlineData("Folio", null, "13", null, null, null, null, null)]
    // The same serie and folio of another business, or issued again another day, is not this CFDI.
    [InlineData("Fecha", null, null, "2026-10-23T10:30:00", null, null, null, null)]
    [InlineData("TipoDeComprobante", null, null, null, "E", null, null, null)]
    [InlineData("Total", null, null, null, null, "200.01", null, null)]
    [InlineData("Emisor", null, null, null, null, null, "AAA010101AAA", null)]
    [InlineData("Receptor", null, null, null, null, null, null, "XAXX010101000")]
    public void XmlOfAnotherCfdi_ShouldSayWhatDiffers(
        string differs, string? serie, string? folio, string? date, string? type, string? total, string? issuer, string? receiver)
    {
        var xml = Xml(
            serie ?? "F", folio ?? "12", date ?? "2026-10-22T10:30:00", type ?? "I", total ?? "200.00",
            issuer ?? "EKU9003173C9", receiver ?? "XIQB891116QE4");

        Assert.Equal(differs, StuckStamps.Mismatch(Document, xml));
    }

    [Fact]
    public void WhatIsNotACfdi_ShouldNotMatch()
    {
        Assert.Equal("XML", StuckStamps.Mismatch(Document, "no es XML"));
        Assert.Equal("Comprobante", StuckStamps.Mismatch(Document, "<Retenciones Serie=\"F\" Folio=\"12\" />"));
    }
}

using System.Collections.Concurrent;
using System.Text;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The PAC for the API in process (Fase 28A): it keeps each CSD, stamps each key once (the same stamp for a repeated key),
/// renders a PDF and takes every cancellation.
/// </summary>
public sealed class TestInvoicingGateway : IInvoicingGateway
{
    private readonly ConcurrentDictionary<Guid, StampedCfdi> _stamped = new();

    public bool IsConfigured => true;
    public bool Sandbox => false;

    public ConcurrentQueue<(string IssuerRfc, string Uuid)> Cancellations { get; } = new();

    public Task SaveCertificateAsync(byte[] certificate, byte[] privateKey, string password, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RemoveCertificateAsync(string certificateNumber, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<StampedCfdi> StampAsync(CfdiDocument document, Guid idempotencyKey, CancellationToken cancellationToken)
        => Task.FromResult(_stamped.GetOrAdd(idempotencyKey, _ =>
        {
            var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
            return new StampedCfdi(
                uuid,
                $"<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" Serie=\"{document.Serie}\" Folio=\"{document.Folio}\" "
                + $"Sello=\"AAAABBBBSELLO123\"><cfdi:Complemento UUID=\"{uuid}\" /></cfdi:Comprobante>",
                DateTimeOffset.UtcNow);
        }));

    public Task<byte[]> RenderPdfAsync(string stampedXml, CancellationToken cancellationToken)
        => Task.FromResult(Encoding.ASCII.GetBytes("%PDF-1.7"));

    public Task<CfdiCancellationOutcome> CancelAsync(
        string issuerRfc, string uuid, CfdiCancellationMotive motive, CancellationToken cancellationToken)
    {
        Cancellations.Enqueue((issuerRfc, uuid));
        return Task.FromResult(CfdiCancellationOutcome.Requested);
    }

    public Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new CfdiSatStatus(CfdiSatState.InForce, false));
}

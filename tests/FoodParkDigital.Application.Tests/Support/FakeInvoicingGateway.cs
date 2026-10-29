using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// The platform's PAC in the tests (Fase 28A): it stamps each key once, gives back the same stamp for a repeated key, and can
/// refuse, stay silent or lose its answer.
/// </summary>
internal sealed class FakeInvoicingGateway : IInvoicingGateway
{
    public const string SealTail = "SELLO123";

    public bool IsConfigured { get; set; } = true;
    public bool Sandbox { get; set; }
    /// <summary>Nothing answers.</summary>
    public bool Unavailable { get; set; }
    /// <summary>Stamping is refused with this message.</summary>
    public string? Refusal { get; set; }
    /// <summary>The next stamp is made, but its answer never arrives.</summary>
    public bool LoseNextAnswer { get; set; }
    public bool PdfUnavailable { get; set; }
    public CfdiCancellationOutcome CancelOutcome { get; set; } = CfdiCancellationOutcome.Requested;
    public string? CancelRefusal { get; set; }
    public CfdiSatStatus SatStatus { get; set; } = new(CfdiSatState.InForce, false);

    public List<(CfdiDocument Document, Guid Key)> StampRequests { get; } = [];
    public Dictionary<Guid, StampedCfdi> Stamped { get; } = [];
    public List<(byte[] Certificate, byte[] Key, string Password)> SavedCertificates { get; } = [];
    public List<string> RemovedCertificates { get; } = [];
    public List<(string IssuerRfc, string Uuid, CfdiCancellationMotive Motive)> Cancellations { get; } = [];
    /// <summary>The UUID each CFDI cancelled with motive 01 was told it is replaced by (Fase 28G).</summary>
    public Dictionary<string, string> Replacements { get; } = [];
    public List<CfdiStatusQuery> StatusQueries { get; } = [];
    public int PdfRenders { get; private set; }
    /// <summary>Every time a PDF was asked for, given or not.</summary>
    public int PdfRequests { get; private set; }

    public Task SaveCertificateAsync(byte[] certificate, byte[] privateKey, string password, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        SavedCertificates.Add((certificate, privateKey, password));
        return Task.CompletedTask;
    }

    public Task RemoveCertificateAsync(string certificateNumber, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        RemovedCertificates.Add(certificateNumber);
        return Task.CompletedTask;
    }

    public Task<StampedCfdi> StampAsync(CfdiDocument document, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        StampRequests.Add((document, idempotencyKey));
        if (Refusal is not null)
            throw new InvoicingGatewayRefusedException(Refusal);
        if (!Stamped.TryGetValue(idempotencyKey, out var stamped))
        {
            var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
            stamped = new StampedCfdi(
                uuid,
                $"<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" Serie=\"{document.Serie}\" Folio=\"{document.Folio}\" "
                + $"Total=\"{document.Total}\" TipoDeComprobante=\"{document.VoucherType}\" Sello=\"AAAABBBB{SealTail}\">"
                + $"<cfdi:Emisor Rfc=\"{document.Issuer.Rfc}\" /><cfdi:Receptor Rfc=\"{document.Receiver.Rfc}\" />"
                + $"<cfdi:Complemento UUID=\"{uuid}\" /></cfdi:Comprobante>",
                new DateTimeOffset(2026, 10, 4, 11, 0, 5, TimeSpan.FromHours(-6)));
            Stamped[idempotencyKey] = stamped;
        }
        if (LoseNextAnswer)
        {
            LoseNextAnswer = false;
            throw new InvoicingGatewayUnavailableException("El PAC no respondió.");
        }
        return Task.FromResult(stamped);
    }

    /// <summary>The folios fiscales a person brought from the SAT's portal, looked up (Fase 28K).</summary>
    public List<string> Lookups { get; } = [];

    public Task<StampedCfdi?> FindStampedAsync(string uuid, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        Lookups.Add(uuid);
        return Task.FromResult(Stamped.Values.FirstOrDefault(s => string.Equals(s.Uuid, uuid, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<byte[]> RenderPdfAsync(string stampedXml, CancellationToken cancellationToken)
    {
        PdfRequests++;
        ThrowIfUnavailable();
        if (PdfUnavailable)
            throw new InvoicingGatewayUnavailableException("El PDF no está listo.");
        PdfRenders++;
        return Task.FromResult(Encoding.ASCII.GetBytes("%PDF-1.7 " + stampedXml.Length));
    }

    public Task<CfdiCancellationOutcome> CancelAsync(
        string issuerRfc, string uuid, CfdiCancellationMotive motive, string? replacementUuid, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        if (CancelRefusal is not null)
            throw new InvoicingGatewayRefusedException(CancelRefusal);
        Cancellations.Add((issuerRfc, uuid, motive));
        if (replacementUuid is not null)
            Replacements[uuid] = replacementUuid;
        return Task.FromResult(CancelOutcome);
    }

    public Task<CfdiSatStatus> GetSatStatusAsync(CfdiStatusQuery query, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();
        StatusQueries.Add(query);
        return Task.FromResult(SatStatus);
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
            throw new InvoicingGatewayUnavailableException("El PAC no respondió.");
    }
}

/// <summary>The business's private files in memory: a key is written once, as the real storage does.</summary>
internal sealed class MemoryPrivateStorage : IPrivateFileStorage
{
    public Dictionary<string, (byte[] Content, string ContentType)> Files { get; } = [];

    /// <summary>The keys the storage fails to write, as when it does not answer.</summary>
    public Func<string, bool>? FailsToSave { get; set; }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (FailsToSave?.Invoke(key) == true)
            throw new IOException("The storage did not answer.");
        if (Files.ContainsKey(key))
            throw new IOException($"The file {key} exists.");
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[key] = (buffer.ToArray(), contentType);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream?>(Files.TryGetValue(key, out var file) ? new MemoryStream(file.Content) : null);

    public string Text(string key) => Encoding.UTF8.GetString(Files[key].Content);
}

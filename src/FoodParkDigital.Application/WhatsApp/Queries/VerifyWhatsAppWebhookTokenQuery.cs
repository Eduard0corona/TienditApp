using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.WhatsApp.Queries;

/// <summary>
/// Resolves the tenant that owns the verify token Meta sends in the webhook
/// subscription handshake. Returns the tenant id when the token matches.
/// </summary>
[AllowAnonymousRequest("Verificación pública del webhook de Meta; compara el token con el configurado.")]
public record VerifyWhatsAppWebhookTokenQuery(string VerifyToken) : IRequest<Result<Guid>>;

public class VerifyWhatsAppWebhookTokenQueryHandler
    : IRequestHandler<VerifyWhatsAppWebhookTokenQuery, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public VerifyWhatsAppWebhookTokenQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> Handle(
        VerifyWhatsAppWebhookTokenQuery request, CancellationToken cancellationToken)
    {
        // Pre-tenant scenario: the anonymous handshake only carries the token, so the
        // match runs in memory instead of as a SQL equality. Hashing gives fixed-length
        // inputs and every candidate is compared, so timing does not reveal partial matches.
        var candidates = await _context.WhatsAppConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.VerifyToken != null)
            .Select(c => new { c.TenantId, c.VerifyToken })
            .ToListAsync(cancellationToken);

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.VerifyToken));
        Guid? matchedTenantId = null;

        foreach (var candidate in candidates)
        {
            var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate.VerifyToken!));
            if (CryptographicOperations.FixedTimeEquals(providedHash, candidateHash))
                matchedTenantId ??= candidate.TenantId;
        }

        return matchedTenantId is { } tenantId
            ? Result.Success(tenantId)
            : Result.Failure<Guid>("Token de verificación inválido.");
    }
}

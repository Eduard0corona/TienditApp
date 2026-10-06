using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Audit;

/// <summary>Records the platform admin's requests on a business other than the one of their session.</summary>
public interface ISupportAccessAudit
{
    /// <summary>Records the request in the activity of <paramref name="tenantId"/>, the business set for it.</summary>
    /// <returns>False when that business does not exist: nothing is recorded, and the request must not run.</returns>
    Task<bool> RecordAsync(Guid tenantId, Guid sessionTenantId, string method, string path, CancellationToken cancellationToken);
}

/// <summary>
/// The X-Tenant-Id header lets the platform admin act on any business. Every request doing so is recorded in that
/// business's activity before it runs: who, from the business of their session, and the method and path (without the
/// query, which can carry searches). The owner sees who entered and what they did.
/// </summary>
public sealed class SupportAccessAudit : ISupportAccessAudit
{
    public const string Action = "SupportAccess";
    public const string EntityName = "Tenant";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public SupportAccessAudit(IApplicationDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<bool> RecordAsync(
        Guid tenantId,
        Guid sessionTenantId,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        if (!await _context.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken))
            return false;

        _context.AuditLogs.Add(new AuditLog(
            tenantId,
            EntityName,
            tenantId.ToString(),
            Action,
            _currentUser.UserName ?? _currentUser.UserId ?? "platform",
            JsonSerializer.Serialize(new
            {
                Method = method,
                Path = path,
                AdminUserId = _currentUser.UserId,
                SessionTenantId = sessionTenantId
            }),
            _clock.UtcNow));
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

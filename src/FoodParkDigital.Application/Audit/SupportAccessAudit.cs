using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Audit;

/// <summary>What became of a request of the platform admin on another business.</summary>
public enum SupportAccessOutcome
{
    /// <summary>The admin has an open access to the business: the request is recorded in its activity and runs.</summary>
    Recorded,
    /// <summary>The business does not exist: nothing is recorded, and the request does not run.</summary>
    BusinessNotFound,
    /// <summary>The admin has no access to the business, or closed it: the request does not run.</summary>
    AccessRequired,
    /// <summary>The admin's access to the business reached its time: the request does not run.</summary>
    AccessExpired
}

/// <summary>Lets the platform admin act on a business other than the one of their session, and records it.</summary>
public interface ISupportAccessAudit
{
    /// <summary>
    /// Checks the admin's open access to <paramref name="tenantId"/>, the business set for the request, and records the
    /// request in its activity. Only <see cref="SupportAccessOutcome.Recorded"/> lets the request run.
    /// </summary>
    Task<SupportAccessOutcome> RecordAsync(Guid tenantId, Guid sessionTenantId, string method, string path, CancellationToken cancellationToken);
}

/// <summary>
/// The X-Tenant-Id header lets the platform admin act on a business only while they have an open access to it (Fase 32G,
/// ADR 0099): opened with a reason and for a time. Every request doing so is recorded in that business's activity before
/// it runs: who, from the business of their session, the access and its reason, and the method and path (without the
/// query, which can carry searches). The owner sees who entered, why and what they did.
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

    public async Task<SupportAccessOutcome> RecordAsync(
        Guid tenantId,
        Guid sessionTenantId,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var adminUserId))
            return SupportAccessOutcome.AccessRequired;

        // The business of the request is already in scope: its accesses are read through the usual filter.
        var now = _clock.UtcNow;
        var access = await _context.SupportAccesses
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AdminUserId == adminUserId && a.EndedAt == null && a.ExpiresAt > now)
            .OrderByDescending(a => a.ExpiresAt)
            .Select(a => new { a.Id, a.Reason })
            .FirstOrDefaultAsync(cancellationToken);
        if (access is null)
            return await RefusalAsync(tenantId, adminUserId, cancellationToken);

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
                AdminUserId = adminUserId,
                SessionTenantId = sessionTenantId,
                SupportAccessId = access.Id,
                access.Reason
            }),
            now));
        await _context.SaveChangesAsync(cancellationToken);
        return SupportAccessOutcome.Recorded;
    }

    /// <summary>Why a request without an open access does not run: no such business, an access that expired, or none.</summary>
    private async Task<SupportAccessOutcome> RefusalAsync(Guid tenantId, Guid adminUserId, CancellationToken cancellationToken)
    {
        if (!await _context.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, cancellationToken))
            return SupportAccessOutcome.BusinessNotFound;

        // Their latest access, when not closed, ended by its time.
        var latestNotClosed = await _context.SupportAccesses
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.AdminUserId == adminUserId)
            .OrderByDescending(a => a.StartedAt)
            .Select(a => (bool?)(a.EndedAt == null))
            .FirstOrDefaultAsync(cancellationToken);
        return latestNotClosed == true ? SupportAccessOutcome.AccessExpired : SupportAccessOutcome.AccessRequired;
    }
}

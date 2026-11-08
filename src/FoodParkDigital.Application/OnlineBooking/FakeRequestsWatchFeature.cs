using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineBooking;

/// <summary>
/// What the platform watches of the requests its businesses mark as fake (Fase 30A, ADR 0081): which businesses mark the most,
/// and which phones several businesses marked. In the days Explorar counts (<see cref="ResponseTimes.WindowDays"/>).
/// </summary>
public sealed class FakeRequestsWatchDto
{
    public IReadOnlyList<FakeRequestsBusinessDto> Businesses { get; set; } = [];
    public IReadOnlyList<FakeRequestsPhoneDto> Phones { get; set; } = [];
    /// <summary>How many each list shows at most: the most first.</summary>
    public int Top { get; set; }
}

/// <summary>A business that marked requests as fake, with the rest of its requests online to compare.</summary>
public sealed class FakeRequestsBusinessDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    /// <summary>Still marked as fake.</summary>
    public int Marked { get; set; }
    /// <summary>Marked, and then taken back («No era falsa»).</summary>
    public int TakenBack { get; set; }
    /// <summary>
    /// Every request online of its customers that arrived in those days, and the ones marked in them that arrived before:
    /// never those of its team. So it is never fewer than <see cref="Marked"/> and <see cref="TakenBack"/> together.
    /// </summary>
    public int Requests { get; set; }
    public DateTimeOffset LastMarkedAt { get; set; }
}

/// <summary>A phone that two or more businesses marked as fake, said only by its last four digits.</summary>
public sealed class FakeRequestsPhoneDto
{
    /// <summary>Its last four digits: the platform compares phones, it does not read them.</summary>
    public string Ending { get; set; } = string.Empty;
    public IReadOnlyList<string> BusinessNames { get; set; } = [];
    public DateTimeOffset LastMarkedAt { get; set; }
}

[RequiresPlatformAdmin]
public record GetFakeRequestsWatchQuery : IRequest<FakeRequestsWatchDto>;

public sealed class GetFakeRequestsWatchQueryHandler : IRequestHandler<GetFakeRequestsWatchQuery, FakeRequestsWatchDto>
{
    /// <summary>How many businesses and phones each list shows at most.</summary>
    public const int Top = 20;

    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public GetFakeRequestsWatchQueryHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<FakeRequestsWatchDto> Handle(GetFakeRequestsWatchQuery request, CancellationToken cancellationToken)
    {
        var since = ResponseTimes.Since(_clock.UtcNow);
        // The platform reads every business's requests: its admin is no member of them, and watching them is its job. Read only.
        var marked = _context.Appointments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.Channel == AppointmentChannel.Online && a.MarkedFakeAt >= since);

        var byBusiness = await marked
            .GroupBy(a => a.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Marked = g.Count(a => a.UnmarkedFakeAt == null),
                TakenBack = g.Count(a => a.UnmarkedFakeAt != null),
                LastMarkedAt = g.Max(a => a.MarkedFakeAt)!.Value
            })
            .OrderByDescending(b => b.Marked)
            .ThenByDescending(b => b.LastMarkedAt)
            .ThenBy(b => b.TenantId)
            .Take(Top)
            .ToListAsync(cancellationToken);
        var ids = byBusiness.Select(b => b.TenantId).ToList();
        var requests = await _context.Appointments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.Channel == AppointmentChannel.Online && !a.FromTeam && (a.CreatedAt >= since || a.MarkedFakeAt >= since)
                && ids.Contains(a.TenantId))
            .GroupBy(a => a.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.TenantId, r => r.Count, cancellationToken);

        // A phone still marked by two businesses or more; its requests keep it as a number (MessagingPhone.ToKey).
        var stillMarked = marked.Where(a => a.UnmarkedFakeAt == null && a.RequestPhone != "");
        var phones = await stillMarked
            .GroupBy(a => a.RequestPhone)
            .Select(g => new
            {
                Phone = g.Key,
                Businesses = g.Select(a => a.TenantId).Distinct().Count(),
                LastMarkedAt = g.Max(a => a.MarkedFakeAt)!.Value
            })
            .Where(p => p.Businesses >= 2)
            .OrderByDescending(p => p.Businesses)
            .ThenByDescending(p => p.LastMarkedAt)
            .ThenBy(p => p.Phone)
            .Take(Top)
            .ToListAsync(cancellationToken);
        var keys = phones.Select(p => p.Phone).ToList();
        var whoMarked = await stillMarked
            .Where(a => keys.Contains(a.RequestPhone))
            .Select(a => new { a.RequestPhone, a.TenantId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var tenantIds = ids.Concat(whoMarked.Select(w => w.TenantId)).Distinct().ToList();
        var tenants = await _context.Tenants
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Slug })
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        return new FakeRequestsWatchDto
        {
            Top = Top,
            Businesses = byBusiness
                .Where(b => tenants.ContainsKey(b.TenantId))
                .Select(b => new FakeRequestsBusinessDto
                {
                    TenantId = b.TenantId,
                    Name = tenants[b.TenantId].Name,
                    Slug = tenants[b.TenantId].Slug,
                    Marked = b.Marked,
                    TakenBack = b.TakenBack,
                    Requests = requests.GetValueOrDefault(b.TenantId),
                    LastMarkedAt = b.LastMarkedAt
                })
                .ToList(),
            Phones = phones
                .Select(p => new FakeRequestsPhoneDto
                {
                    Ending = p.Phone.Length <= 4 ? p.Phone : p.Phone[^4..],
                    BusinessNames = whoMarked
                        .Where(w => w.RequestPhone == p.Phone && tenants.ContainsKey(w.TenantId))
                        .Select(w => tenants[w.TenantId].Name)
                        .Order(StringComparer.CurrentCulture)
                        .ToList(),
                    LastMarkedAt = p.LastMarkedAt
                })
                .ToList()
        };
    }
}

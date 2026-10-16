using System.Globalization;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>Explorar: the businesses a customer can browse by category, and how many each category has.</summary>
public class PublicDirectoryDto
{
    /// <summary>Every category, with how many businesses Explorar lists in it (whatever the search); some have none.</summary>
    public List<PublicDirectoryCategoryDto> Categories { get; set; } = new();

    /// <summary>The businesses of the category and search asked: those that take orders now first, then those open.</summary>
    public PaginatedList<PublicDirectoryBusinessDto> Businesses { get; set; } = null!;
}

public class PublicDirectoryCategoryDto
{
    public BusinessCategory Category { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// A business in Explorar: only what its public menu already shows (/m/{slug}), never its contact, fiscal data or the
/// address of a kitchen that only delivers.
/// </summary>
public class PublicDirectoryBusinessDto
{
    public string Name { get; set; } = null!;
    /// <summary>Its public menu: /m/{slug}.</summary>
    public string Slug { get; set; } = null!;
    public string? LogoUrl { get; set; }
    public BusinessCategory Category { get; set; }
    /// <summary>What it sells, in one line written by its owner.</summary>
    public string? Description { get; set; }
    public BusinessTheme Theme { get; set; }
    /// <summary>The city of its main branch; none for a kitchen that only delivers.</summary>
    public string? City { get; set; }
    /// <summary>A kitchen without premises: it only delivers, and its address stays with the team.</summary>
    public bool DeliveryOnly { get; set; }
    /// <summary>Its online orders can go to an address (it has delivery zones).</summary>
    public bool DeliveryAvailable { get; set; }
    /// <summary>Online orders arrive now: the same answer its menu gives.</summary>
    public bool AcceptsOrders { get; set; }
    /// <summary>It takes online orders, but not right now: it is closed, or no drawer is open.</summary>
    public bool OrdersPaused { get; set; }
    /// <summary>«Abierto», «Abierto las 24 horas» or «Cerrado»; none without hours.</summary>
    public string? StatusLabel { get; set; }
    /// <summary>When it closes or opens and why: «abre mañana a las 9:00».</summary>
    public string? StatusDetail { get; set; }
}

/// <summary>
/// Explorar (Fase 24A, ADR 0021): the active businesses whose menu is published (to be seen or with orders), that show
/// in Explorar and have at least one product their menu shows, with the categories and how many each has. Whether each
/// takes orders now is the rule of its menu (<see cref="OnlineOrderAvailability"/>). Read from a snapshot of every
/// business, rebuilt at most every <see cref="PublicDirectoryCache.Lifetime"/>: the menu confirms it when opened.
/// </summary>
/// <param name="Category">Only the businesses of this category; all without one.</param>
/// <param name="Search">Words of the name or the description, without accents or capitals.</param>
[AllowAnonymousRequest("Explorar: los negocios que publicaron su menú y aparecen en el directorio, solo con lo que su menú ya muestra.")]
public record GetPublicDirectoryQuery(
    BusinessCategory? Category = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = GetPublicDirectoryQuery.DefaultPageSize) : IRequest<PublicDirectoryDto>
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;
    public const int MaxSearchLength = 60;
}

public class GetPublicDirectoryQueryValidator : AbstractValidator<GetPublicDirectoryQuery>
{
    public GetPublicDirectoryQueryValidator()
    {
        RuleFor(x => x.Category).IsInEnum().WithMessage("La categoría no existe.");
        RuleFor(x => x.Search).MaximumLength(GetPublicDirectoryQuery.MaxSearchLength)
            .WithMessage($"Busca con {GetPublicDirectoryQuery.MaxSearchLength} letras como máximo.");
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetPublicDirectoryQuery.MaxPageSize);
    }
}

public class GetPublicDirectoryQueryHandler : IRequestHandler<GetPublicDirectoryQuery, PublicDirectoryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly PublicDirectoryCache _cache;

    public GetPublicDirectoryQueryHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider, PublicDirectoryCache cache)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _cache = cache;
    }

    public async Task<PublicDirectoryDto> Handle(GetPublicDirectoryQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await _cache.GetAsync(
            _dateTimeProvider.UtcNow, (utcNow, ct) => PublicDirectorySnapshot.BuildAsync(_context, utcNow, ct), cancellationToken);

        var words = PublicDirectorySnapshot.Comparable(request.Search)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = snapshot.Entries
            .Where(entry => request.Category is null || entry.Business.Category == request.Category)
            .Where(entry => words.All(entry.SearchText.Contains))
            .Select(entry => entry.Business)
            .ToList();
        var pageSize = Math.Clamp(request.PageSize, 1, GetPublicDirectoryQuery.MaxPageSize);
        var pageNumber = Math.Max(1, request.PageNumber);
        var page = found.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return new PublicDirectoryDto
        {
            Categories = snapshot.Categories.ToList(),
            Businesses = new PaginatedList<PublicDirectoryBusinessDto>(page, found.Count, pageNumber, pageSize)
        };
    }
}

/// <summary>Every business Explorar lists at an instant, in its order, with the counts by category.</summary>
public sealed class PublicDirectorySnapshot
{
    /// <summary>
    /// The most businesses one snapshot reads (by name). Past it, a table kept up to date by events replaces reading every
    /// business (ADR 0021).
    /// </summary>
    public const int MaxBusinesses = 1000;

    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.GetCultureInfo("es-MX"), ignoreCase: true);

    private PublicDirectorySnapshot(DateTimeOffset builtAt, IReadOnlyList<Entry> entries)
    {
        BuiltAt = builtAt;
        Entries = entries;
        Categories = Enum.GetValues<BusinessCategory>()
            .Select(category => new PublicDirectoryCategoryDto
            {
                Category = category,
                Count = entries.Count(entry => entry.Business.Category == category)
            })
            .ToList();
    }

    public DateTimeOffset BuiltAt { get; }

    internal IReadOnlyList<Entry> Entries { get; }

    internal IReadOnlyList<PublicDirectoryCategoryDto> Categories { get; }

    /// <param name="SearchText">Its name and description as a search compares them.</param>
    /// <param name="Rank">0 while it takes orders, 1 open (its menu, or its orders paused), 2 closed.</param>
    internal sealed record Entry(PublicDirectoryBusinessDto Business, string SearchText, int Rank);

    /// <summary>
    /// Reads every business Explorar lists: a public request has no business, so these reads span all of them, each
    /// filtered by its own business and only for the columns its menu already shows.
    /// </summary>
    public static async Task<PublicDirectorySnapshot> BuildAsync(
        IApplicationDbContext context, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        // Explorar lists an active business whose menu is published and shows in Explorar, with a product its menu shows.
        var menuProducts = MenuProducts(context);
        var listed = context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active
                && t.Settings != null
                && t.Settings.OnlineMenuMode != OnlineMenuMode.Off
                && t.Settings.ListedInDirectory
                && menuProducts.Any(p => p.TenantId == t.Id));
        var rows = await listed
            .OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Take(MaxBusinesses)
            .Select(t => new Row(
                t.Id,
                t.Name,
                t.Slug,
                t.LogoUrl,
                t.DirectoryCategory,
                t.PublicDescription,
                t.Settings!.OnlineMenuMode,
                t.Settings.Theme,
                t.Settings.TimeZone,
                t.Settings.OpeningHours,
                context.Branches
                    .Where(b => b.TenantId == t.Id && b.IsDefault)
                    .Select(b => new MainBranch(b.Id, b.City, b.ServiceMode))
                    .FirstOrDefault(),
                // Online orders arrive at the main branch while a drawer is open there; a drawer from before branches, or
                // any one without a main branch, counts (OrderCashService.HasOpenDrawerAtBranchAsync).
                context.CashSessions.Any(cs => cs.TenantId == t.Id
                    && cs.Status == CashSessionStatus.Open
                    && (cs.BranchId == null
                        || !context.Branches.Any(b => b.TenantId == t.Id && b.IsDefault)
                        || context.Branches.Any(b => b.TenantId == t.Id && b.IsDefault && b.Id == cs.BranchId))),
                context.DeliveryZones.Any(z => z.TenantId == t.Id && z.IsActive)))
            .ToListAsync(cancellationToken);

        // Their special days, in a range that holds the window of every time zone; each business keeps its own below.
        var utcToday = DateOnly.FromDateTime(utcNow.UtcDateTime);
        var since = utcToday.AddDays(-2);
        var until = utcToday.AddDays(SpecialDay.MaxDaysAhead + 2);
        var specialDays = (await context.SpecialDays
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(day => day.Date >= since && day.Date <= until && listed.Any(t => t.Id == day.TenantId))
                .OrderBy(day => day.Date)
                .ToListAsync(cancellationToken))
            .ToLookup(day => day.TenantId);

        var entries = rows
            .Select(row => ToEntry(row, specialDays[row.Id], utcNow))
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Business.Name, Names)
            .ToList();
        return new PublicDirectorySnapshot(utcNow, entries);
    }

    /// <summary>
    /// The products a public menu shows (GetPublicMenuQuery): available, not a service (they are not ordered online), in
    /// an active category of their business.
    /// </summary>
    internal static IQueryable<Product> MenuProducts(IApplicationDbContext context)
        => context.Products.Where(p => p.IsAvailable
            && p.Unit != UnitType.Hour && p.Unit != UnitType.Session && p.Unit != UnitType.Service
            && context.Categories.Any(c => c.Id == p.CategoryId && c.TenantId == p.TenantId && c.IsActive));

    private static Entry ToEntry(Row row, IEnumerable<SpecialDay> specialDays, DateTimeOffset utcNow)
    {
        var (since, until) = BusinessHours.SpecialDaysWindow(row.TimeZone, utcNow);
        var hours = BusinessHours.At(
            row.OpeningHours,
            specialDays.Where(day => day.Date >= since && day.Date <= until).ToList(),
            TenantDateTimeResolver.Resolve(row.TimeZone),
            utcNow);
        var ordersOn = row.Mode == OnlineMenuMode.Orders;
        // Explorar has no table: a closed business takes no orders here, whatever its tables.
        var acceptsOrders = OnlineOrderAvailability.Gate(row.Mode, hours, row.DrawerOpen) == OnlineOrderGate.Open;
        var deliveryOnly = row.MainBranch is { ServiceMode: BranchServiceMode.DeliveryOnly };
        var business = new PublicDirectoryBusinessDto
        {
            Name = row.Name,
            Slug = row.Slug,
            LogoUrl = string.IsNullOrEmpty(row.LogoUrl) ? null : $"{row.LogoUrl}_sm.webp",
            Category = row.Category,
            Description = row.Description,
            Theme = row.Theme,
            City = deliveryOnly || string.IsNullOrWhiteSpace(row.MainBranch?.City) ? null : row.MainBranch.City!.Trim(),
            DeliveryOnly = deliveryOnly,
            DeliveryAvailable = ordersOn && row.Delivers,
            AcceptsOrders = acceptsOrders,
            OrdersPaused = ordersOn && !acceptsOrders,
            StatusLabel = hours is null ? null : BusinessHoursText.StatusLabel(hours),
            StatusDetail = hours is null ? null : BusinessHoursText.StatusDetail(hours, seatedOverride: false)
        };
        var rank = acceptsOrders ? 0 : hours is { Status.IsOpen: false } ? 2 : 1;
        return new Entry(business, Comparable($"{row.Name} {row.Description}"), rank);
    }

    /// <summary>Lowercase, without accents and with single spaces: «Taquería  EL Güero» and «taqueria el guero» match.</summary>
    internal static string Comparable(string? value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var letters = decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
        return string.Join(' ', new string(letters.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record MainBranch(Guid Id, string? City, BranchServiceMode ServiceMode);

    private sealed record Row(
        Guid Id,
        string Name,
        string Slug,
        string? LogoUrl,
        BusinessCategory Category,
        string? Description,
        OnlineMenuMode Mode,
        BusinessTheme Theme,
        string TimeZone,
        OpeningHours? OpeningHours,
        MainBranch? MainBranch,
        bool DrawerOpen,
        bool Delivers);
}

/// <summary>
/// The snapshot of Explorar, shared by every request of this instance and rebuilt at most every <see cref="Lifetime"/>,
/// by one request at a time. A business that opens its drawer or changes its menu shows so within that time; its menu
/// always answers in the moment.
/// </summary>
public sealed class PublicDirectoryCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _rebuild = new(1, 1);
    private PublicDirectorySnapshot? _snapshot;

    public async Task<PublicDirectorySnapshot> GetAsync(
        DateTimeOffset utcNow,
        Func<DateTimeOffset, CancellationToken, Task<PublicDirectorySnapshot>> build,
        CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _snapshot);
        if (IsFresh(current, utcNow))
            return current!;

        await _rebuild.WaitAsync(cancellationToken);
        try
        {
            // Another request may have rebuilt it while this one waited.
            current = Volatile.Read(ref _snapshot);
            if (IsFresh(current, utcNow))
                return current!;
            current = await build(utcNow, cancellationToken);
            Volatile.Write(ref _snapshot, current);
            return current;
        }
        finally
        {
            _rebuild.Release();
        }
    }

    private static bool IsFresh(PublicDirectorySnapshot? snapshot, DateTimeOffset utcNow)
        => snapshot is not null && utcNow >= snapshot.BuiltAt && utcNow - snapshot.BuiltAt < Lifetime;
}

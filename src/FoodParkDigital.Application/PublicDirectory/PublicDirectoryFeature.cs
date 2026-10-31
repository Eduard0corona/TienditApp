using System.Globalization;
using System.Text;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Reviews;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>Explorar: the businesses a customer can browse by category, and how many each category has.</summary>
public class PublicDirectoryDto
{
    /// <summary>Every category, with how many businesses Explorar lists in it (whatever the search); some have none.</summary>
    public List<PublicDirectoryCategoryDto> Categories { get; set; } = new();

    /// <summary>The businesses of the category and search asked: those that take orders now first, then those open.</summary>
    public PaginatedList<PublicDirectoryBusinessDto> Businesses { get; set; } = null!;

    /// <summary>The customer's postal code the cards answer for (Fase 24B), as read; null without one.</summary>
    public string? PostalCode { get; set; }

    /// <summary>
    /// With a search, on its first page (Fase 24F): the products of the businesses of the category whose name or
    /// description has every word, at most <see cref="PublicProductIndex.MaxResults"/> and
    /// <see cref="PublicProductIndex.MaxPerBusiness"/> of one business. Empty without a search or on another page.
    /// </summary>
    public List<PublicDirectoryProductDto> Products { get; set; } = new();
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
    /// <summary>Its cover (Fase 24E), 512 px wide, across the top of its card (Fase 24F); none without one.</summary>
    public string? CoverImageUrl { get; set; }
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
    /// <summary>Closed now by its hours or a special day; false without hours (it never closes).</summary>
    public bool Closed { get; set; }
    /// <summary>Its prices include IVA; a delivery fee is written like them («más IVA» when they do not).</summary>
    public bool PricesIncludeTax { get; set; }
    /// <summary>Customers book appointments online with it now (Fase 24D): its booking page is /m/{slug}/agendar.</summary>
    public bool BooksOnline { get; set; }

    /// <summary>
    /// With the customer's postal code (Fase 24B): whether its online orders go there, what its menu would answer. Null
    /// without a postal code, or when it takes no orders to an address (<see cref="DeliveryAvailable"/>).
    /// </summary>
    public bool? DeliversToPostalCode { get; set; }
    /// <summary>What the delivery there costs, as its prices are written; null when it does not go there. Never the zone.</summary>
    public decimal? DeliveryFee { get; set; }
    /// <summary>The smallest order in products it takes there; 0 is none. Null when it does not go there.</summary>
    public decimal? MinimumOrder { get; set; }
    /// <summary>From what order in products the delivery there is free, when it is.</summary>
    public decimal? FreeDeliveryFrom { get; set; }

    /// <summary>
    /// With the customer's position (Fase 24G): how far its main branch is in a straight line, in kilometers with one
    /// decimal. Null without a position, or when its main branch is not on the map or only delivers.
    /// </summary>
    public decimal? DistanceKm { get; set; }

    /// <summary>How its customers rate it (Fase 24H): its published reviews and their average; null without reviews.</summary>
    public RatingSummaryDto? Rating { get; set; }

    /// <summary>
    /// It receives visits to quote now (Fase 29A, ADR 0053): its card speaks of visits, never of orders or the drawer,
    /// and it goes with those taking orders whatever its drawer or its hours (the customer picks a free time).
    /// </summary>
    public bool ReceivesVisits { get; set; }
    /// <summary>How it charges the visit; null when it does not receive visits.</summary>
    public HomeVisitChargePolicy? VisitChargePolicy { get; set; }
    /// <summary>
    /// What a visit costs, written like its prices: with the customer's postal code, the visit there (null when it does
    /// not go there); without it, from the zone that charges least («desde» when another charges more). Null when it does
    /// not receive visits.
    /// </summary>
    public HomeVisitPriceDto? VisitPrice { get; set; }
    /// <summary>
    /// With the customer's postal code: whether one of its zones goes there, so it visits. Null without a postal code, or
    /// when it does not receive visits.
    /// </summary>
    public bool? VisitsPostalCode { get; set; }

    internal PublicDirectoryBusinessDto Copy() => (PublicDirectoryBusinessDto)MemberwiseClone();
}

/// <summary>
/// Explorar (Fase 24A, ADR 0021): the active businesses whose menu is published (to be seen or with orders), that show
/// in Explorar and have at least one product their menu shows, book appointments online or receive visits to quote (Fase
/// 29A), with the categories and how many each has. Whether each
/// takes orders now is the rule of its menu (<see cref="OnlineOrderAvailability"/>). Read from a snapshot of every
/// business, rebuilt at most every <see cref="PublicDirectoryCache.Lifetime"/>: the menu confirms it when opened.
/// </summary>
/// <param name="Category">Only the businesses of this category; all without one.</param>
/// <param name="Search">Words of the name, the description or the city, without accents or capitals.</param>
/// <param name="PostalCode">
/// The customer's postal code (Fase 24B): each card of the page says whether the business delivers there and what it
/// costs, as its menu would, or whether it visits there and what the visit costs (Fase 29A). The businesses that receive
/// visits and do not go there come last; the rest of the order does not change.
/// </param>
/// <param name="Latitude">
/// The customer's position, with <paramref name="Longitude"/> (Fase 24G, «Cerca de mí»): the businesses open now come
/// nearest first, then the closed ones nearest first, and each card says how far it is. Read to
/// <see cref="NearDecimals"/> decimals, never kept or written to the log.
/// </param>
[AllowAnonymousRequest("Explorar: los negocios que publicaron su menú y aparecen en el directorio, solo con lo que su menú ya muestra.")]
public record GetPublicDirectoryQuery(
    BusinessCategory? Category = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = GetPublicDirectoryQuery.DefaultPageSize,
    string? PostalCode = null,
    decimal? Latitude = null,
    decimal? Longitude = null) : IRequest<PublicDirectoryDto>
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 48;
    public const int MaxSearchLength = 60;
    /// <summary>The customer's position is read to three decimals, about 110 m: what the app sends, and all it needs.</summary>
    public const int NearDecimals = 3;
    public const string PositionMessage = "No pudimos leer tu ubicación. Vuelve a tocar «Cerca de mí».";

    /// <summary>The customer's position as Explorar measures from it; null without one, or when it is not a point.</summary>
    public GeoPoint? Near()
    {
        if (Latitude is not { } latitude || Longitude is not { } longitude)
            return null;
        // Read to three decimals before it is checked: 0.0001, 0.0002 is the 0, 0 it becomes.
        var lat = Math.Round(latitude, NearDecimals, MidpointRounding.AwayFromZero);
        var lng = Math.Round(longitude, NearDecimals, MidpointRounding.AwayFromZero);
        return GeoPoint.Problem(lat, lng) is null ? new GeoPoint(lat, lng) : null;
    }
}

public class GetPublicDirectoryQueryValidator : AbstractValidator<GetPublicDirectoryQuery>
{
    public GetPublicDirectoryQueryValidator()
    {
        RuleFor(x => x.Category).IsInEnum().WithMessage("La categoría no existe.");
        RuleFor(x => x.Search).MaximumLength(GetPublicDirectoryQuery.MaxSearchLength)
            .WithMessage($"Busca con {GetPublicDirectoryQuery.MaxSearchLength} letras como máximo.");
        // No snapshot holds more pages than businesses.
        RuleFor(x => x.PageNumber).InclusiveBetween(1, PublicDirectorySnapshot.MaxBusinesses);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetPublicDirectoryQuery.MaxPageSize);
        RuleFor(x => x.PostalCode)
            .Must(code => DeliveryZone.NormalizePostalCode(code) is not { } postalCode || DeliveryZone.IsPostalCode(postalCode))
            // Without what was written: a validation message reaches the log (ADR 0017 keeps postal codes out of it).
            .WithMessage(GetPublicDeliveryQuoteQueryValidator.PostalCodeMessage);
        // Both halves of a point or neither, never echoed back: the customer's position stays out of the log too.
        RuleFor(x => x.Latitude)
            .Must((query, _) => (query.Latitude is null && query.Longitude is null) || query.Near() is not null)
            .WithMessage(GetPublicDirectoryQuery.PositionMessage);
    }
}

public class GetPublicDirectoryQueryHandler : IRequestHandler<GetPublicDirectoryQuery, PublicDirectoryDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly PublicDirectoryCache _cache;
    private readonly PublicProductIndexCache _products;
    private readonly ILogger<GetPublicDirectoryQueryHandler> _logger;

    public GetPublicDirectoryQueryHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        PublicDirectoryCache cache,
        PublicProductIndexCache products,
        ILogger<GetPublicDirectoryQueryHandler> logger)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _cache = cache;
        _products = products;
        _logger = logger;
    }

    public async Task<PublicDirectoryDto> Handle(GetPublicDirectoryQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await _cache.GetAsync(
            _dateTimeProvider.UtcNow, (utcNow, ct) => PublicDirectorySnapshot.BuildAsync(_context, utcNow, ct, _logger), cancellationToken);

        var words = PublicDirectorySnapshot.Comparable(request.Search)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var inCategory = snapshot.Entries
            .Where(entry => request.Category is null || entry.Business.Category == request.Category)
            .ToList();
        // «Cerca de mí» orders every business of the category, so the products of a search follow the same order.
        var near = request.Near() is { } position ? PublicDirectoryNearby.Order(inCategory, position) : null;
        var found = (near?.Entries ?? inCategory)
            .Where(entry => words.All(entry.SearchText.Contains))
            .ToList();
        var postalCode = DeliveryZone.NormalizePostalCode(request.PostalCode);
        // A trade that does not go to the postal code is no use to the customer, so it goes after the rest (a stable order:
        // «Cerca de mí» and the order of Explorar stay within each part). Only the businesses found that receive visits are
        // asked for their zones here; nothing is read when none does.
        var visiting = postalCode is null ? [] : found.Where(entry => entry.Business.ReceivesVisits).ToList();
        if (visiting.Count > 0)
        {
            var visitZones = (await PublicDirectoryCards.ZonesAtAsync(_context, visiting, postalCode, cancellationToken))!;
            found = found.OrderBy(entry => entry.Business.ReceivesVisits && !visitZones.ContainsKey(entry.TenantId) ? 1 : 0).ToList();
        }
        var pageSize = Math.Clamp(request.PageSize, 1, GetPublicDirectoryQuery.MaxPageSize);
        var pageNumber = Math.Max(1, request.PageNumber);
        var page = found.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        // What each card of the page says about delivering and visiting there: the zones of the page only.
        var businesses = await PublicDirectoryCards.WithDeliveryToAsync(_context, page, postalCode, cancellationToken, near?.DistanceKm);
        // The products of a search come with its first page; the index of the snapshot is read only when there is one.
        var products = words.Length == 0 || pageNumber > 1 ? [] : await SearchProductsAsync(snapshot, words, request.Category, near, cancellationToken);

        return new PublicDirectoryDto
        {
            Categories = snapshot.Categories.ToList(),
            Businesses = new PaginatedList<PublicDirectoryBusinessDto>(businesses, found.Count, pageNumber, pageSize),
            PostalCode = postalCode,
            Products = products
        };
    }

    /// <summary>The products of a search; none when their index could not be built: the businesses still answer.</summary>
    private async Task<List<PublicDirectoryProductDto>> SearchProductsAsync(
        PublicDirectorySnapshot snapshot, string[] words, BusinessCategory? category, PublicDirectoryNearby? near, CancellationToken cancellationToken)
    {
        try
        {
            var index = await _products.GetAsync(
                snapshot, (current, ct) => PublicProductIndex.BuildAsync(_context, current, ct, _logger), cancellationToken);
            return index?.Search(words, category, near) ?? [];
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Explorar could not read the products of its businesses: this search answers with the businesses only.");
            return [];
        }
    }
}

/// <summary>
/// The cards of Explorar as a customer reads them: with the delivery to their postal code (Fase 24B), or the visit there
/// (Fase 29A), and how far each business is.
/// </summary>
internal static class PublicDirectoryCards
{
    /// <summary>
    /// The cards of <paramref name="page"/>, each saying whether the business delivers to <paramref name="postalCode"/>
    /// and what it costs, or visits it and what the visit costs, and how far it is (<paramref name="distanceKm"/>): copies,
    /// because the snapshot is shared by every request.
    /// </summary>
    public static async Task<List<PublicDirectoryBusinessDto>> WithDeliveryToAsync(
        IApplicationDbContext context,
        IReadOnlyList<PublicDirectorySnapshot.Entry> page,
        string? postalCode,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, decimal>? distanceKm = null)
        => WithDeliveryTo(page, await ZonesAtAsync(context, page, postalCode, cancellationToken), distanceKm);

    /// <summary>
    /// The active zone that reaches <paramref name="postalCode"/> of each business of <paramref name="entries"/> that
    /// delivers or receives visits, by business; one that is not there does not go. Null without a postal code: nothing
    /// is read.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, ZoneAt>?> ZonesAtAsync(
        IApplicationDbContext context,
        IReadOnlyList<PublicDirectorySnapshot.Entry> entries,
        string? postalCode,
        CancellationToken cancellationToken)
    {
        if (postalCode is null)
            return null;

        // Only the businesses that deliver or visit ask; a list without one reads nothing.
        var asking = entries
            .Where(entry => entry.Business.DeliveryAvailable || entry.Business.ReceivesVisits)
            .Select(entry => entry.TenantId)
            .ToList();
        if (asking.Count == 0)
            return new Dictionary<Guid, ZoneAt>();

        // A public request has no business: the active zones of those businesses that reach the postal code, in one read
        // (each zone with its own business, IX_DeliveryZonePostalCodes_PostalCode); a business has one at most.
        var zones = await context.DeliveryZonePostalCodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(code => code.PostalCode == postalCode && asking.Contains(code.TenantId))
            .Join(context.DeliveryZones, code => code.DeliveryZoneId, zone => zone.Id, (code, zone) => new { code.TenantId, Zone = zone })
            .Where(row => row.Zone.IsActive && row.Zone.TenantId == row.TenantId)
            .Select(row => new { row.TenantId, row.Zone.Fee, row.Zone.MinimumOrder, row.Zone.FreeDeliveryFrom })
            .ToListAsync(cancellationToken);
        return zones
            .GroupBy(zone => zone.TenantId)
            .ToDictionary(group => group.Key, group =>
            {
                var zone = group.First();
                return new ZoneAt(zone.Fee, zone.MinimumOrder, zone.FreeDeliveryFrom);
            });
    }

    /// <summary>
    /// The cards of <paramref name="page"/> with what the zones that reach the customer's postal code say
    /// (<paramref name="zones"/>, null without a postal code) and how far each business is: copies. Without a postal code
    /// or a position, the cards of the snapshot as they are.
    /// </summary>
    public static List<PublicDirectoryBusinessDto> WithDeliveryTo(
        IReadOnlyList<PublicDirectorySnapshot.Entry> page,
        IReadOnlyDictionary<Guid, ZoneAt>? zones,
        IReadOnlyDictionary<Guid, decimal>? distanceKm = null)
    {
        if (zones is null && distanceKm is null)
            return page.Select(entry => entry.Business).ToList();

        return page.Select(entry =>
        {
            var card = Measured(entry, distanceKm);
            if (zones is null)
                return card;
            var zone = zones.GetValueOrDefault(entry.TenantId);
            if (card.DeliveryAvailable)
            {
                card.DeliversToPostalCode = zone is not null;
                card.DeliveryFee = zone?.Fee;
                card.MinimumOrder = zone?.MinimumOrder;
                card.FreeDeliveryFrom = zone?.FreeDeliveryFrom;
            }
            if (card.ReceivesVisits)
            {
                // The visit there, as the store and the request say: never the zone, only what going there costs.
                card.VisitsPostalCode = zone is not null;
                card.VisitPrice = zone is null
                    ? null
                    : new HomeVisitPriceDto
                    {
                        Amount = HomeVisitCharge.To(card.VisitChargePolicy ?? HomeVisitChargePolicy.Charged, entry.VisitServicePrice, zone.Fee)
                    };
            }
            return card;
        }).ToList();
    }

    /// <summary>A copy of the card with its distance, when there is one to tell.</summary>
    private static PublicDirectoryBusinessDto Measured(PublicDirectorySnapshot.Entry entry, IReadOnlyDictionary<Guid, decimal>? distanceKm)
    {
        var card = entry.Business.Copy();
        card.DistanceKm = distanceKm is not null && distanceKm.TryGetValue(entry.TenantId, out var km) ? km : null;
        return card;
    }

    /// <summary>What the zone of a business that reaches the customer's postal code charges: never its name.</summary>
    internal sealed record ZoneAt(decimal Fee, decimal MinimumOrder, decimal? FreeDeliveryFrom);
}

/// <summary>
/// «Cerca de mí» (Fase 24G, ADR 0029): the businesses of Explorar in the order of their distance to the customer, and how
/// far each one is. Measured to the main branch, the one whose address its menu shows and where its online orders
/// arrive, while customers come to it: a kitchen that only delivers is never measured (its address stays private).
/// </summary>
public sealed class PublicDirectoryNearby
{
    private PublicDirectoryNearby(IReadOnlyList<PublicDirectorySnapshot.Entry> entries, IReadOnlyDictionary<Guid, decimal> distanceKm)
    {
        Entries = entries;
        DistanceKm = distanceKm;
        Position = entries.Select((entry, index) => (entry.TenantId, index)).ToDictionary(item => item.TenantId, item => item.index);
    }

    /// <summary>
    /// The businesses open now first (they take orders, book online, receive visits or show their menu while open), nearest first; then
    /// the closed ones, nearest first. In each group, those not on the map follow in the order of Explorar.
    /// </summary>
    internal IReadOnlyList<PublicDirectorySnapshot.Entry> Entries { get; }

    /// <summary>How far each business on the map is, in kilometers with one decimal.</summary>
    internal IReadOnlyDictionary<Guid, decimal> DistanceKm { get; }

    /// <summary>Each business's place in <see cref="Entries"/>: the products of a search follow it.</summary>
    internal IReadOnlyDictionary<Guid, int> Position { get; }

    internal static PublicDirectoryNearby Order(IReadOnlyList<PublicDirectorySnapshot.Entry> entries, GeoPoint customer)
    {
        var measured = entries
            .Select((entry, index) => (Entry: entry, Index: index, Km: entry.Location?.KilometersTo(customer)))
            .ToList();
        var ordered = measured
            .OrderBy(item => item.Entry.Rank == PublicDirectorySnapshot.ClosedRank ? 1 : 0)
            .ThenBy(item => item.Km is null ? 1 : 0)
            .ThenBy(item => item.Km)
            .ThenBy(item => item.Index)
            .Select(item => item.Entry)
            .ToList();
        var distanceKm = measured
            .Where(item => item.Km is not null)
            .ToDictionary(item => item.Entry.TenantId, item => Math.Round((decimal)item.Km!.Value, 1, MidpointRounding.AwayFromZero));
        return new PublicDirectoryNearby(ordered, distanceKm);
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

    /// <summary>The <see cref="Entry.Rank"/> of a business closed now that does not book online either.</summary>
    internal const int ClosedRank = 2;

    /// <param name="TenantId">Its business, to look up its delivery to a postal code; never in the card.</param>
    /// <param name="SearchText">Its name, description and city as a search compares them.</param>
    /// <param name="Rank">
    /// 0 while it takes orders, books online or receives visits (Fase 29A), 1 open (its menu, or its orders paused), 2
    /// closed.
    /// </param>
    /// <param name="Location">
    /// Where its main branch is on the map while customers come to it (Fase 24G); never in the card, only how far it is.
    /// </param>
    /// <param name="VisitServicePrice">
    /// The price of the service it picked as its visit while it is bookable (Fase 29A), to say what the visit to a postal
    /// code costs; never in the card.
    /// </param>
    internal sealed record Entry(
        Guid TenantId,
        PublicDirectoryBusinessDto Business,
        string SearchText,
        int Rank,
        GeoPoint? Location = null,
        decimal? VisitServicePrice = null);

    /// <summary>
    /// Reads every business Explorar lists: a public request has no business, so these reads span all of them, each
    /// filtered by its own business and only for the columns its menu already shows.
    /// </summary>
    /// <param name="logger">Says when there were more businesses than one snapshot reads.</param>
    public static async Task<PublicDirectorySnapshot> BuildAsync(
        IApplicationDbContext context, DateTimeOffset utcNow, CancellationToken cancellationToken, ILogger? logger = null)
    {
        // Explorar lists an active business whose menu is published and shows in Explorar, with a product its menu shows.
        var menuProducts = MenuProducts(context);
        var bookableServices = OnlineBookingCalendar.OfferedServices(context);
        var visitServices = OnlineBookingCalendar.BookableServices(context);
        // A business that only sells services shows while it books them online (Fase 24D) or receives visits to quote (Fase
        // 29A, it goes somewhere: an active zone): its menu has nothing to order.
        var listed = context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active
                && t.Settings != null
                && t.Settings.OnlineMenuMode != OnlineMenuMode.Off
                && t.Settings.ListedInDirectory
                && (menuProducts.Any(p => p.TenantId == t.Id)
                    || (t.Settings.OnlineBookingEnabled && bookableServices.Any(p => p.TenantId == t.Id))
                    || (t.Settings.HomeVisitRequestsEnabled && context.DeliveryZones.Any(z => z.TenantId == t.Id && z.IsActive))));
        var rows = await listed
            .OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Take(MaxBusinesses + 1)
            .Select(t => new Row(
                t.Id,
                t.Name,
                t.Slug,
                t.LogoUrl,
                t.CoverImageUrl,
                t.DirectoryCategory,
                t.PublicDescription,
                t.Settings!.OnlineMenuMode,
                t.Settings.Theme,
                t.Settings.TimeZone,
                t.Settings.OpeningHours,
                t.Settings.PricesIncludeTax,
                context.Branches
                    .Where(b => b.TenantId == t.Id && b.IsDefault)
                    .Select(b => new MainBranch(b.Id, b.City, b.ServiceMode, b.Latitude, b.Longitude))
                    .FirstOrDefault(),
                // Online orders arrive at the main branch while a drawer is open there; a drawer from before branches, or
                // any one without a main branch, counts (OrderCashService.HasOpenDrawerAtBranchAsync).
                context.CashSessions.Any(cs => cs.TenantId == t.Id
                    && cs.Status == CashSessionStatus.Open
                    && (cs.BranchId == null
                        || !context.Branches.Any(b => b.TenantId == t.Id && b.IsDefault)
                        || context.Branches.Any(b => b.TenantId == t.Id && b.IsDefault && b.Id == cs.BranchId))),
                context.DeliveryZones.Any(z => z.TenantId == t.Id && z.IsActive),
                menuProducts.Any(p => p.TenantId == t.Id),
                t.Settings.OnlineBookingEnabled && bookableServices.Any(p => p.TenantId == t.Id),
                t.Settings.Capabilities,
                t.BusinessType,
                t.Settings.HomeVisitRequestsEnabled,
                t.Settings.HomeVisitChargePolicy,
                visitServices
                    .Where(p => p.TenantId == t.Id && p.Id == t.Settings.HomeVisitServiceId)
                    .Select(p => (decimal?)p.PriceAmount)
                    .FirstOrDefault(),
                // What going costs: from the cheapest active zone, «desde» when another charges more.
                context.DeliveryZones.Where(z => z.TenantId == t.Id && z.IsActive).Min(z => (decimal?)z.Fee),
                context.DeliveryZones.Where(z => z.TenantId == t.Id && z.IsActive).Max(z => (decimal?)z.Fee)))
            .ToListAsync(cancellationToken);
        if (rows.Count > MaxBusinesses)
        {
            // The last ones by name are left out, also when they take orders: time for the table kept by events (ADR 0021).
            logger?.LogWarning("Explorar lists more than {MaxBusinesses} businesses: the ones after it by name are left out.", MaxBusinesses);
            rows.RemoveAt(MaxBusinesses);
        }

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
        // How customers rate each one (Fase 24H): the published reviews of the businesses listed, one row per business.
        var ratings = (await context.BusinessReviews
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.Status == ReviewStatus.Published && listed.Any(t => t.Id == r.TenantId))
                .GroupBy(r => r.TenantId)
                .Select(g => new { TenantId = g.Key, Count = g.Count(), Stars = g.Sum(r => r.Rating) })
                .ToListAsync(cancellationToken))
            .ToDictionary(r => r.TenantId, r => RatingSummaryDto.From(r.Count, r.Stars));

        var entries = rows
            .Select(row => ToEntry(row, specialDays[row.Id], utcNow))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.Business.Name, Names)
            .ToList();
        foreach (var entry in entries)
            entry.Business.Rating = ratings.GetValueOrDefault(entry.TenantId);
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

    /// <summary>
    /// The order of a public menu: its featured products first, then as its business sorts them, by name. The menu shows
    /// the first <see cref="GetPublicMenuQueryHandler.MaxProducts"/> in it, and Explorar searches the same ones.
    /// </summary>
    internal static IOrderedQueryable<Product> InMenuOrder(IQueryable<Product> products)
        => products.OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name).ThenBy(p => p.Id);

    /// <summary>The products already ordered by something else (their business), each group in <see cref="InMenuOrder"/>.</summary>
    internal static IOrderedQueryable<Product> ThenInMenuOrder(IOrderedQueryable<Product> products)
        => products.ThenByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name).ThenBy(p => p.Id);

    /// <summary>
    /// The card of a business; null for one listed for its services that neither books them online nor receives visits
    /// after all.
    /// </summary>
    private static Entry? ToEntry(Row row, IEnumerable<SpecialDay> specialDays, DateTimeOffset utcNow)
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
        // The rule of its booking page (OnlineBookingAvailability), with its services already counted.
        var booksOnline = row.BooksServices && OnlineBookingAvailability.BooksOnline(
            enabled: true,
            TenantCapabilityRules.NormalizeCapabilities(row.Capabilities, row.BusinessType),
            row.Mode,
            row.MainBranch?.ServiceMode,
            row.OpeningHours is not null);
        // The rule of its visits (HomeVisitRequestAvailability): its premises and its drawer do not count.
        var receivesVisits = HomeVisitRequestAvailability.ReceivesVisits(
            row.VisitsEnabled,
            TenantCapabilityRules.NormalizeCapabilities(row.Capabilities, row.BusinessType),
            row.Mode,
            hasActiveZones: row.Delivers,
            hasWeeklyHours: row.OpeningHours is not null);
        if (!row.HasMenu && !booksOnline && !receivesVisits)
            return null;
        var business = new PublicDirectoryBusinessDto
        {
            Name = row.Name,
            Slug = row.Slug,
            LogoUrl = string.IsNullOrEmpty(row.LogoUrl) ? null : $"{row.LogoUrl}_sm.webp",
            CoverImageUrl = LogoImageUrlsDto.FromBaseUrl(row.CoverImageUrl)?.Medium,
            Category = row.Category,
            Description = row.Description,
            Theme = row.Theme,
            City = deliveryOnly || string.IsNullOrWhiteSpace(row.MainBranch?.City) ? null : row.MainBranch.City!.Trim(),
            DeliveryOnly = deliveryOnly,
            DeliveryAvailable = ordersOn && row.Delivers,
            AcceptsOrders = acceptsOrders,
            OrdersPaused = ordersOn && !acceptsOrders,
            StatusLabel = hours is null ? null : BusinessHoursText.StatusLabel(hours),
            StatusDetail = hours is null ? null : BusinessHoursText.StatusDetail(hours, seatedOverride: false),
            Closed = hours is { Status.IsOpen: false },
            PricesIncludeTax = row.PricesIncludeTax,
            BooksOnline = booksOnline,
            ReceivesVisits = receivesVisits,
            VisitChargePolicy = receivesVisits ? row.VisitPolicy : null,
            VisitPrice = receivesVisits && row.MinZoneFee is { } cheapest
                ? HomeVisitPriceDto.From(HomeVisitCharge.Starting(
                    row.VisitPolicy, row.VisitServicePrice, row.MaxZoneFee is { } dearest && dearest != cheapest ? [cheapest, dearest] : [cheapest]))
                : null
        };
        var rank = acceptsOrders || booksOnline || receivesVisits ? 0 : business.Closed ? ClosedRank : 1;
        // Measured only while customers come to it: the point of a kitchen that only delivers stays with its team.
        var location = deliveryOnly ? null : GeoPoint.FromStored(row.MainBranch?.Latitude, row.MainBranch?.Longitude);
        // Its city too, when the card shows it: never the one of a kitchen that only delivers.
        return new Entry(
            row.Id,
            business,
            Comparable($"{row.Name} {row.Description} {business.City}"),
            rank,
            location,
            receivesVisits ? row.VisitServicePrice : null);
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

    private sealed record MainBranch(Guid Id, string? City, BranchServiceMode ServiceMode, decimal? Latitude, decimal? Longitude);

    private sealed record Row(
        Guid Id,
        string Name,
        string Slug,
        string? LogoUrl,
        string? CoverImageUrl,
        BusinessCategory Category,
        string? Description,
        OnlineMenuMode Mode,
        BusinessTheme Theme,
        string TimeZone,
        OpeningHours? OpeningHours,
        bool PricesIncludeTax,
        MainBranch? MainBranch,
        bool DrawerOpen,
        bool Delivers,
        bool HasMenu,
        bool BooksServices,
        TenantCapability Capabilities,
        BusinessType BusinessType,
        bool VisitsEnabled,
        HomeVisitChargePolicy VisitPolicy,
        decimal? VisitServicePrice,
        decimal? MinZoneFee,
        decimal? MaxZoneFee);
}

/// <summary>
/// The snapshot of Explorar, shared by every request of this instance and rebuilt at most every <see cref="Lifetime"/>,
/// by one request at a time. A business that opens its drawer or changes its menu shows so within that time; its menu
/// always answers in the moment.
/// </summary>
public sealed class PublicDirectoryCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RebuildTimeout = TimeSpan.FromSeconds(30);

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
            // Its own time limit, not the request's: every request waiting shares it, and a customer who leaves (or
            // changes the search) does not throw away a rebuild half done.
            using var limit = new CancellationTokenSource(RebuildTimeout);
            current = await build(utcNow, limit.Token);
            Volatile.Write(ref _snapshot, current);
            return current;
        }
        finally
        {
            _rebuild.Release();
        }
    }

    /// <summary>Forgets the snapshot: the next request reads every business again (a test that has just changed them).</summary>
    public void Invalidate() => Volatile.Write(ref _snapshot, null);

    /// <summary>
    /// Built less than <see cref="Lifetime"/> before or after this instant: a request that read the clock an instant
    /// before the one that rebuilt it uses it too, and a clock that goes back further rebuilds it.
    /// </summary>
    private static bool IsFresh(PublicDirectorySnapshot? snapshot, DateTimeOffset utcNow)
        => snapshot is not null && (utcNow - snapshot.BuiltAt).Duration() < Lifetime;
}

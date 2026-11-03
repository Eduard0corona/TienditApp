using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>
/// A product a search of Explorar found (Fase 24F): only what its menu already shows, and the business it is at. It opens
/// in its store (/m/{slug}?producto={id}), where its menu answers in the moment.
/// </summary>
public class PublicDirectoryProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public decimal Price { get; set; }
    /// <summary>What it cost before its discount, when it has one (its menu crosses it out).</summary>
    public decimal? CompareAtPrice { get; set; }
    /// <summary>Its photo, 256 px wide; none without one.</summary>
    public string? ImageUrl { get; set; }
    public string BusinessName { get; set; } = null!;
    /// <summary>Its store: /m/{slug}.</summary>
    public string BusinessSlug { get; set; } = null!;
    /// <summary>The colors of its store, for a product without a photo.</summary>
    public BusinessTheme Theme { get; set; }
    /// <summary>Its business takes online orders now: what its card in Explorar says.</summary>
    public bool AcceptsOrders { get; set; }
    /// <summary>
    /// The order adds IVA to its price: its business writes its prices without IVA and it is not exempt nor at 0%. The
    /// card writes «más IVA».
    /// </summary>
    public bool AddsTax { get; set; }
    /// <summary>
    /// With the customer's position (Fase 24G): how far its business is, as its card says. Null without a position or
    /// when its business is not measured.
    /// </summary>
    public decimal? DistanceKm { get; set; }
}

/// <summary>Where a service a search of Explorar found is had (Fase 29I, ADR 0063).</summary>
public enum PublicDirectoryServiceKind
{
    /// <summary>Booked online at the business: it opens on its booking page, chosen (/m/{slug}/agendar?servicio={id}).</summary>
    BooksOnline = 0,
    /// <summary>
    /// Quoted after a visit to the customer's home: its price is what it starts at, and it opens the store of its business,
    /// where the visit is requested.
    /// </summary>
    HomeVisit = 1
}

/// <summary>
/// A service a search of Explorar found (Fase 29I, ADR 0063): only what the booking page or the store of its business
/// already shows, and the business it is at.
/// </summary>
public class PublicDirectoryServiceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    /// <summary>Its price; for <see cref="PublicDirectoryServiceKind.HomeVisit"/>, what it starts at («desde»).</summary>
    public decimal Price { get; set; }
    /// <summary>How long it takes, as its booking page or its store says; null when its store says nothing.</summary>
    public int? DurationMinutes { get; set; }
    /// <summary>Its photo, 256 px wide; none without one.</summary>
    public string? ImageUrl { get; set; }
    public PublicDirectoryServiceKind Kind { get; set; }
    /// <summary>
    /// Its store writes «más IVA» after its price: a service that receives visits of a business that writes its prices
    /// without IVA. Its booking page never does.
    /// </summary>
    public bool AddsTax { get; set; }
    public string BusinessName { get; set; } = null!;
    /// <summary>Its store: /m/{slug}.</summary>
    public string BusinessSlug { get; set; } = null!;
    /// <summary>The colors of its store, for a service without a photo.</summary>
    public BusinessTheme Theme { get; set; }
    /// <summary>With the customer's position: how far its business is, as its card says.</summary>
    public decimal? DistanceKm { get; set; }
}

/// <summary>
/// The products of every business of a snapshot of Explorar, as their menus show them, and the services of those that book
/// online or receive visits, as their booking page or their store show them (Fase 29I), to search by words: built from the
/// snapshot when a search first needs it, and rebuilt with the next snapshot (<see cref="PublicProductIndexCache"/>).
/// </summary>
public sealed class PublicProductIndex
{
    /// <summary>
    /// The most products one index reads; past it, those of the businesses Explorar shows last are left out (and logged).
    /// </summary>
    public const int MaxProducts = 20_000;

    /// <summary>
    /// The businesses whose products one read brings: few reads, each of at most this many menus of
    /// <see cref="GetPublicMenuQueryHandler.MaxProducts"/> products.
    /// </summary>
    internal const int BusinessesPerRead = 50;

    /// <summary>The products one search returns.</summary>
    public const int MaxResults = 12;

    /// <summary>The products of one business one search returns, so a search shows several businesses.</summary>
    public const int MaxPerBusiness = 3;

    /// <summary>
    /// The most services one index reads (Fase 29I); past it, those of the businesses Explorar shows last are left out (and
    /// logged). A search returns at most <see cref="MaxResults"/> of them, <see cref="MaxPerBusiness"/> of one business.
    /// </summary>
    public const int MaxServices = 10_000;

    /// <summary>
    /// A business with at most this many bookable services is read whole: its booking page (<see cref="OnlineBookingCalendar.MaxServices"/>)
    /// and its store (<see cref="PublicHomeVisitsDto.MaxServices"/>) show them all. One with more is read on its own, each
    /// list cut as its page cuts it.
    /// </summary>
    internal const int WholeServices = PublicHomeVisitsDto.MaxServices;

    private readonly IReadOnlyList<Entry> _entries;
    private readonly IReadOnlyList<ServiceEntry> _services;

    private PublicProductIndex(DateTimeOffset builtFor, IReadOnlyList<Entry> entries, IReadOnlyList<ServiceEntry> services)
    {
        BuiltFor = builtFor;
        _entries = entries;
        _services = services;
    }

    /// <summary>When the snapshot it was built from was built.</summary>
    public DateTimeOffset BuiltFor { get; }

    /// <summary>
    /// Reads the products the menus of the snapshot's businesses show: of each, the first
    /// <see cref="GetPublicMenuQueryHandler.MaxProducts"/> in the order of its menu
    /// (<see cref="PublicDirectorySnapshot.MenuProducts"/>, <see cref="PublicDirectorySnapshot.InMenuOrder"/>), so a product
    /// found always opens in its menu. A public request has no business, so the read spans them all, each product with its
    /// own business and only the columns its menu already shows. The businesses are read in the order of Explorar.
    /// </summary>
    /// <param name="logger">Says when there were more products than one index reads.</param>
    public static async Task<PublicProductIndex> BuildAsync(
        IApplicationDbContext context, PublicDirectorySnapshot snapshot, CancellationToken cancellationToken, ILogger? logger = null)
    {
        var businesses = snapshot.Entries
            .Select((entry, position) => (entry, position))
            .ToDictionary(item => item.entry.TenantId);
        // The menus of some businesses: the products they show, read before any business is in context.
        IQueryable<Product> Menus(IReadOnlyCollection<Guid> tenantIds) => PublicDirectorySnapshot.MenuProducts(context)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => tenantIds.Contains(p.TenantId));
        IQueryable<ProductRow> Rows(IQueryable<Product> products) => products.Select(p => new ProductRow(
            p.Id,
            p.TenantId,
            p.Name,
            p.Description,
            p.PriceAmount,
            p.CompareAtPrice,
            p.ImageUrl,
            p.TaxRate,
            p.IsTaxExempt,
            context.TenantSettings.Where(s => s.TenantId == p.TenantId).Select(s => s.DefaultTaxRate).FirstOrDefault()));

        var entries = new List<Entry>();
        foreach (var batch in snapshot.Entries.Select(entry => entry.TenantId).Chunk(BusinessesPerRead))
        {
            // Each business brings at most what its menu shows: one with a larger catalog is read on its own, cut to
            // the first ones of its menu (GetPublicMenuQuery), and never takes the place of the others of its batch.
            var sizes = await Menus(batch)
                .GroupBy(p => p.TenantId)
                .Select(menu => new { TenantId = menu.Key, Products = menu.Count() })
                .ToListAsync(cancellationToken);
            var rows = new List<ProductRow>();
            var whole = sizes.Where(menu => menu.Products <= GetPublicMenuQueryHandler.MaxProducts).Select(menu => menu.TenantId).ToList();
            if (whole.Count > 0)
            {
                rows.AddRange(await Rows(PublicDirectorySnapshot.ThenInMenuOrder(Menus(whole).OrderBy(p => p.TenantId)))
                    .ToListAsync(cancellationToken));
            }

            foreach (var large in sizes.Where(menu => menu.Products > GetPublicMenuQueryHandler.MaxProducts))
            {
                rows.AddRange(await Rows(PublicDirectorySnapshot.InMenuOrder(Menus([large.TenantId])).Take(GetPublicMenuQueryHandler.MaxProducts))
                    .ToListAsync(cancellationToken));
            }

            // In the order of Explorar (OrderBy keeps the order of each menu): past the limit, the last ones are left out.
            entries.AddRange(rows
                .OrderBy(row => businesses[row.TenantId].position)
                .Select(row =>
                {
                    var (business, position) = businesses[row.TenantId];
                    return new Entry(
                        business,
                        position,
                        row.Id,
                        row.Name,
                        row.PriceAmount,
                        // The menu's rule (Product.HasDiscount): a price before only when it was higher.
                        row.CompareAtPrice is { } before && before > row.PriceAmount ? before : null,
                        ProductImageUrlsDto.FromBaseUrl(row.ImageUrl)?.Small,
                        !business.Business.PricesIncludeTax && VatRate.ForProduct(row.TaxRate, row.IsTaxExempt, row.DefaultTaxRate).Percent > 0,
                        PublicDirectorySnapshot.Comparable(row.Name),
                        PublicDirectorySnapshot.Comparable($"{row.Name} {row.Description}"));
                }));
            if (entries.Count > MaxProducts)
            {
                logger?.LogWarning("Explorar searches more than {MaxProducts} products: those of the businesses it shows last are left out.", MaxProducts);
                entries.RemoveRange(MaxProducts, entries.Count - MaxProducts);
                break;
            }
        }

        var services = await ServicesAsync(context, snapshot, businesses, cancellationToken, logger);
        return new PublicProductIndex(snapshot.BuiltAt, entries, services);
    }

    /// <summary>
    /// Reads the services of the snapshot's businesses that book online or receive visits (Fase 29I), as their pages show
    /// them: of one that books online, those its booking page offers (the first <see cref="OnlineBookingCalendar.MaxServices"/>
    /// bookable ones, and of them those someone attends while it says who does, Fase 24J); of one that receives visits, those
    /// its store shows as reference prices (the first <see cref="PublicHomeVisitsDto.MaxServices"/>, never the visit itself).
    /// A service on both opens on its booking page. Like the products, one read spans the businesses, each service with its
    /// own business and only the columns its pages already show.
    /// </summary>
    private static async Task<List<ServiceEntry>> ServicesAsync(
        IApplicationDbContext context,
        PublicDirectorySnapshot snapshot,
        IReadOnlyDictionary<Guid, (PublicDirectorySnapshot.Entry entry, int position)> businesses,
        CancellationToken cancellationToken,
        ILogger? logger)
    {
        // The bookable services of some businesses, read before any business is in context (the filters are left out for
        // the whole read, the subqueries too): each with whether its booking page offers it now and whether it is the visit.
        var offered = OnlineBookingCalendar.OfferedServices(context).Select(p => p.Id);
        IQueryable<Product> Bookable(IReadOnlyCollection<Guid> tenantIds) => OnlineBookingCalendar.BookableServices(context)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => tenantIds.Contains(p.TenantId));
        IQueryable<ServiceRow> Rows(IQueryable<Product> services) => services.Select(p => new ServiceRow(
            p.Id,
            p.TenantId,
            p.Name,
            p.Description,
            p.PriceAmount,
            p.PreparationTimeMinutes,
            p.ImageUrl,
            offered.Contains(p.Id),
            context.TenantSettings.Any(s => s.TenantId == p.TenantId && s.HomeVisitServiceId == p.Id)));

        var entries = new List<ServiceEntry>();
        var showing = snapshot.Entries.Where(entry => entry.Business.BooksOnline || entry.Business.ReceivesVisits).Select(entry => entry.TenantId);
        foreach (var batch in showing.Chunk(BusinessesPerRead))
        {
            var sizes = await Bookable(batch)
                .GroupBy(p => p.TenantId)
                .Select(services => new { TenantId = services.Key, Services = services.Count() })
                .ToListAsync(cancellationToken);
            // Each service with the lists of its business it is on: its booking page, its store.
            var listed = new Dictionary<Guid, (ServiceRow Row, bool Booked, bool Visited)>();
            void List(ServiceRow row, bool booked, bool visited)
            {
                var (_, wasBooked, wasVisited) = listed.GetValueOrDefault(row.Id);
                listed[row.Id] = (row, wasBooked || booked, wasVisited || visited);
            }

            var whole = sizes.Where(business => business.Services <= WholeServices).Select(business => business.TenantId).ToList();
            if (whole.Count > 0)
            {
                foreach (var row in await Rows(Bookable(whole)).ToListAsync(cancellationToken))
                    List(row, booked: true, visited: !row.IsVisit);
            }

            foreach (var large in sizes.Where(business => business.Services > WholeServices))
            {
                var business = businesses[large.TenantId].entry.Business;
                if (business.BooksOnline)
                {
                    // The booking page's cut (GetPublicBookingQuery): by its order, before who attends.
                    var page = Bookable([large.TenantId]).OrderBy(p => p.SortOrder).ThenBy(p => p.Name).Take(OnlineBookingCalendar.MaxServices);
                    foreach (var row in await Rows(page).ToListAsync(cancellationToken))
                        List(row, booked: true, visited: false);
                }

                if (business.ReceivesVisits)
                {
                    // The store's cut (PublicHomeVisits): the featured first, never the visit.
                    var store = Bookable([large.TenantId])
                        .Where(p => !context.TenantSettings.Any(s => s.TenantId == p.TenantId && s.HomeVisitServiceId == p.Id))
                        .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name).ThenBy(p => p.Id)
                        .Take(PublicHomeVisitsDto.MaxServices);
                    foreach (var row in await Rows(store).ToListAsync(cancellationToken))
                        List(row, booked: false, visited: true);
                }
            }

            foreach (var (row, booked, visited) in listed.Values.OrderBy(service => businesses[service.Row.TenantId].position))
            {
                var (business, position) = businesses[row.TenantId];
                PublicDirectoryServiceKind kind;
                int? minutes;
                if (business.Business.BooksOnline && booked && row.Offered)
                {
                    kind = PublicDirectoryServiceKind.BooksOnline;
                    minutes = (int)OnlineBookingRules.ServiceDuration(row.PreparationTimeMinutes).TotalMinutes;
                }
                else if (business.Business.ReceivesVisits && visited)
                {
                    kind = PublicDirectoryServiceKind.HomeVisit;
                    minutes = row.PreparationTimeMinutes;
                }
                else
                {
                    continue;
                }

                entries.Add(new ServiceEntry(
                    business,
                    position,
                    row.Id,
                    row.Name,
                    row.PriceAmount,
                    minutes,
                    ProductImageUrlsDto.FromBaseUrl(row.ImageUrl)?.Small,
                    kind,
                    PublicDirectorySnapshot.Comparable(row.Name),
                    PublicDirectorySnapshot.Comparable($"{row.Name} {row.Description}")));
            }

            if (entries.Count > MaxServices)
            {
                logger?.LogWarning("Explorar searches more than {MaxServices} services: those of the businesses it shows last are left out.", MaxServices);
                entries.RemoveRange(MaxServices, entries.Count - MaxServices);
                break;
            }
        }

        return entries;
    }

    /// <summary>
    /// The products whose name or description has every word (as <see cref="PublicDirectorySnapshot.Comparable"/> writes
    /// them), of the businesses of <paramref name="category"/>: those whose name has them first, then by the order of
    /// Explorar (taking orders now first) and by name; at most <see cref="MaxPerBusiness"/> of one business and
    /// <see cref="MaxResults"/> in all.
    /// </summary>
    /// <param name="near">
    /// With the customer's position (Fase 24G): after the products named so, those of the nearest businesses first, each
    /// with how far its business is.
    /// </param>
    public List<PublicDirectoryProductDto> Search(IReadOnlyCollection<string> words, BusinessCategory? category, PublicDirectoryNearby? near = null)
    {
        if (words.Count == 0)
            return [];

        var found = _entries
            .Where(entry => category is null || entry.Business.Business.Category == category)
            .Where(entry => words.All(entry.SearchText.Contains))
            .OrderBy(entry => words.All(entry.NameText.Contains) ? 0 : 1)
            // The index may be of a newer snapshot than the one this search read (PublicProductIndexCache): a business it
            // has that the older snapshot lacks has no place in that order, and goes last.
            .ThenBy(entry => near is null ? entry.Position : near.Position.GetValueOrDefault(entry.Business.TenantId, int.MaxValue))
            .ThenBy(entry => entry.NameText, StringComparer.Ordinal);
        var perBusiness = new Dictionary<Guid, int>();
        var products = new List<PublicDirectoryProductDto>();
        foreach (var entry in found)
        {
            var business = entry.Business;
            var shown = perBusiness.GetValueOrDefault(business.TenantId);
            if (shown == MaxPerBusiness)
                continue;
            perBusiness[business.TenantId] = shown + 1;
            products.Add(new PublicDirectoryProductDto
            {
                Id = entry.ProductId,
                Name = entry.Name,
                Price = entry.Price,
                CompareAtPrice = entry.CompareAtPrice,
                ImageUrl = entry.ImageUrl,
                BusinessName = business.Business.Name,
                BusinessSlug = business.Business.Slug,
                Theme = business.Business.Theme,
                AcceptsOrders = business.Business.AcceptsOrders,
                AddsTax = entry.AddsTax,
                DistanceKm = near is not null && near.DistanceKm.TryGetValue(business.TenantId, out var km) ? km : null
            });
            if (products.Count == MaxResults)
                break;
        }

        return products;
    }

    /// <summary>
    /// The businesses whose services that receive visits have every word, in <paramref name="category"/> (Fase 29I): with the
    /// customer's postal code, a search asks them whether they go there (<see cref="SearchServices"/>).
    /// </summary>
    internal IReadOnlyList<PublicDirectorySnapshot.Entry> VisitingWith(IReadOnlyCollection<string> words, BusinessCategory? category)
        => words.Count == 0
            ? []
            : _services
                .Where(service => service.Kind == PublicDirectoryServiceKind.HomeVisit)
                .Where(service => category is null || service.Business.Business.Category == category)
                .Where(service => words.All(service.SearchText.Contains))
                .Select(service => service.Business)
                .DistinctBy(business => business.TenantId)
                .ToList();

    /// <summary>
    /// The services whose name or description has every word, of the businesses of <paramref name="category"/> (Fase 29I),
    /// as the products: those whose name has them first, then by the order of Explorar and by name; at most
    /// <see cref="MaxPerBusiness"/> of one business and <see cref="MaxResults"/> in all.
    /// </summary>
    /// <param name="near">With the customer's position: the nearest businesses first, each with how far it is.</param>
    /// <param name="unreached">
    /// With the customer's postal code: the businesses that receive visits and do not go there. Their services go after the
    /// rest, as their cards do.
    /// </param>
    public List<PublicDirectoryServiceDto> SearchServices(
        IReadOnlyCollection<string> words, BusinessCategory? category, PublicDirectoryNearby? near = null, IReadOnlySet<Guid>? unreached = null)
    {
        if (words.Count == 0)
            return [];

        var found = _services
            .Where(service => category is null || service.Business.Business.Category == category)
            .Where(service => words.All(service.SearchText.Contains))
            .OrderBy(service => service.Kind == PublicDirectoryServiceKind.HomeVisit && unreached?.Contains(service.Business.TenantId) == true ? 1 : 0)
            .ThenBy(service => words.All(service.NameText.Contains) ? 0 : 1)
            .ThenBy(service => near is null ? service.Position : near.Position.GetValueOrDefault(service.Business.TenantId, int.MaxValue))
            .ThenBy(service => service.NameText, StringComparer.Ordinal);
        var perBusiness = new Dictionary<Guid, int>();
        var services = new List<PublicDirectoryServiceDto>();
        foreach (var service in found)
        {
            var business = service.Business;
            var shown = perBusiness.GetValueOrDefault(business.TenantId);
            if (shown == MaxPerBusiness)
                continue;
            perBusiness[business.TenantId] = shown + 1;
            services.Add(new PublicDirectoryServiceDto
            {
                Id = service.ServiceId,
                Name = service.Name,
                Price = service.Price,
                DurationMinutes = service.DurationMinutes,
                ImageUrl = service.ImageUrl,
                Kind = service.Kind,
                AddsTax = service.Kind == PublicDirectoryServiceKind.HomeVisit && !business.Business.PricesIncludeTax,
                BusinessName = business.Business.Name,
                BusinessSlug = business.Business.Slug,
                Theme = business.Business.Theme,
                DistanceKm = near is not null && near.DistanceKm.TryGetValue(business.TenantId, out var km) ? km : null
            });
            if (services.Count == MaxResults)
                break;
        }

        return services;
    }

    /// <summary>A product as the index reads it: the columns its menu shows, and the IVA of its business.</summary>
    private sealed record ProductRow(
        Guid Id,
        Guid TenantId,
        string Name,
        string? Description,
        decimal PriceAmount,
        decimal? CompareAtPrice,
        string? ImageUrl,
        decimal? TaxRate,
        bool IsTaxExempt,
        decimal DefaultTaxRate);

    /// <summary>
    /// A bookable service as the index reads it: the columns its pages show, whether its booking page offers it now and
    /// whether it is the visit of its business.
    /// </summary>
    private sealed record ServiceRow(
        Guid Id,
        Guid TenantId,
        string Name,
        string? Description,
        decimal PriceAmount,
        int? PreparationTimeMinutes,
        string? ImageUrl,
        bool Offered,
        bool IsVisit);

    /// <param name="Position">Its business's place in Explorar.</param>
    /// <param name="NameText">Its name as a search compares it.</param>
    /// <param name="SearchText">Its name and description as a search compares them.</param>
    private sealed record ServiceEntry(
        PublicDirectorySnapshot.Entry Business,
        int Position,
        Guid ServiceId,
        string Name,
        decimal Price,
        int? DurationMinutes,
        string? ImageUrl,
        PublicDirectoryServiceKind Kind,
        string NameText,
        string SearchText);

    /// <param name="Position">Its business's place in Explorar: those taking orders now first.</param>
    /// <param name="AddsTax">The order adds IVA to its price.</param>
    /// <param name="NameText">Its name as a search compares it.</param>
    /// <param name="SearchText">Its name and description as a search compares them.</param>
    private sealed record Entry(
        PublicDirectorySnapshot.Entry Business,
        int Position,
        Guid ProductId,
        string Name,
        decimal Price,
        decimal? CompareAtPrice,
        string? ImageUrl,
        bool AddsTax,
        string NameText,
        string SearchText);
}

/// <summary>
/// The product index of the current snapshot of Explorar, shared by every request of this instance: built by the first
/// search after a snapshot is rebuilt, one request at a time, so a product a business changes shows within
/// <see cref="PublicDirectoryCache.Lifetime"/> too. Explorar without a search never reads it.
/// </summary>
public sealed class PublicProductIndexCache
{
    private static readonly TimeSpan RebuildTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _rebuild = new(1, 1);
    private PublicProductIndex? _index;
    // The snapshot whose index could not be built: searches with it do not try again (each would wait its time limit).
    private DateTimeOffset? _failedFor;

    /// <summary>
    /// The index of <paramref name="snapshot"/>, or of a newer one when another search already built it (a search that
    /// read the snapshot just before it was rebuilt never puts back the older index). Null when building it for this
    /// snapshot failed: the search answers without products until the next snapshot.
    /// </summary>
    /// <exception cref="Exception">What building it threw, the first time.</exception>
    public async Task<PublicProductIndex?> GetAsync(
        PublicDirectorySnapshot snapshot,
        Func<PublicDirectorySnapshot, CancellationToken, Task<PublicProductIndex>> build,
        CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _index);
        if (current is not null && current.BuiltFor >= snapshot.BuiltAt)
            return current;

        await _rebuild.WaitAsync(cancellationToken);
        try
        {
            // Another search may have built it while this one waited, or failed to.
            current = Volatile.Read(ref _index);
            if (current is not null && current.BuiltFor >= snapshot.BuiltAt)
                return current;
            if (_failedFor >= snapshot.BuiltAt)
                return null;
            // Its own time limit, as the snapshot's: a customer who changes the search does not throw it away half done.
            using var limit = new CancellationTokenSource(RebuildTimeout);
            try
            {
                current = await build(snapshot, limit.Token);
            }
            catch
            {
                _failedFor = snapshot.BuiltAt;
                throw;
            }

            Volatile.Write(ref _index, current);
            return current;
        }
        finally
        {
            _rebuild.Release();
        }
    }
}

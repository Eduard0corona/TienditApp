using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Catalog.Entities;
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
}

/// <summary>
/// The products of every business of a snapshot of Explorar, as their menus show them, to search by words: built from the
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

    private readonly IReadOnlyList<Entry> _entries;

    private PublicProductIndex(DateTimeOffset builtFor, IReadOnlyList<Entry> entries)
    {
        BuiltFor = builtFor;
        _entries = entries;
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

        return new PublicProductIndex(snapshot.BuiltAt, entries);
    }

    /// <summary>
    /// The products whose name or description has every word (as <see cref="PublicDirectorySnapshot.Comparable"/> writes
    /// them), of the businesses of <paramref name="category"/>: those whose name has them first, then by the order of
    /// Explorar (taking orders now first) and by name; at most <see cref="MaxPerBusiness"/> of one business and
    /// <see cref="MaxResults"/> in all.
    /// </summary>
    public List<PublicDirectoryProductDto> Search(IReadOnlyCollection<string> words, BusinessCategory? category)
    {
        if (words.Count == 0)
            return [];

        var found = _entries
            .Where(entry => category is null || entry.Business.Business.Category == category)
            .Where(entry => words.All(entry.SearchText.Contains))
            .OrderBy(entry => words.All(entry.NameText.Contains) ? 0 : 1)
            .ThenBy(entry => entry.Position)
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
                AddsTax = entry.AddsTax
            });
            if (products.Count == MaxResults)
                break;
        }

        return products;
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

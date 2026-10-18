using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
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
    /// <summary>Its prices include IVA (its menu writes «más IVA» when they do not).</summary>
    public bool PricesIncludeTax { get; set; }
}

/// <summary>
/// The products of every business of a snapshot of Explorar, as their menus show them, to search by words: built from the
/// snapshot when a search first needs it, and rebuilt with the next snapshot (<see cref="PublicProductIndexCache"/>).
/// </summary>
public sealed class PublicProductIndex
{
    /// <summary>The most products one index reads; past it, the last ones by business are left out (and logged).</summary>
    public const int MaxProducts = 20_000;

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
    /// Reads the products the menus of the snapshot's businesses show (<see cref="PublicDirectorySnapshot.MenuProducts"/>):
    /// a public request has no business, so the read spans them all, each product with its own business and only the
    /// columns its menu already shows.
    /// </summary>
    /// <param name="logger">Says when there were more products than one index reads.</param>
    public static async Task<PublicProductIndex> BuildAsync(
        IApplicationDbContext context, PublicDirectorySnapshot snapshot, CancellationToken cancellationToken, ILogger? logger = null)
    {
        var businesses = snapshot.Entries
            .Select((entry, position) => (entry, position))
            .ToDictionary(item => item.entry.TenantId);
        var tenantIds = businesses.Keys.ToList();
        if (tenantIds.Count == 0)
            return new PublicProductIndex(snapshot.BuiltAt, []);

        var rows = await PublicDirectorySnapshot.MenuProducts(context)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => tenantIds.Contains(p.TenantId))
            .OrderBy(p => p.TenantId).ThenByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Take(MaxProducts + 1)
            .Select(p => new { p.Id, p.TenantId, p.Name, p.Description, p.PriceAmount, p.CompareAtPrice, p.ImageUrl })
            .ToListAsync(cancellationToken);
        if (rows.Count > MaxProducts)
        {
            logger?.LogWarning("Explorar searches more than {MaxProducts} products: the ones after it are left out.", MaxProducts);
            rows.RemoveAt(MaxProducts);
        }

        var entries = rows.Select(row =>
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
                PublicDirectorySnapshot.Comparable(row.Name),
                PublicDirectorySnapshot.Comparable($"{row.Name} {row.Description}"));
        }).ToList();
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
                PricesIncludeTax = business.Business.PricesIncludeTax
            });
            if (products.Count == MaxResults)
                break;
        }

        return products;
    }

    /// <param name="Position">Its business's place in Explorar: those taking orders now first.</param>
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

    public async Task<PublicProductIndex> GetAsync(
        PublicDirectorySnapshot snapshot,
        Func<PublicDirectorySnapshot, CancellationToken, Task<PublicProductIndex>> build,
        CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _index);
        if (current?.BuiltFor == snapshot.BuiltAt)
            return current;

        await _rebuild.WaitAsync(cancellationToken);
        try
        {
            // Another search may have built it while this one waited.
            current = Volatile.Read(ref _index);
            if (current?.BuiltFor == snapshot.BuiltAt)
                return current;
            // Its own time limit, as the snapshot's: a customer who changes the search does not throw it away half done.
            using var limit = new CancellationTokenSource(RebuildTimeout);
            current = await build(snapshot, limit.Token);
            Volatile.Write(ref _index, current);
            return current;
        }
        finally
        {
            _rebuild.Release();
        }
    }
}

using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>
/// What search engines and the previews of a shared link read of a store (/m/{slug}) before its page runs (Fase 24D-2,
/// ADR 0027): only what its public menu already shows to anyone with the link.
/// </summary>
public class StorePreviewDto
{
    public string BusinessName { get; set; } = null!;
    public string Slug { get; set; } = null!;
    /// <summary>What it sells, in one line written by its owner (its line in Explorar).</summary>
    public string? Description { get; set; }
    public BusinessCategory Category { get; set; }
    /// <summary>Its logo, 512 px.</summary>
    public string? LogoUrl { get; set; }
    /// <summary>Its cover across the menu, 1024 px wide: the preview of a shared link.</summary>
    public string? CoverImageUrl { get; set; }
    /// <summary>
    /// Explorar lists it, by the rule of Explorar: its owner shows it there, and its menu has a product or it books online.
    /// Search engines may list its pages. One that only shares its link (a QR, its WhatsApp) keeps the preview of the
    /// link, but stays out of search engines and the sitemap.
    /// </summary>
    public bool ListedInDirectory { get; set; }
    /// <summary>Its menu takes orders (whether or not it is open right now); otherwise it only shows the menu.</summary>
    public bool TakesOrders { get; set; }
    /// <summary>Customers book appointments with it online now: its booking page is /m/{slug}/agendar.</summary>
    public bool BooksOnline { get; set; }
    /// <summary>Where it sells, as its menu shows it; none for a kitchen that only delivers.</summary>
    public StoreAddressDto? Address { get; set; }
    /// <summary>The phone its menu offers for WhatsApp; none without one.</summary>
    public string? ContactPhone { get; set; }
    /// <summary>Its weekly hours, as its menu shows them; empty without hours. Special days are left out.</summary>
    public List<StoreOpeningDto> OpeningHours { get; set; } = new();
}

/// <summary>The address of its main branch, in its parts, for the structured data of search engines.</summary>
public class StoreAddressDto
{
    /// <summary>The street and number, with the second line when it has one.</summary>
    public string Street { get; set; } = null!;
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>The country as the owner wrote it; none means Mexico.</summary>
    public string? Country { get; set; }
}

/// <summary>One opening of its week: a day and its wall-clock times; closes before it opens past midnight.</summary>
public class StoreOpeningDto
{
    public DayOfWeek Day { get; set; }
    public TimeOnly Opens { get; set; }
    public TimeOnly Closes { get; set; }
}

/// <summary>
/// The preview of a store for its page (/m/{slug} and /m/{slug}/agendar): null when no active business has that slug
/// or its menu is not published, as its menu answers.
/// </summary>
[AllowAnonymousRequest("Vista previa de la tienda para buscadores y enlaces compartidos: solo lo que su menú público ya muestra.")]
public record GetStorePreviewQuery(string Slug) : IRequest<StorePreviewDto?>;

public class GetStorePreviewQueryHandler : IRequestHandler<GetStorePreviewQuery, StorePreviewDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetStorePreviewQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<StorePreviewDto?> Handle(GetStorePreviewQuery request, CancellationToken cancellationToken)
    {
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return null;

        // A public request has no tenant: the queries below are scoped to the business of the slug, as its menu's.
        _tenantService.SetTenant(tenant.Id);
        var settings = tenant.Settings;
        var mainBranch = await _context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenant.Id && b.IsDefault, cancellationToken);
        // The rule of its booking page and its menu (Fase 24D), and a service to book.
        var booksOnline = OnlineBookingAvailability.BooksOnline(
                settings.OnlineBookingEnabled,
                TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, tenant.BusinessType),
                settings.OnlineMenuMode,
                mainBranch?.ServiceMode,
                settings.OpeningHours is not null)
            && await OnlineBookingCalendar.BookableServices(_context).AnyAsync(cancellationToken);
        // Listed as Explorar lists it (PublicDirectorySnapshot): shown there by its owner, with something to order or book.
        var listed = settings.ListedInDirectory
            && (booksOnline || await PublicDirectorySnapshot.MenuProducts(_context).AnyAsync(cancellationToken));

        return new StorePreviewDto
        {
            BusinessName = tenant.Name,
            Slug = tenant.Slug,
            Description = tenant.PublicDescription,
            Category = tenant.DirectoryCategory,
            LogoUrl = string.IsNullOrEmpty(tenant.LogoUrl) ? null : $"{tenant.LogoUrl}_md.webp",
            CoverImageUrl = LogoImageUrlsDto.FromBaseUrl(tenant.CoverImageUrl)?.Large,
            ListedInDirectory = listed,
            TakesOrders = settings.OnlineMenuMode == OnlineMenuMode.Orders,
            BooksOnline = booksOnline,
            // The address its menu shows (AddressText.ForCustomers): never the one of a branch that only delivers.
            Address = mainBranch is null || mainBranch.KeepsAddressPrivate || string.IsNullOrWhiteSpace(mainBranch.AddressLine1)
                ? null
                : new StoreAddressDto
                {
                    Street = string.Join(", ", new[] { mainBranch.AddressLine1, mainBranch.AddressLine2 }
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => line!.Trim())),
                    City = Blank(mainBranch.City),
                    State = Blank(mainBranch.State),
                    PostalCode = Blank(mainBranch.PostalCode),
                    Country = Blank(mainBranch.Country)
                },
            ContactPhone = Blank(tenant.ContactPhone),
            OpeningHours = (settings.OpeningHours?.Intervals ?? [])
                .OrderBy(interval => ((int)interval.Day + 6) % 7)
                .ThenBy(interval => interval.Opens)
                .Select(interval => new StoreOpeningDto { Day = interval.Day, Opens = interval.Opens, Closes = interval.Closes })
                .ToList()
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The pages a sitemap lists (Fase 24D-2): Explorar, its categories with businesses, and each store.</summary>
public class PublicSitemapDto
{
    /// <summary>The categories with at least one business in Explorar, in the order of their chips.</summary>
    public List<BusinessCategory> Categories { get; set; } = new();

    /// <summary>The stores Explorar lists, by their address (slug).</summary>
    public List<PublicSitemapStoreDto> Stores { get; set; } = new();
}

public class PublicSitemapStoreDto
{
    /// <summary>Its page: /m/{slug}.</summary>
    public string Slug { get; set; } = null!;
    /// <summary>It books appointments online: its booking page /m/{slug}/agendar is listed too.</summary>
    public bool BooksOnline { get; set; }
}

/// <summary>
/// The sitemap of search engines: the businesses Explorar lists now (its snapshot, rebuilt at most every 30 seconds),
/// never one that the owner keeps out of it.
/// </summary>
[AllowAnonymousRequest("Mapa del sitio para buscadores: los negocios que aparecen en Explorar, solo su dirección pública.")]
public record GetPublicSitemapQuery : IRequest<PublicSitemapDto>;

public class GetPublicSitemapQueryHandler : IRequestHandler<GetPublicSitemapQuery, PublicSitemapDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly PublicDirectoryCache _cache;
    private readonly ILogger<GetPublicSitemapQueryHandler> _logger;

    public GetPublicSitemapQueryHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        PublicDirectoryCache cache,
        ILogger<GetPublicSitemapQueryHandler> logger)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PublicSitemapDto> Handle(GetPublicSitemapQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await _cache.GetAsync(
            _dateTimeProvider.UtcNow, (utcNow, ct) => PublicDirectorySnapshot.BuildAsync(_context, utcNow, ct, _logger), cancellationToken);

        return new PublicSitemapDto
        {
            Categories = snapshot.Categories.Where(category => category.Count > 0).Select(category => category.Category).ToList(),
            Stores = snapshot.Entries
                .OrderBy(entry => entry.Business.Slug, StringComparer.Ordinal)
                .Select(entry => new PublicSitemapStoreDto { Slug = entry.Business.Slug, BooksOnline = entry.Business.BooksOnline })
                .ToList()
        };
    }
}

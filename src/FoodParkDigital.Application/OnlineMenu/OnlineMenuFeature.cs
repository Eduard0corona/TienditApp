using System.Globalization;
using System.Text.Json;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Reviews;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineMenu;

public class OnlineMenuSettingsDto
{
    public OnlineMenuMode Mode { get; set; }
    /// <summary>The public path of the menu: /m/{slug}.</summary>
    public string Slug { get; set; } = null!;
}

/// <summary>Turns the public menu off, on, or on with orders (owners and admins).</summary>
[RequiresTenantManager]
public record UpdateOnlineMenuCommand(OnlineMenuMode Mode) : IRequest<Result<OnlineMenuSettingsDto>>;

public class UpdateOnlineMenuCommandValidator : AbstractValidator<UpdateOnlineMenuCommand>
{
    public UpdateOnlineMenuCommandValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
    }
}

public class UpdateOnlineMenuCommandHandler : IRequestHandler<UpdateOnlineMenuCommand, Result<OnlineMenuSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public UpdateOnlineMenuCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OnlineMenuSettingsDto>> Handle(UpdateOnlineMenuCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<OnlineMenuSettingsDto>("Negocio no encontrado.");

        var settings = tenant.Settings;
        if (settings is null)
        {
            settings = new TenantSettings(tenant.Id, TenantCapabilityDefaults.FromBusinessType(tenant.BusinessType));
            _context.TenantSettings.Add(settings);
        }

        var previous = settings.OnlineMenuMode;
        settings.UpdateOnlineMenu(request.Mode);
        if (previous != request.Mode)
        {
            // Publishing the catalog and taking orders from strangers is a decision worth recording.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenant.Id,
                entityName: nameof(TenantSettings),
                entityId: tenant.Id.ToString(),
                action: "OnlineMenuUpdated",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { Previous = previous.ToString(), New = request.Mode.ToString() })));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new OnlineMenuSettingsDto { Mode = settings.OnlineMenuMode, Slug = tenant.Slug });
    }
}

public class PublicMenuDto
{
    public string BusinessName { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? LogoUrl { get; set; }
    /// <summary>
    /// The wide photo across the top of the menu (Fase 24E, ADR 0026), 1024 px wide; null without one, and the menu
    /// shows the colors of the business instead.
    /// </summary>
    public string? CoverImageUrl { get; set; }
    /// <summary>The same cover up to 2048 px wide, for a wide screen; null without one.</summary>
    public string? CoverImageWideUrl { get; set; }
    /// <summary>Its line under the name, as on its card in Explorar ("Tacos al pastor desde 1998"); null without one.</summary>
    public string? Description { get; set; }
    /// <summary>What it is, as Explorar lists it (Comida, Abarrotes…), under its name.</summary>
    public BusinessCategory Category { get; set; }
    /// <summary>The business phone for "Escríbenos por WhatsApp", when it has one.</summary>
    public string? ContactPhone { get; set; }
    public string Currency { get; set; } = "MXN";
    public bool PricesIncludeTax { get; set; }
    public decimal DefaultTaxRate { get; set; }
    /// <summary>The menu takes orders now; otherwise it only shows the catalog.</summary>
    public bool AcceptsOrders { get; set; }
    /// <summary>
    /// The business takes online orders, but not right now: it is closed, outside its hours or on a special day
    /// (<see cref="OpeningHours"/> says why and when it opens), or the branch they arrive at has no open drawer to
    /// charge them in.
    /// </summary>
    public bool OrdersPaused { get; set; }
    /// <summary>
    /// The customer may pay the order online, in Mercado Pago (Fase 24I): the business connected its account and turned it
    /// on. Only to pick it up or to have it delivered, never at a table.
    /// </summary>
    public bool PaysOnline { get; set; }
    /// <summary>The colors the business picked: its customers see the menu with them.</summary>
    public BusinessTheme Theme { get; set; }
    /// <summary>
    /// Where the main branch is, in one line, to show it and open it in a map; null without a street or when the branch
    /// only delivers. Never the fiscal address of the business.
    /// </summary>
    public string? Address { get; set; }
    /// <summary>
    /// Where the main branch is on the map (Fase 24G), so «Cómo llegar» opens its exact point; null when its owner has not
    /// put it there or it only delivers.
    /// </summary>
    public MapPointDto? MapPoint { get; set; }
    /// <summary>
    /// The main branch only delivers: it has no premises to visit, and its address (<see cref="Address"/> stays null) is
    /// never published.
    /// </summary>
    public bool DeliveryOnly { get; set; }
    /// <summary>
    /// The menu takes orders to an address (Fase 22C): it takes orders and the business has an active delivery zone. The
    /// customer writes a postal code (GetPublicDeliveryQuoteQuery says whether it is reached and what it costs). Without a
    /// premises (<see cref="DeliveryOnly"/>) it is the only way to order, except from a table.
    /// </summary>
    public bool DeliveryAvailable { get; set; }
    /// <summary>
    /// Its hours and whether it is open now. Null when the business has no hours and no special day today or in the
    /// next 31 days.
    /// </summary>
    public PublicOpeningHoursDto? OpeningHours { get; set; }
    /// <summary>Customers book appointments online with the business now (Fase 24D): /m/{slug}/agendar.</summary>
    public bool BooksOnline { get; set; }
    /// <summary>How its customers rate it (Fase 24H): its published reviews and their average; null without reviews.</summary>
    public RatingSummaryDto? Rating { get; set; }
    public List<PublicMenuCategoryDto> Categories { get; set; } = new();
}

/// <summary>
/// The hours of the business on its public menu, worked out in its own time zone: its weekly hours with its special
/// days on top. The server writes the words (<see cref="StatusLabel"/>, <see cref="StatusDetail"/> and the notices);
/// an API that does not send <see cref="TodayDate"/> left them to the client.
/// </summary>
public class PublicOpeningHoursDto
{
    /// <summary>How far ahead <see cref="SpecialDays"/> looks, and how many it lists at most.</summary>
    public const int SpecialDaysAhead = 31;
    public const int MaxSpecialDays = 5;

    /// <summary>The weekly hours; empty when the business has only special days.</summary>
    public List<OpeningIntervalDto> Intervals { get; set; } = new();
    /// <summary>Today in the business, for "Hoy" in the hours.</summary>
    public DayOfWeek Today { get; set; }
    public bool IsOpen { get; set; }
    /// <summary>While open: when it closes ("20:00") and in how many days (0 today); null while it never closes.</summary>
    public string? ClosesAt { get; set; }
    public int? ClosesInDays { get; set; }
    /// <summary>While closed: when it opens next ("09:00") and in how many days (0 today, 1 tomorrow).</summary>
    public string? OpensAt { get; set; }
    public int? OpensInDays { get; set; }
    /// <summary>Today in the business. The words below come from the server when it is present.</summary>
    public DateOnly? TodayDate { get; set; }
    public bool? HasWeeklyHours { get; set; }
    /// <summary>
    /// For the chip: "Abierto", "Abierto las 24 horas" or "Cerrado". Null without weekly hours and without a special day
    /// today: there is nothing to say then.
    /// </summary>
    public string? StatusLabel { get; set; }
    /// <summary>After the chip: "cierra a las 18:00 por Nochebuena", "por Navidad · abre el sábado 26 a las 9:00".</summary>
    public string? StatusDetail { get; set; }
    /// <summary>While closed and taking no orders: why, when to come back, and that the menu can still be seen.</summary>
    public string? ClosedNotice { get; set; }
    /// <summary>The next special day within a week, in one line: "El viernes 25 cerramos por Navidad."</summary>
    public string? UpcomingNotice { get; set; }
    /// <summary>
    /// Closed by a closed special day (today's, or yesterday's while the night it took away would still run): not even
    /// a customer at a table orders.
    /// </summary>
    public bool? ClosedBySpecialDay { get; set; }
    /// <summary>The openings that start today, its special day's or its weekday's; null when <see cref="StatusLabel"/> is.</summary>
    public PublicDayHoursDto? TodayHours { get; set; }
    /// <summary>The dates of <see cref="OpensAt"/> and <see cref="ClosesAt"/>.</summary>
    public DateOnly? OpensOn { get; set; }
    public DateOnly? ClosesOn { get; set; }
    /// <summary>
    /// The special days of the next 31 days (at most 5), by first date. One under way that lasts after today starts
    /// today, with the days it has left; today alone is <see cref="TodayHours"/>.
    /// </summary>
    public List<PublicSpecialDayDto>? SpecialDays { get; set; }

    /// <summary>
    /// Null without weekly hours and without a special day today or in the next 31 days: such a business closes only on
    /// its special days, so this hides no closing (and the order still asks the whole schedule).
    /// </summary>
    /// <param name="seatedOverride">The business is closed, but the customer at one of its tables still orders.</param>
    public static PublicOpeningHoursDto? From(BusinessHoursNow hours, bool seatedOverride)
    {
        var specialDays = hours.FromTodayWithin(SpecialDaysAhead)
            .Where(period => period.To > hours.Today)
            .Take(MaxSpecialDays)
            .Select(period => new PublicSpecialDayDto
            {
                From = period.From,
                To = period.To,
                Kind = period.Rule.Kind,
                Openings = period.Rule.Openings.ToDtos(),
                Reason = period.Rule.Reason
            })
            .ToList();
        var mayWordStatus = BusinessHoursText.MayWordStatus(hours);
        if (!mayWordStatus && specialDays.Count == 0)
            return null;

        var status = hours.Status;
        var today = hours.Today;
        var todayOpenings = hours.Schedule.OpeningsOn(today);
        return new PublicOpeningHoursDto
        {
            Intervals = hours.Weekly?.ToDtos() ?? new(),
            Today = today.DayOfWeek,
            IsOpen = status.IsOpen,
            ClosesAt = status.ClosesAt?.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture),
            ClosesInDays = status.ClosesAt is { } closes ? DateOnly.FromDateTime(closes).DayNumber - today.DayNumber : null,
            OpensAt = status.OpensAt?.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture),
            OpensInDays = status.OpensAt is { } opens ? DateOnly.FromDateTime(opens).DayNumber - today.DayNumber : null,
            TodayDate = today,
            HasWeeklyHours = hours.HasWeeklyHours,
            StatusLabel = BusinessHoursText.StatusLabel(hours),
            StatusDetail = BusinessHoursText.StatusDetail(hours, seatedOverride),
            ClosedNotice = BusinessHoursText.ClosedNotice(hours, seatedOverride),
            UpcomingNotice = BusinessHoursText.UpcomingNotice(hours),
            ClosedBySpecialDay = status.ClosedBySpecialDay,
            TodayHours = mayWordStatus
                ? new PublicDayHoursDto
                {
                    Date = today,
                    Closed = todayOpenings.Count == 0,
                    Special = status.TodaySpecial is not null,
                    Openings = todayOpenings.ToDtos(),
                    Reason = status.TodaySpecial?.Reason
                }
                : null,
            OpensOn = status.OpensAt is { } opensOn ? DateOnly.FromDateTime(opensOn) : null,
            ClosesOn = status.ClosesAt is { } closesOn ? DateOnly.FromDateTime(closesOn) : null,
            SpecialDays = specialDays
        };
    }
}

/// <summary>The hours of the business's today on its public menu: its special day's, or else its weekday's.</summary>
public class PublicDayHoursDto
{
    public DateOnly Date { get; set; }
    /// <summary>No opening starts today (one that started yesterday may still run past midnight).</summary>
    public bool Closed { get; set; }
    /// <summary>Today is a special day: its hours replace the weekly ones.</summary>
    public bool Special { get; set; }
    public List<SpecialOpeningDto> Openings { get; set; } = new();
    /// <summary>Why, on a special day ("Navidad").</summary>
    public string? Reason { get; set; }
}

/// <summary>A special day ahead on the public menu: one date, or consecutive dates with one rule.</summary>
public class PublicSpecialDayDto
{
    public DateOnly From { get; set; }
    /// <summary>The last date; the same as <see cref="From"/> for one day.</summary>
    public DateOnly To { get; set; }
    public SpecialDayKind Kind { get; set; }
    /// <summary>One or two with special hours; none when closed.</summary>
    public List<SpecialOpeningDto> Openings { get; set; } = new();
    public string? Reason { get; set; }
}

public class PublicMenuCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public List<PublicMenuProductDto> Products { get; set; } = new();
}

public class PublicMenuProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public decimal? TaxRate { get; set; }
    public bool IsTaxExempt { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>The same photo 1024 px wide, for the product's own sheet (Fase 24E); null without a photo.</summary>
    public string? ImageLargeUrl { get; set; }
    public Domain.Catalog.Enums.UnitType? Unit { get; set; }
    public bool IsFeatured { get; set; }
    /// <summary>False outside its hours (it is shown but cannot be ordered now).</summary>
    public bool AvailableNow { get; set; }
    public string? Allergens { get; set; }
    public List<ProductOptionGroupDto> OptionGroups { get; set; } = new();
}

/// <summary>
/// The public menu of a business, by its slug. Only what the business published: available products of
/// active categories (services are not ordered online). Null when the business has no public menu.
/// </summary>
/// <param name="Table">
/// The table of the QR the customer opened it from. At a table of the business the customer is already seated, and
/// orders arrive outside the hours (not on a closed special day, see <see cref="PublicOrderRules"/>).
/// </param>
[AllowAnonymousRequest("Menú público del negocio: solo el catálogo que el negocio decidió publicar.")]
public record GetPublicMenuQuery(string Slug, string? Table = null) : IRequest<PublicMenuDto?>;

public class GetPublicMenuQueryHandler : IRequestHandler<GetPublicMenuQuery, PublicMenuDto?>
{
    public const int MaxProducts = 500;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOnlinePaymentGateway _gateway;

    public GetPublicMenuQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider dateTimeProvider, IOnlinePaymentGateway gateway)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
        _gateway = gateway;
    }

    public async Task<PublicMenuDto?> Handle(GetPublicMenuQuery request, CancellationToken cancellationToken)
    {
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return null;

        // A public request has no tenant: the catalog queries below are scoped to the business of the slug.
        _tenantService.SetTenant(tenant.Id);
        var settings = tenant.Settings;
        var utcNow = _dateTimeProvider.UtcNow;
        var localTime = TenantDateTimeResolver.LocalNow(utcNow, settings.TimeZone).TimeOfDay;
        var hours = await BusinessHours.ReadAsync(_context, tenant.Id, settings.OpeningHours, settings.TimeZone, utcNow, cancellationToken);
        var closed = hours is { Status.IsOpen: false };

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);
        // The products a menu shows (the rule Explorar searches, Fase 24F), the first MaxProducts of them in its order: a
        // service or a product of an inactive category does not take the place of one it shows.
        var products = await PublicDirectorySnapshot.InMenuOrder(PublicDirectorySnapshot.MenuProducts(_context))
            .AsNoTracking()
            .Include(p => p.OptionGroups).ThenInclude(g => g.Options)
            .Take(MaxProducts)
            .ToListAsync(cancellationToken);

        var byCategory = products
            .Where(p => !OrderProductClassifier.IsServiceUnit(p.Unit))
            .GroupBy(p => p.CategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());
        // Online orders arrive at the default branch, and only while a drawer is open there (see PlacePublicOrderCommand).
        // It is also the address customers see: where the business sells, never its fiscal address, which is often the
        // owner's home; and none when that branch only delivers.
        var mainBranch = await _context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenant.Id && b.IsDefault, cancellationToken);
        var ordersOn = settings.OnlineMenuMode == OnlineMenuMode.Orders;
        var drawerOpen = ordersOn && await OrderCashService.HasOpenDrawerAtBranchAsync(
            _context, tenant.Id, mainBranch?.Id, cancellationToken);
        // Closed, it takes no pickup or delivery orders; a customer at one of its tables still orders, except on a closed
        // special day. The same rule as its orders and Explorar.
        var seated = drawerOpen
            && OnlineOrderAvailability.TableMayOrderWhileClosed(hours)
            && await PublicMenuTables.IsSeatedAsync(_context, tenant.Id, request.Table, cancellationToken);
        var acceptsOrders = OnlineOrderAvailability.Gate(settings.OnlineMenuMode, hours, drawerOpen, seated) == OnlineOrderGate.Open;
        // The rule of its booking page (Fase 24D), and a service to book.
        var booksOnline = OnlineBookingAvailability.BooksOnline(
                settings.OnlineBookingEnabled,
                TenantCapabilityRules.NormalizeCapabilities(settings.Capabilities, tenant.BusinessType),
                settings.OnlineMenuMode,
                mainBranch?.ServiceMode,
                settings.OpeningHours is not null)
            && await OnlineBookingCalendar.BookableServices(_context).AnyAsync(cancellationToken);

        var cover = LogoImageUrlsDto.FromBaseUrl(tenant.CoverImageUrl);
        return new PublicMenuDto
        {
            BusinessName = tenant.Name,
            Slug = tenant.Slug,
            LogoUrl = string.IsNullOrEmpty(tenant.LogoUrl) ? null : $"{tenant.LogoUrl}_md.webp",
            CoverImageUrl = cover?.Large,
            CoverImageWideUrl = cover?.Original,
            Description = tenant.PublicDescription,
            Category = tenant.DirectoryCategory,
            ContactPhone = tenant.ContactPhone,
            Currency = settings.Currency,
            PricesIncludeTax = settings.PricesIncludeTax,
            DefaultTaxRate = settings.DefaultTaxRate,
            AcceptsOrders = acceptsOrders,
            OrdersPaused = ordersOn && !acceptsOrders,
            PaysOnline = acceptsOrders && _gateway.IsConfigured && OnlineCheckoutRules.Offers(
                await _context.OnlinePaymentAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken), _gateway, utcNow),
            Theme = settings.Theme,
            Address = AddressText.ForCustomers(mainBranch),
            MapPoint = MapPointDto.From(mainBranch?.PublicLocation),
            DeliveryOnly = mainBranch is { KeepsAddressPrivate: true },
            DeliveryAvailable = ordersOn && await DeliveryZoneLookup.AnyActiveAsync(_context, tenant.Id, cancellationToken),
            OpeningHours = hours is null ? null : PublicOpeningHoursDto.From(hours, seatedOverride: acceptsOrders && closed),
            BooksOnline = booksOnline,
            Rating = await ReviewRules.SummaryAsync(_context, cancellationToken),
            Categories = categories
                .Where(c => byCategory.ContainsKey(c.Id))
                .Select(c => new PublicMenuCategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Products = byCategory[c.Id].Select(p => new PublicMenuProductDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        Price = p.PriceAmount,
                        CompareAtPrice = p.HasDiscount ? p.CompareAtPrice : null,
                        TaxRate = p.TaxRate,
                        IsTaxExempt = p.IsTaxExempt,
                        ImageUrl = ProductImageUrlsDto.FromBaseUrl(p.ImageUrl)?.Medium,
                        ImageLargeUrl = ProductImageUrlsDto.FromBaseUrl(p.ImageUrl)?.Large,
                        Unit = p.Unit,
                        IsFeatured = p.IsFeatured,
                        AvailableNow = p.IsAvailableAt(localTime),
                        Allergens = p.Allergens,
                        OptionGroups = p.OptionGroups.OrderBy(g => g.SortOrder).Select(g => new ProductOptionGroupDto
                        {
                            Id = g.Id,
                            Name = g.Name,
                            MinSelections = g.MinSelections,
                            MaxSelections = g.MaxSelections,
                            Options = g.Options.OrderBy(o => o.SortOrder).Select(o => new ProductOptionDto
                            {
                                Id = o.Id,
                                Name = o.Name,
                                PriceDelta = o.PriceDelta,
                                IsAvailable = o.IsAvailable
                            }).ToList()
                        }).ToList()
                    }).ToList()
                })
                .ToList()
        };
    }
}

public class PublicOrderReceiptDto
{
    public string OrderNumber { get; set; } = null!;
    public decimal Total { get; set; }
    /// <summary>What the delivery charged, inside <see cref="Total"/>; null for an order picked up or served.</summary>
    public decimal? DeliveryFee { get; set; }
    /// <summary>True when the business accepts online orders without confirming each one.</summary>
    public bool Confirmed { get; set; }
    public string BusinessName { get; set; } = null!;
    public string? ContactPhone { get; set; }
    /// <summary>
    /// Placed with a customer session (Fase 24B): the order is in the account's orders, whatever phone it carries.
    /// </summary>
    public bool LinkedToAccount { get; set; }
    /// <summary>
    /// The link to follow the order without an account (Fase 24C, ADR 0023): the app opens it at /seguimiento#token, in
    /// the fragment, which never reaches a server log. None for an order placed before the link existed.
    /// </summary>
    public string? TrackingToken { get; set; }
}

/// <summary>
/// An order placed from the public menu. It arrives in Pedidos with the Online channel. With a customer session the order
/// is the account's, without confirming the phone by SMS (Fase 24B); a session of a business's team orders as a guest.
/// </summary>
[AllowAnonymousRequest("Pedido del menú público: el negocio activó los pedidos en línea y el cliente se identifica con su teléfono.")]
public record PlacePublicOrderCommand : IRequest<Result<PublicOrderReceiptDto>>, ITransactionalRequest
{
    public string Slug { get; init; } = null!;
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? Notes { get; init; }
    /// <summary>Table or reference from a table QR ("Mesa 4").</summary>
    public string? TableLabel { get; init; }
    public List<CreateOrderItemDto> Items { get; init; } = new();
    /// <summary>Total the customer saw; the order is rejected if a price changed meanwhile.</summary>
    public decimal? ExpectedTotal { get; init; }
    /// <summary>
    /// Where the order goes (Fase 22C): a zone of the business must reach the postal code and the products its minimum;
    /// its fee is in the total. Never with a table.
    /// </summary>
    public OrderDeliveryInput? Delivery { get; init; }
    /// <summary>
    /// Key the store generates for this order (Fase 24B). Sent again, by a retry after a lost answer, it answers the order
    /// already placed with it instead of placing a second one.
    /// </summary>
    public Guid? IdempotencyKey { get; init; }
    /// <summary>
    /// Keeps the delivery address in the signed-in customer account while it has room (Fase 24B), with
    /// <see cref="AddressLabel"/> as its name. Without a customer session, or without a delivery, nothing is kept.
    /// </summary>
    public bool SaveAddress { get; init; }
    public string? AddressLabel { get; init; }
}

public class PlacePublicOrderCommandValidator : AbstractValidator<PlacePublicOrderCommand>
{
    /// <summary>A public order is smaller than a counter sale.</summary>
    public const int MaxLines = 50;

    public PlacePublicOrderCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CustomerName).NotEmpty().WithMessage("Escribe tu nombre.").MaximumLength(100);
        RuleFor(x => x.CustomerPhone).NotEmpty().WithMessage("Escribe tu teléfono para avisarte de tu pedido.").MaximumLength(30);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.TableLabel).MaximumLength(50);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Agrega al menos un producto.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(MaxLines)
            .WithMessage($"Un pedido en línea admite como máximo {MaxLines} productos distintos.");
        RuleForEach(x => x.Items).SetValidator(new CreateOrderItemDtoValidator());
        RuleFor(x => x.ExpectedTotal).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.ExpectedTotal.HasValue);
        When(x => x.Delivery is not null, () =>
        {
            RuleFor(x => x.Delivery!).SetValidator(new OrderDeliveryInputValidator());
            RuleFor(x => x.TableLabel).Empty().WithMessage(PlacePublicOrderCommandHandler.TableNotDeliveredMessage);
        });
        RuleFor(x => x.IdempotencyKey).NotEqual(Guid.Empty).When(x => x.IdempotencyKey.HasValue)
            .WithMessage("La clave del pedido no es válida.");
        RuleFor(x => x.AddressLabel).MaximumLength(AccountAddressRules.MaxLabelInputLength)
            .WithMessage(AccountAddressRules.LabelTooLongMessage);
    }
}

public class PlacePublicOrderCommandHandler : IRequestHandler<PlacePublicOrderCommand, Result<PublicOrderReceiptDto>>
{
    /// <summary>Orders a phone may leave waiting for the business to confirm them (spam guard).</summary>
    public const int MaxPendingPerPhone = 3;
    public const string OrdersPausedMessage = "Por ahora este negocio no está recibiendo pedidos en línea. Intenta más tarde.";
    public const string TableNotDeliveredMessage = "Un pedido para tu mesa no se manda a domicilio.";
    public const string DeliveryRequiredMessage = "Este negocio solo entrega a domicilio: escribe a dónde te lo llevamos.";
    public const string AlreadySentMessage = "Ese pedido ya se envió.";
    public const string AlreadyCancelledMessage = "Ese pedido ya se envió y el negocio lo canceló.";
    public const string TooManyWaitingMessage = "Ya tienes pedidos esperando confirmación. Espera a que el negocio los confirme.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICustomerLookupService _customerLookupService;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderInventoryService _orderInventoryService;
    private readonly IFolioGenerator _folios;
    private readonly ICurrentUserService _currentUser;

    public PlacePublicOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICustomerLookupService customerLookupService,
        IPhoneNormalizationService phoneNormalizationService,
        IDateTimeProvider dateTimeProvider,
        IOrderInventoryService orderInventoryService,
        IFolioGenerator folios,
        ICurrentUserService currentUser)
    {
        _context = context;
        _tenantService = tenantService;
        _customerLookupService = customerLookupService;
        _phoneNormalizationService = phoneNormalizationService;
        _dateTimeProvider = dateTimeProvider;
        _orderInventoryService = orderInventoryService;
        _folios = folios;
        _currentUser = currentUser;
    }

    public async Task<Result<PublicOrderReceiptDto>> Handle(PlacePublicOrderCommand request, CancellationToken cancellationToken)
    {
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode != OnlineMenuMode.Orders)
            return Result.Failure<PublicOrderReceiptDto>("Este negocio no recibe pedidos en línea.");

        if (!_phoneNormalizationService.TryNormalizeToE164(request.CustomerPhone, CustomerPhoneOwnershipRules.DefaultCountry, out var phone))
            return Result.Failure<PublicOrderReceiptDto>(PhoneNumberRules.InvalidPhoneMessage);

        // A public request has no tenant: everything below is scoped to the business of the slug.
        _tenantService.SetTenant(tenant.Id);

        // The same key again: a retry of an order already placed, whose answer was lost. Before any rule of now: the business
        // may have closed since.
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (request.IdempotencyKey is { } key && await PlacedAsync(key, phone, tenant, account, cancellationToken) is { } placed)
            return placed;

        var now = _dateTimeProvider.UtcNow;
        var asked = Request(request, phone);
        var placement = new PublicOrderPlacement(_context);
        var (branchId, closed) = await placement.TakesOrderAsync(tenant, asked, now, cancellationToken);
        if (closed is not null)
            return Result.Failure<PublicOrderReceiptDto>(closed);

        // Until the business confirms or cancels them, a phone cannot keep piling up orders.
        var waiting = await _context.Orders.CountAsync(o => o.Channel == OrderChannel.Online
            && o.CustomerPhone == phone
            && o.Status == OrderStatus.Pending, cancellationToken);
        if (waiting >= MaxPendingPerPhone)
            return Result.Failure<PublicOrderReceiptDto>(TooManyWaitingMessage);

        var settings = await TenantSalesSettings.ReadAsync(_context, tenant.Id, cancellationToken);
        // With a customer session the session says whose order it is: the account sees it in its orders without confirming
        // the phone by SMS, and nobody who confirms the phone later claims it. Without one, the phone decides.
        var customer = account is not null
            ? new CustomerLookupResult(phone, account.Id, null, request.CustomerName.Trim())
            : await _customerLookupService.ResolveForOrderAsync(
                tenant.Id, phone, request.CustomerName.Trim(), CustomerPhoneOwnershipRules.DefaultCountry, cancellationToken);

        var (order, refused) = await placement.BuildAsync(tenant, asked, branchId, customer, settings, now, cancellationToken);
        if (order is null)
            return Result.Failure<PublicOrderReceiptDto>(refused!);

        if (request.ExpectedTotal.HasValue && request.ExpectedTotal.Value != order.Total)
            return Result.Failure<PublicOrderReceiptDto>(TotalChangedMessage(order.Total));

        var accepted = await placement.PlaceAsync(
            order, tenant, asked, account?.Id, now, _folios, _orderInventoryService, cancellationToken);
        if (request.IdempotencyKey is { } requestKey)
            order.IdentifyClientRequest(requestKey, capturedOfflineAt: null);
        order.MarkAsCreated();
        _context.Orders.Add(order);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.IdempotencyKey is { } racedKey)
        {
            // The same order arrived twice at once and the other request stored it first (IX_Orders_TenantId_ClientRequestId).
            // This copy is undone whole, its folio, supplies and address included, and the stored one is the answer.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await PlacedAsync(racedKey, phone, tenant, account, cancellationToken) is { } stored)
                return stored;
            throw;
        }

        return Result.Success(Receipt(order, tenant, accepted, account));
    }

    private static PublicOrderReceiptDto Receipt(
        Order order, Domain.Tenants.Entities.Tenant tenant, bool confirmed, Domain.Identity.Entities.ApplicationUser? account) => new()
    {
        OrderNumber = order.OrderNumber,
        Total = order.Total,
        DeliveryFee = order.Delivery?.Fee,
        Confirmed = confirmed,
        BusinessName = tenant.Name,
        ContactPhone = tenant.ContactPhone,
        LinkedToAccount = account is not null && order.CustomerUserId == account.Id,
        TrackingToken = order.TrackingToken
    };

    /// <summary>
    /// The answer for an order of the business already placed with <paramref name="key"/>, or null. Only the same
    /// customer's online order answers, by its phone: a key is not a way to read another order.
    /// </summary>
    private async Task<Result<PublicOrderReceiptDto>?> PlacedAsync(
        Guid key,
        string phone,
        Domain.Tenants.Entities.Tenant tenant,
        Domain.Identity.Entities.ApplicationUser? account,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Delivery)
            .FirstOrDefaultAsync(o => o.TenantId == tenant.Id && o.ClientRequestId == key, cancellationToken);
        if (order is null)
            return null;
        if (order.Channel != OrderChannel.Online || order.CustomerPhone != phone)
            return Result.Failure<PublicOrderReceiptDto>(AlreadySentMessage);
        // The business cancelled it meanwhile: «lo confirmará en unos minutos» would not be true.
        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<PublicOrderReceiptDto>(AlreadyCancelledMessage);
        return Result.Success(Receipt(order, tenant, order.Status != OrderStatus.Pending, account));
    }

    /// <summary>What the customer asked for, with the phone normalized.</summary>
    internal static PublicOrderRequest Request(PlacePublicOrderCommand request, string phone) => new()
    {
        CustomerName = request.CustomerName,
        CustomerPhone = phone,
        Notes = request.Notes,
        TableLabel = request.TableLabel,
        Items = request.Items,
        Delivery = request.Delivery,
        SaveAddress = request.SaveAddress,
        AddressLabel = request.AddressLabel
    };

    internal static string TotalChangedMessage(decimal total) =>
        $"El total de tu pedido es {OrderStatusLabels.FormatMoney(total)}: algún precio cambió. Revísalo antes de enviarlo.";
}

/// <summary>
/// The tables a customer orders from with the QR of the table (<c>?mesa=</c>). Anyone can type a table into the link,
/// so only a table of the business that is in use counts as a customer already seated.
/// </summary>
internal static class PublicMenuTables
{
    public static async Task<bool> IsSeatedAsync(
        IApplicationDbContext context,
        Guid tenantId,
        string? table,
        CancellationToken cancellationToken)
    {
        var name = table?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > DiningTable.MaxNameLength)
            return false;
        return await context.DiningTables.AnyAsync(t => t.TenantId == tenantId && t.IsActive && t.Name == name, cancellationToken);
    }
}

internal static class PublicMenuTenant
{
    /// <summary>An active business by its slug, with its settings.</summary>
    public static async Task<Domain.Tenants.Entities.Tenant?> FindAsync(
        IApplicationDbContext context,
        string slug,
        CancellationToken cancellationToken)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        // The public menu finds its business by slug before any business is in context; only that business's
        // settings come with it. The caller then enters the business.
        var tenant = await context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Slug == normalized && t.Status == TenantStatus.Active, cancellationToken);
        return tenant?.Settings is null ? null : tenant;
    }

    /// <summary>
    /// Of <paramref name="businessIds"/>, the businesses whose menu is published (to be seen or with orders): read before
    /// any business is in context, only that setting of each (the favorites of a customer account, Fase 24F).
    /// </summary>
    public static async Task<HashSet<Guid>> PublishedAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<Guid> businessIds,
        CancellationToken cancellationToken)
    {
        if (businessIds.Count == 0)
            return [];
        var published = await context.TenantSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => businessIds.Contains(s.TenantId) && s.OnlineMenuMode != OnlineMenuMode.Off)
            .Select(s => s.TenantId)
            .ToListAsync(cancellationToken);
        return published.ToHashSet();
    }
}

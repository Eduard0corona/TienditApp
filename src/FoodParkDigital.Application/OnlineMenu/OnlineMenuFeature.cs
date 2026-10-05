using System.Text.Json;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
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
    /// <summary>The business phone for "Escríbenos por WhatsApp", when it has one.</summary>
    public string? ContactPhone { get; set; }
    public string Currency { get; set; } = "MXN";
    public bool PricesIncludeTax { get; set; }
    public decimal DefaultTaxRate { get; set; }
    /// <summary>The menu takes orders; otherwise it only shows the catalog.</summary>
    public bool AcceptsOrders { get; set; }
    public List<PublicMenuCategoryDto> Categories { get; set; } = new();
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
[AllowAnonymousRequest("Menú público del negocio: solo el catálogo que el negocio decidió publicar.")]
public record GetPublicMenuQuery(string Slug) : IRequest<PublicMenuDto?>;

public class GetPublicMenuQueryHandler : IRequestHandler<GetPublicMenuQuery, PublicMenuDto?>
{
    public const int MaxProducts = 500;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetPublicMenuQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<PublicMenuDto?> Handle(GetPublicMenuQuery request, CancellationToken cancellationToken)
    {
        var tenant = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (tenant is null || tenant.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return null;

        // A public request has no tenant: the catalog queries below are scoped to the business of the slug.
        _tenantService.SetTenant(tenant.Id);
        var settings = tenant.Settings;
        var localTime = TenantDateTimeResolver.GetLocalTimeOfDay(_dateTimeProvider.UtcNow, TenantDateTimeResolver.Resolve(settings.TimeZone));

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);
        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.OptionGroups).ThenInclude(g => g.Options)
            .Where(p => p.IsAvailable)
            .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Take(MaxProducts)
            .ToListAsync(cancellationToken);

        var byCategory = products
            .Where(p => !OrderProductClassifier.IsServiceUnit(p.Unit))
            .GroupBy(p => p.CategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new PublicMenuDto
        {
            BusinessName = tenant.Name,
            Slug = tenant.Slug,
            LogoUrl = string.IsNullOrEmpty(tenant.LogoUrl) ? null : $"{tenant.LogoUrl}_md.webp",
            ContactPhone = tenant.ContactPhone,
            Currency = settings.Currency,
            PricesIncludeTax = settings.PricesIncludeTax,
            DefaultTaxRate = settings.DefaultTaxRate,
            AcceptsOrders = settings.OnlineMenuMode == OnlineMenuMode.Orders,
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
    /// <summary>True when the business accepts online orders without confirming each one.</summary>
    public bool Confirmed { get; set; }
    public string BusinessName { get; set; } = null!;
    public string? ContactPhone { get; set; }
}

/// <summary>An order placed from the public menu. It arrives in Pedidos with the Online channel.</summary>
[AllowAnonymousRequest("Pedido del menú público: el negocio activó los pedidos en línea y el cliente se identifica con su teléfono.")]
public record PlacePublicOrderCommand : IRequest<Result<PublicOrderReceiptDto>>
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
    }
}

public class PlacePublicOrderCommandHandler : IRequestHandler<PlacePublicOrderCommand, Result<PublicOrderReceiptDto>>
{
    /// <summary>Orders a phone may leave waiting for the business to confirm them (spam guard).</summary>
    public const int MaxPendingPerPhone = 3;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICustomerLookupService _customerLookupService;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderInventoryService _orderInventoryService;
    private readonly IFolioGenerator _folios;

    public PlacePublicOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICustomerLookupService customerLookupService,
        IPhoneNormalizationService phoneNormalizationService,
        IDateTimeProvider dateTimeProvider,
        IOrderInventoryService orderInventoryService,
        IFolioGenerator folios)
    {
        _context = context;
        _tenantService = tenantService;
        _customerLookupService = customerLookupService;
        _phoneNormalizationService = phoneNormalizationService;
        _dateTimeProvider = dateTimeProvider;
        _orderInventoryService = orderInventoryService;
        _folios = folios;
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
        var now = _dateTimeProvider.UtcNow;
        // Until the business confirms or cancels them, a phone cannot keep piling up orders.
        var waiting = await _context.Orders.CountAsync(o => o.Channel == OrderChannel.Online
            && o.CustomerPhone == phone
            && o.Status == OrderStatus.Pending, cancellationToken);
        if (waiting >= MaxPendingPerPhone)
            return Result.Failure<PublicOrderReceiptDto>("Ya tienes pedidos esperando confirmación. Espera a que el negocio los confirme.");

        var settings = await TenantSalesSettings.ReadAsync(_context, tenant.Id, cancellationToken);
        var localTimeOfDay = TenantDateTimeResolver.GetLocalTimeOfDay(now, TenantDateTimeResolver.Resolve(settings.TimeZone));
        var customer = await _customerLookupService.ResolveForOrderAsync(
            tenant.Id, phone, request.CustomerName.Trim(), CustomerPhoneOwnershipRules.DefaultCountry, cancellationToken);

        // Online orders arrive at the default branch of the business.
        var branchId = await BranchRules.DefaultBranchIdAsync(_context, tenant.Id, cancellationToken);
        var order = new Order(
            tenantId: tenant.Id,
            customerUserId: customer.CustomerUserId,
            temporaryCustomerId: customer.TemporaryCustomerId,
            createdByBusiness: false,
            orderNumber: Order.NumberNotAssigned,
            businessType: tenant.BusinessType,
            orderType: OrderType.Retail,
            customerName: request.CustomerName.Trim(),
            customerPhone: customer.PhoneNormalized,
            customerNotes: string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            tableNumber: string.IsNullOrWhiteSpace(request.TableLabel) ? null : request.TableLabel.Trim(),
            pricesIncludeTax: settings.PricesIncludeTax,
            channel: OrderChannel.Online,
            branchId: branchId);

        var lineBuilder = new OrderLineBuilder(localTimeOfDay, settings.DefaultTaxRate);
        var products = await OrderCatalog.LoadProductsAsync(_context.Products, request.Items, cancellationToken);
        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            if (!product.IsAvailable || !OrderProductClassifier.MatchesOrderType(OrderType.Retail, product.Unit))
                return Result.Failure<PublicOrderReceiptDto>($"'{product.Name}' no se puede pedir en línea.");

            var built = lineBuilder.Build(product, item);
            if (built.IsFailure)
                return Result.Failure<PublicOrderReceiptDto>(built.Error!);
            order.AddItem(built.Value.Line);
        }

        if (request.ExpectedTotal.HasValue && request.ExpectedTotal.Value != order.Total)
        {
            return Result.Failure<PublicOrderReceiptDto>(
                $"El total de tu pedido es {OrderStatusLabels.FormatMoney(order.Total)}: algún precio cambió. Revísalo antes de enviarlo.");
        }

        // Businesses that accept orders automatically send them straight to preparation.
        var accepted = tenant.Settings.AutoAcceptOrders;
        if (accepted)
        {
            var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
            if (stateMachine.GetAllowedTransitions(order.Status).Contains(OrderStatus.Confirmed))
                order.AdvanceOnCreation(OrderStatus.Confirmed, stateMachine, "menu-en-linea");
            else
                accepted = false;
        }

        order.AssignNumber(await _folios.NextOrderNumberAsync(tenant.Id, cancellationToken));

        // The customer's order is never refused for recipe stock: the business sees it and decides.
        await _orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken, StockShortageHandling.Allow);
        order.MarkAsCreated();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new PublicOrderReceiptDto
        {
            OrderNumber = order.OrderNumber,
            Total = order.Total,
            Confirmed = accepted,
            BusinessName = tenant.Name,
            ContactPhone = tenant.ContactPhone
        });
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
        var tenant = await context.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Slug == normalized && t.Status == TenantStatus.Active, cancellationToken);
        return tenant?.Settings is null ? null : tenant;
    }
}

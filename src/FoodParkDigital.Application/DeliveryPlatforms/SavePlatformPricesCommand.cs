using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>
/// Saves prices in the app of a delivery platform (Catálogo › Precios en apps), as whoever changes the menu's prices. Each
/// product listed takes its price there (null: the menu's), and each of its options listed what it adds there (null: what
/// it adds in the menu); the options left out keep theirs. Orders already registered keep the prices they copied.
/// </summary>
[RequiresPermission(TenantPermission.ManageCatalog)]
public record SavePlatformPricesCommand(OrderChannel Channel, IReadOnlyList<PlatformProductPriceInput> Products) : IRequest<Result>;

/// <param name="Price">What the product costs in the app; null sells it there at the menu's price.</param>
/// <param name="Options">Options whose price in the app changes; null or empty leaves them all as they are.</param>
public record PlatformProductPriceInput(Guid ProductId, decimal? Price, IReadOnlyList<PlatformOptionPriceInput>? Options);

/// <param name="PriceDelta">What the option adds in the app; null adds there what it adds in the menu.</param>
public record PlatformOptionPriceInput(Guid OptionId, decimal? PriceDelta);

public static class PlatformPriceErrors
{
    /// <summary>A product of the list is no longer in the catalog (deleted meanwhile, or never the business's).</summary>
    public const string ProductGone = "Uno de los productos ya no está en el catálogo. Recarga e intenta de nuevo.";

    public static string IsAService(string name) => $"'{name}' es un servicio, y un pedido de plataforma lleva solo productos.";
}

public class SavePlatformPricesCommandValidator : AbstractValidator<SavePlatformPricesCommand>
{
    /// <summary>The products of one save: as many as one page of the list.</summary>
    public const int MaxProducts = Paging.MaxPageSize;

    /// <summary>The options of one product: every option it can have.</summary>
    public const int MaxOptionsPerProduct = Product.MaxOptionGroups * ProductOptionGroup.MaxOptions;

    public SavePlatformPricesCommandValidator()
    {
        RuleFor(x => x.Channel)
            .Must(OrderChannelRules.IsDeliveryPlatform)
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        RuleFor(x => x.Products).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Indica los precios que cambian.")
            .Must(products => products.Count is > 0 and <= MaxProducts)
            .WithMessage($"Guarda entre 1 y {MaxProducts} productos a la vez.")
            .Must(products => products.Select(product => product.ProductId).Distinct().Count() == products.Count)
            .WithMessage("Un producto está repetido en la lista.");
        RuleForEach(x => x.Products).ChildRules(product =>
        {
            product.RuleFor(x => x.ProductId).NotEmpty().WithMessage(PlatformPriceErrors.ProductGone);
            product.RuleFor(x => x.Options)
                .Must(options => options is null || options.Count <= MaxOptionsPerProduct)
                .WithMessage($"Un producto tiene como máximo {MaxOptionsPerProduct} opciones.")
                .Must(options => options is null || options.Select(option => option.OptionId).Distinct().Count() == options.Count)
                .WithMessage("Una opción está repetida en la lista.");
        });
    }
}

public class SavePlatformPricesCommandHandler : IRequestHandler<SavePlatformPricesCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SavePlatformPricesCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SavePlatformPricesCommand request, CancellationToken cancellationToken)
    {
        var channel = request.Channel;
        var ids = request.Products.Select(product => product.ProductId).ToList();
        // Only the prices of this platform: the other platforms' stay as they are.
        var products = await _context.Products
            .Include(product => product.PlatformPrices.Where(price => price.Channel == channel))
            .Include(product => product.OptionGroups)
            .ThenInclude(group => group.Options)
            .ThenInclude(option => option.PlatformPrices.Where(price => price.Channel == channel))
            .Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        // Every product first, before changing any: a save is all of its prices or none.
        foreach (var input in request.Products)
        {
            if (!products.TryGetValue(input.ProductId, out var product))
                return Result.Failure(PlatformPriceErrors.ProductGone);
            if (OrderProductClassifier.IsServiceUnit(product.Unit))
                return Result.Failure(PlatformPriceErrors.IsAService(product.Name));
        }

        try
        {
            foreach (var input in request.Products)
            {
                var product = products[input.ProductId];
                product.SetPlatformPrice(channel, input.Price);
                foreach (var option in input.Options ?? [])
                    product.SetOptionPlatformPrice(option.OptionId, channel, option.PriceDelta);
            }
        }
        catch (DomainException ex)
        {
            // A price the catalog cannot have: the ones already changed in memory are dropped with the rest.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure(ex.Message);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone saved a first price of the same product or option at the same time (the unique index), or deleted it
            // meanwhile: this save is undone whole, and the person reloads to see what is there now.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure(DeliveryPlatformErrors.Stale);
        }

        return Result.Success();
    }
}

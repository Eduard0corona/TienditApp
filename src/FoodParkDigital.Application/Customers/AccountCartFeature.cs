using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers;

/// <summary>
/// The cart a customer account left in a store (Fase 31J, ADR 0090): what was chosen, never a price. The store prices it with
/// its menu of now when it opens it, and leaves out what the menu no longer has.
/// </summary>
public class MyCartDto
{
    /// <summary>Its store: /m/{slug}.</summary>
    public string Slug { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    /// <summary>When the customer last changed it, on whatever device.</summary>
    public DateTimeOffset SavedAt { get; set; }
    public List<MyCartLineDto> Lines { get; set; } = [];
    /// <summary>Filled by «Pedir otra vez»: how many products the order had.</summary>
    public int? ReorderOf { get; set; }
    /// <summary>Filled by «Pedir otra vez»: the products sold by weight or in fractions, which the cart does not take.</summary>
    public int? Skipped { get; set; }
}

public class MyCartLineDto
{
    public Guid ProductId { get; set; }
    public List<Guid> OptionIds { get; set; } = [];
    public int Quantity { get; set; }
    /// <summary>What the customer asked for this product («sin cebolla»); none without a note.</summary>
    public string? Notes { get; set; }
}

/// <summary>The postal code the customer account gives in Explorar; none without one.</summary>
public class MyPostalCodeDto
{
    public string? PostalCode { get; set; }
}

/// <summary>
/// The carts the signed-in customer account left in the stores, the last one changed first: changed within a week, with
/// something in them, in stores that are active and published (one suspended or unpublished is left out until it is back).
/// None for an account of a business's team.
/// </summary>
[RequiresAuthentication]
public record GetMyCartsQuery : IRequest<List<MyCartDto>>;

/// <summary>
/// Keeps the cart of the signed-in customer account in a store, all of it: what it had is replaced, and an empty one is
/// forgotten (the answer is then none). The account keeps its <see cref="AccountCart.MaxPerAccount"/> most recent carts.
/// </summary>
[RequiresAuthentication]
public record SaveMyCartCommand : IRequest<Result<MyCartDto?>>
{
    public string Slug { get; init; } = null!;
    public List<MyCartLineDto> Lines { get; init; } = [];
    public int? ReorderOf { get; init; }
    public int? Skipped { get; init; }
}

/// <summary>Forgets the cart of the signed-in customer account in a store; one that was not there changes nothing.</summary>
[RequiresAuthentication]
public record DeleteMyCartCommand(string Slug) : IRequest<Result>;

/// <summary>The postal code the signed-in customer account gave in Explorar; none for an account of a business's team.</summary>
[RequiresAuthentication]
public record GetMyPostalCodeQuery : IRequest<MyPostalCodeDto>;

/// <summary>Keeps the postal code the signed-in customer account gives in Explorar; empty forgets it.</summary>
[RequiresAuthentication]
public record SaveMyPostalCodeCommand(string? PostalCode) : IRequest<Result<MyPostalCodeDto>>;

public class SaveMyCartCommandValidator : AbstractValidator<SaveMyCartCommand>
{
    public SaveMyCartCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(FavoriteBusinessRules.MaxSlugLength);
        RuleFor(x => x.Lines).NotNull();
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(AccountCart.MaxLines).WithMessage(AccountCart.TooManyLinesMessage);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty().WithMessage(AccountCart.BadLineMessage);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, AccountCart.MaxLineQuantity).WithMessage(AccountCart.BadLineMessage);
            line.RuleFor(l => l.OptionIds).NotNull().WithMessage(AccountCart.BadLineMessage);
            line.RuleFor(l => l.OptionIds.Count).LessThanOrEqualTo(AccountCart.MaxOptionsPerLine)
                .WithMessage(AccountCart.BadLineMessage).When(l => l.OptionIds is not null);
            line.RuleFor(l => l.Notes).MaximumLength(AccountCart.MaxLineNotesLength).WithMessage(AccountCart.BadLineMessage);
        });
        RuleFor(x => x.ReorderOf).InclusiveBetween(0, 1000);
        RuleFor(x => x.Skipped).InclusiveBetween(0, 1000);
    }
}

public class DeleteMyCartCommandValidator : AbstractValidator<DeleteMyCartCommand>
{
    public DeleteMyCartCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(FavoriteBusinessRules.MaxSlugLength);
    }
}

public class SaveMyPostalCodeCommandValidator : AbstractValidator<SaveMyPostalCodeCommand>
{
    public SaveMyPostalCodeCommandValidator()
    {
        RuleFor(x => x.PostalCode)
            .Must(code => DeliveryZone.NormalizePostalCode(code) is not { } postalCode || DeliveryZone.IsPostalCode(postalCode))
            // Without what was written: a validation message reaches the log (ADR 0017 keeps postal codes out of it).
            .WithMessage(GetPublicDeliveryQuoteQueryValidator.PostalCodeMessage);
    }
}

public static class AccountCartRules
{
    public const string CustomersOnlyMessage = "Solo una cuenta de cliente guarda su carrito y su código postal.";
    public const string NotFoundMessage = FavoriteBusinessRules.NotFoundMessage;
}

public class AccountCartHandlers :
    IRequestHandler<GetMyCartsQuery, List<MyCartDto>>,
    IRequestHandler<SaveMyCartCommand, Result<MyCartDto?>>,
    IRequestHandler<DeleteMyCartCommand, Result>,
    IRequestHandler<GetMyPostalCodeQuery, MyPostalCodeDto>,
    IRequestHandler<SaveMyPostalCodeCommand, Result<MyPostalCodeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public AccountCartHandlers(IApplicationDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<List<MyCartDto>> Handle(GetMyCartsQuery request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return [];

        var now = _clock.UtcNow;
        var since = now.AddDays(-AccountCart.FreshDays);
        var carts = (await _context.AccountCarts
                .AsNoTracking()
                .Where(c => c.UserId == account.Id && c.SavedAt > since)
                .OrderByDescending(c => c.SavedAt).ThenBy(c => c.Id)
                .Take(AccountCart.MaxPerAccount)
                .ToListAsync(cancellationToken))
            .Where(c => c.IsFresh(now))
            .ToList();
        if (carts.Count == 0)
            return [];

        // The stores themselves (not a business's data: no business filter), the active ones whose menu is published.
        var ids = carts.Select(c => c.BusinessId).ToList();
        var businesses = await _context.Tenants
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.Status == TenantStatus.Active)
            .Select(t => new { t.Id, t.Name, t.Slug })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var published = await PublicMenuTenant.PublishedAsync(_context, businesses.Keys, cancellationToken);

        return carts
            .Where(c => businesses.ContainsKey(c.BusinessId) && published.Contains(c.BusinessId))
            .Select(c => Describe(c, businesses[c.BusinessId].Slug, businesses[c.BusinessId].Name))
            .ToList();
    }

    public async Task<Result<MyCartDto?>> Handle(SaveMyCartCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure<MyCartDto?>(AccountCartRules.CustomersOnlyMessage);

        // The store a customer sees: an active business with its menu published.
        var business = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (business is null || business.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return Result.Failure<MyCartDto?>(AccountCartRules.NotFoundMessage);

        var lines = request.Lines
            .Select(line => new AccountCartLine(line.ProductId, line.OptionIds, line.Quantity, line.Notes))
            .ToList();
        // Twice at most: the same store saved at the same time from another device creates its cart first (the unique
        // index keeps one), and this save then replaces it.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var saved = await SaveAsync(account.Id, business.Id, lines, request, cancellationToken);
                return Result.Success(saved is null ? null : Describe(saved, business.Slug, business.Name));
            }
            catch (DomainException ex)
            {
                await _context.DiscardUnitOfWorkAsync(cancellationToken);
                return Result.Failure<MyCartDto?>(ex.Message);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                await _context.DiscardUnitOfWorkAsync(cancellationToken);
            }
        }
    }

    /// <returns>The cart kept, or null when it was emptied.</returns>
    private async Task<AccountCart?> SaveAsync(
        Guid accountId, Guid businessId, List<AccountCartLine> lines, SaveMyCartCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var carts = await _context.AccountCarts.Where(c => c.UserId == accountId).ToListAsync(cancellationToken);
        var cart = carts.FirstOrDefault(c => c.BusinessId == businessId);
        if (lines.Count == 0)
        {
            if (cart is not null)
            {
                _context.AccountCarts.Remove(cart);
                await _context.SaveChangesAsync(cancellationToken);
            }
            return null;
        }

        if (cart is null)
        {
            cart = AccountCart.Create(accountId, businessId, lines, request.ReorderOf, request.Skipped, now);
            _context.AccountCarts.Add(cart);
        }
        else
        {
            cart.Replace(lines, request.ReorderOf, request.Skipped, now);
        }

        // The account keeps its most recent carts, as a device: the ones over a week old and the oldest past the most go.
        var others = carts.Where(c => c != cart).OrderByDescending(c => c.SavedAt).ThenBy(c => c.Id).ToList();
        foreach (var (old, index) in others.Select((old, index) => (old, index)))
        {
            if (!old.IsFresh(now) || index >= AccountCart.MaxPerAccount - 1)
                _context.AccountCarts.Remove(old);
        }
        await _context.SaveChangesAsync(cancellationToken);
        return cart;
    }

    public async Task<Result> Handle(DeleteMyCartCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure(AccountCartRules.CustomersOnlyMessage);

        // By its slug whatever its state: the cart of a store suspended since can still be forgotten.
        var slug = request.Slug.Trim().ToLowerInvariant();
        var cart = await _context.AccountCarts
            .Where(c => c.UserId == account.Id && _context.Tenants.Any(t => t.Id == c.BusinessId && t.Slug == slug))
            .FirstOrDefaultAsync(cancellationToken);
        if (cart is null)
            return Result.Success();

        _context.AccountCarts.Remove(cart);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<MyPostalCodeDto> Handle(GetMyPostalCodeQuery request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        return new MyPostalCodeDto { PostalCode = account?.PostalCode };
    }

    public async Task<Result<MyPostalCodeDto>> Handle(SaveMyPostalCodeCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure<MyPostalCodeDto>(AccountCartRules.CustomersOnlyMessage);

        var postalCode = DeliveryZone.NormalizePostalCode(request.PostalCode);
        var user = await _context.Users.FirstAsync(u => u.Id == account.Id, cancellationToken);
        if (user.PostalCode != postalCode)
        {
            user.PostalCode = postalCode;
            await _context.SaveChangesAsync(cancellationToken);
        }
        return Result.Success(new MyPostalCodeDto { PostalCode = postalCode });
    }

    private static MyCartDto Describe(AccountCart cart, string slug, string businessName) => new()
    {
        Slug = slug,
        BusinessName = businessName,
        SavedAt = cart.SavedAt,
        Lines = cart.Lines.Select(line => new MyCartLineDto
        {
            ProductId = line.ProductId,
            OptionIds = line.OptionIds.ToList(),
            Quantity = line.Quantity,
            Notes = line.Notes
        }).ToList(),
        ReorderOf = cart.ReorderOf,
        Skipped = cart.Skipped
    };
}

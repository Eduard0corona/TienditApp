using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Customers;

/// <summary>A business the customer account keeps among its favorites, the way Explorar shows it.</summary>
public class MyFavoriteBusinessDto
{
    /// <summary>Its store: /m/{slug}.</summary>
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    /// <summary>Its logo, 256 px; none without one.</summary>
    public string? LogoUrl { get; set; }
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>
    /// Its card in Explorar now, as the directory answers it (with the delivery to the postal code asked); null when it does
    /// not show in Explorar (its owner keeps it out, or its menu has nothing): it stays among the favorites with its name.
    /// </summary>
    public PublicDirectoryBusinessDto? Card { get; set; }
}

/// <summary>
/// The favorite businesses of the signed-in customer account (Fase 24F), the last one kept first; none for an account of a
/// business's team. A business suspended, cancelled or without its menu published is left out until it is back.
/// </summary>
/// <param name="PostalCode">The customer's postal code: each card says whether the business delivers there, as Explorar.</param>
[RequiresAuthentication]
public record GetMyFavoritesQuery(string? PostalCode = null) : IRequest<List<MyFavoriteBusinessDto>>;

public class GetMyFavoritesQueryValidator : AbstractValidator<GetMyFavoritesQuery>
{
    public GetMyFavoritesQueryValidator()
    {
        RuleFor(x => x.PostalCode)
            .Must(code => DeliveryZone.NormalizePostalCode(code) is not { } postalCode || DeliveryZone.IsPostalCode(postalCode))
            // Without what was written: a validation message reaches the log (ADR 0017 keeps postal codes out of it).
            .WithMessage(GetPublicDeliveryQuoteQueryValidator.PostalCodeMessage);
    }
}

public class GetMyFavoritesQueryHandler : IRequestHandler<GetMyFavoritesQuery, List<MyFavoriteBusinessDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly PublicDirectoryCache _cache;
    private readonly ILogger<GetMyFavoritesQueryHandler> _logger;

    public GetMyFavoritesQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IDateTimeProvider dateTimeProvider,
        PublicDirectoryCache cache,
        ILogger<GetMyFavoritesQueryHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<MyFavoriteBusinessDto>> Handle(GetMyFavoritesQuery request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return [];

        var favorites = await _context.FavoriteBusinesses
            .AsNoTracking()
            .Where(f => f.UserId == account.Id)
            .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
            .Take(FavoriteBusiness.MaxPerAccount)
            .Select(f => new { f.BusinessId, f.CreatedAt })
            .ToListAsync(cancellationToken);
        if (favorites.Count == 0)
            return [];

        // The businesses themselves (not a business's data: no business filter), only those still active, and of those the
        // ones whose menu is still published.
        var ids = favorites.Select(f => f.BusinessId).ToList();
        var businesses = await _context.Tenants
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.Status == TenantStatus.Active)
            .Select(t => new { t.Id, t.Name, t.Slug, t.LogoUrl })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var published = await PublicMenuTenant.PublishedAsync(_context, businesses.Keys, cancellationToken);

        // Their cards are the ones of Explorar (its snapshot), with the delivery to the postal code.
        var snapshot = await _cache.GetAsync(
            _dateTimeProvider.UtcNow, (utcNow, ct) => PublicDirectorySnapshot.BuildAsync(_context, utcNow, ct, _logger), cancellationToken);
        var listed = snapshot.Entries.Where(entry => published.Contains(entry.TenantId) && ids.Contains(entry.TenantId)).ToList();
        var cards = (await PublicDirectoryCards.WithDeliveryToAsync(
                _context, listed, DeliveryZone.NormalizePostalCode(request.PostalCode), cancellationToken))
            .Zip(listed, (card, entry) => (entry.TenantId, card))
            .ToDictionary(pair => pair.TenantId, pair => pair.card);

        return favorites
            .Where(f => businesses.ContainsKey(f.BusinessId) && published.Contains(f.BusinessId))
            .Select(f =>
            {
                var business = businesses[f.BusinessId];
                return new MyFavoriteBusinessDto
                {
                    Slug = business.Slug,
                    Name = business.Name,
                    LogoUrl = string.IsNullOrEmpty(business.LogoUrl) ? null : $"{business.LogoUrl}_sm.webp",
                    SavedAt = f.CreatedAt,
                    Card = cards.GetValueOrDefault(f.BusinessId)
                };
            })
            .ToList();
    }
}

/// <summary>
/// Keeps a business among the favorites of the signed-in customer account, by the slug of its store; its menu must be
/// published. Keeping it again changes nothing.
/// </summary>
[RequiresAuthentication]
public record SaveMyFavoriteCommand(string Slug) : IRequest<Result>;

/// <summary>Takes a business out of the favorites of the signed-in customer account; one that was not there changes nothing.</summary>
[RequiresAuthentication]
public record RemoveMyFavoriteCommand(string Slug) : IRequest<Result>;

public class SaveMyFavoriteCommandValidator : AbstractValidator<SaveMyFavoriteCommand>
{
    public SaveMyFavoriteCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(FavoriteBusinessRules.MaxSlugLength);
    }
}

public class RemoveMyFavoriteCommandValidator : AbstractValidator<RemoveMyFavoriteCommand>
{
    public RemoveMyFavoriteCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(FavoriteBusinessRules.MaxSlugLength);
    }
}

public class FavoriteBusinessCommandHandlers :
    IRequestHandler<SaveMyFavoriteCommand, Result>,
    IRequestHandler<RemoveMyFavoriteCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public FavoriteBusinessCommandHandlers(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(SaveMyFavoriteCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure(FavoriteBusinessRules.CustomersOnlyMessage);

        // The store a customer sees: an active business with its menu published.
        var business = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (business is null || business.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return Result.Failure(FavoriteBusinessRules.NotFoundMessage);

        var kept = await _context.FavoriteBusinesses
            .Where(f => f.UserId == account.Id)
            .Select(f => f.BusinessId)
            .ToListAsync(cancellationToken);
        if (kept.Contains(business.Id))
            return Result.Success();

        try
        {
            _context.FavoriteBusinesses.Add(FavoriteBusiness.Create(account.Id, business.Id, kept.Count));
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }
        catch (DbUpdateException)
        {
            // The same business kept at the same time from another tab: the unique index kept one.
            if (await IsKeptAsync(account.Id, business.Id, cancellationToken))
                return Result.Success();
            throw;
        }
    }

    public async Task<Result> Handle(RemoveMyFavoriteCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure(FavoriteBusinessRules.CustomersOnlyMessage);

        // By its slug whatever its state: a business suspended since it was kept can still be taken out.
        var slug = request.Slug.Trim().ToLowerInvariant();
        var favorite = await _context.FavoriteBusinesses
            .Where(f => f.UserId == account.Id && _context.Tenants.Any(t => t.Id == f.BusinessId && t.Slug == slug))
            .FirstOrDefaultAsync(cancellationToken);
        if (favorite is null)
            return Result.Success();

        _context.FavoriteBusinesses.Remove(favorite);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<bool> IsKeptAsync(Guid userId, Guid businessId, CancellationToken cancellationToken)
        => _context.FavoriteBusinesses.AsNoTracking().AnyAsync(f => f.UserId == userId && f.BusinessId == businessId, cancellationToken);
}

public static class FavoriteBusinessRules
{
    /// <summary>No slug of a business is longer.</summary>
    public const int MaxSlugLength = 100;
    public const string CustomersOnlyMessage = "Solo una cuenta de cliente guarda negocios favoritos.";
    public const string NotFoundMessage = "Este negocio no tiene menú en línea.";
}

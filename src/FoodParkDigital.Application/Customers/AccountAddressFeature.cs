using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers;

/// <summary>An address of the customer account, as the store offers it and Cuenta lists it.</summary>
public class AccountAddressDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = null!;
    public string PostalCode { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string? References { get; set; }
    /// <summary>The last time an order of the account went here; null until then.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public static AccountAddressDto From(AccountAddress address) => new()
    {
        Id = address.Id,
        Label = address.Label,
        PostalCode = address.PostalCode,
        AddressLine = address.AddressLine,
        References = address.References,
        LastUsedAt = address.LastUsedAt
    };
}

/// <summary>
/// The addresses of the signed-in customer account (Fase 24B), the last one an order used first; none for an account
/// of a business's team, which orders as a guest.
/// </summary>
[RequiresAuthentication]
public record GetMyAddressesQuery : IRequest<List<AccountAddressDto>>;

public class GetMyAddressesQueryHandler : IRequestHandler<GetMyAddressesQuery, List<AccountAddressDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetMyAddressesQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<AccountAddressDto>> Handle(GetMyAddressesQuery request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return [];

        var addresses = await AccountAddresses.OfAsync(_context, account.Id, cancellationToken);
        return addresses.Select(AccountAddressDto.From).ToList();
    }
}

/// <summary>
/// Saves an address in the signed-in customer account, at most <see cref="AccountAddress.MaxPerAccount"/>. The same
/// place again (the same postal code and line, whatever its capitals and spaces) is not a second address: the saved one
/// takes the new name, when one comes, and the new references.
/// </summary>
[RequiresAuthentication]
public record SaveMyAddressCommand : IRequest<Result<AccountAddressDto>>
{
    public string? Label { get; init; }
    public string PostalCode { get; init; } = string.Empty;
    public string AddressLine { get; init; } = string.Empty;
    public string? References { get; init; }
}

/// <summary>Changes an address of the signed-in customer account; another account's is not found.</summary>
[RequiresAuthentication]
public record UpdateMyAddressCommand : IRequest<Result<AccountAddressDto>>
{
    public Guid Id { get; init; }
    public string? Label { get; init; }
    public string PostalCode { get; init; } = string.Empty;
    public string AddressLine { get; init; } = string.Empty;
    public string? References { get; init; }
}

/// <summary>Deletes an address of the signed-in customer account; the orders sent there keep their own copy.</summary>
[RequiresAuthentication]
public record DeleteMyAddressCommand(Guid Id) : IRequest<Result>;

public class SaveMyAddressCommandValidator : AbstractValidator<SaveMyAddressCommand>
{
    public SaveMyAddressCommandValidator()
    {
        RuleFor(x => x.Label).MaximumLength(AccountAddressRules.MaxLabelInputLength)
            .WithMessage(AccountAddressRules.LabelTooLongMessage);
        AccountAddressRules.Address(this, x => x.PostalCode, x => x.AddressLine, x => x.References);
    }
}

public class UpdateMyAddressCommandValidator : AbstractValidator<UpdateMyAddressCommand>
{
    public UpdateMyAddressCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Label).MaximumLength(AccountAddressRules.MaxLabelInputLength)
            .WithMessage(AccountAddressRules.LabelTooLongMessage);
        AccountAddressRules.Address(this, x => x.PostalCode, x => x.AddressLine, x => x.References);
    }
}

public class AccountAddressCommandHandlers :
    IRequestHandler<SaveMyAddressCommand, Result<AccountAddressDto>>,
    IRequestHandler<UpdateMyAddressCommand, Result<AccountAddressDto>>,
    IRequestHandler<DeleteMyAddressCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public AccountAddressCommandHandlers(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<AccountAddressDto>> Handle(SaveMyAddressCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure<AccountAddressDto>(AccountAddressRules.CustomersOnlyMessage);

        try
        {
            var address = DeliveryAddress.From(request.PostalCode, request.AddressLine, request.References);
            var kept = await AccountAddresses.OfAsync(_context, account.Id, cancellationToken, tracking: true);
            var saved = kept.FirstOrDefault(existing => existing.IsSamePlaceAs(address));
            if (saved is null)
            {
                saved = AccountAddress.Create(account.Id, request.Label, address.PostalCode, address.AddressLine, address.References, kept.Count);
                _context.AccountAddresses.Add(saved);
            }
            else
            {
                // The place keeps how it was first written; it takes the new name, when there is one, and references.
                var label = string.IsNullOrWhiteSpace(request.Label) ? saved.Label : request.Label;
                saved.Change(label, saved.PostalCode, saved.AddressLine, address.References);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(AccountAddressDto.From(saved));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AccountAddressDto>(ex.Message);
        }
    }

    public async Task<Result<AccountAddressDto>> Handle(UpdateMyAddressCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure<AccountAddressDto>(AccountAddressRules.CustomersOnlyMessage);

        var kept = await AccountAddresses.OfAsync(_context, account.Id, cancellationToken, tracking: true);
        var address = kept.FirstOrDefault(existing => existing.Id == request.Id);
        if (address is null)
            return Result.Failure<AccountAddressDto>(AccountAddressRules.NotFoundMessage);

        try
        {
            var changed = DeliveryAddress.From(request.PostalCode, request.AddressLine, request.References);
            // One place is one address: moving it onto another one the account keeps would leave two.
            if (kept.FirstOrDefault(other => other.Id != address.Id && other.IsSamePlaceAs(changed)) is { } other)
                return Result.Failure<AccountAddressDto>($"Ya guardaste esa dirección como «{other.Label}».");

            address.Change(request.Label, changed.PostalCode, changed.AddressLine, changed.References);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(AccountAddressDto.From(address));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AccountAddressDto>(ex.Message);
        }
    }

    public async Task<Result> Handle(DeleteMyAddressCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure(AccountAddressRules.CustomersOnlyMessage);

        var address = await _context.AccountAddresses
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.UserId == account.Id, cancellationToken);
        if (address is null)
            return Result.Failure(AccountAddressRules.NotFoundMessage);

        _context.AccountAddresses.Remove(address);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>What the requests about the account's addresses check, and what they answer.</summary>
public static class AccountAddressRules
{
    /// <summary>A longer name is cut to <see cref="AccountAddress.MaxLabelLength"/>; one much longer is a mistake.</summary>
    public const int MaxLabelInputLength = 200;
    public const string LabelTooLongMessage = "El nombre de la dirección admite hasta 60 caracteres.";
    public const string CustomersOnlyMessage = "Solo una cuenta de cliente guarda direcciones.";
    public const string NotFoundMessage = "Esa dirección ya no está en tu cuenta.";

    /// <summary>The address with the limits of an order's (OrderDeliveryInputValidator).</summary>
    public static void Address<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> postalCode,
        System.Linq.Expressions.Expression<Func<T, string>> addressLine,
        System.Linq.Expressions.Expression<Func<T, string?>> references)
    {
        validator.RuleFor(postalCode).Cascade(CascadeMode.Stop).NotEmpty().WithMessage(DeliveryAddress.PostalCodeRequiredMessage)
            // Without what was written: a validation message reaches the log.
            .MaximumLength(OrderDeliveryInputValidator.MaxPostalCodeLength).WithMessage("El código postal lleva 5 dígitos.");
        validator.RuleFor(addressLine).Cascade(CascadeMode.Stop).NotEmpty().WithMessage(DeliveryAddress.AddressLineRequiredMessage)
            .Must(line => line.Trim().Length <= DeliveryAddress.MaxAddressLineLength).WithMessage(DeliveryAddress.AddressLineTooLongMessage);
        validator.RuleFor(references)
            .Must(text => text is null || text.Trim().Length <= DeliveryAddress.MaxReferencesLength)
            .WithMessage(DeliveryAddress.ReferencesTooLongMessage);
    }
}

/// <summary>The addresses of one account, read by its own id: they have no business filter.</summary>
internal static class AccountAddresses
{
    /// <summary>The last one used first, then the newest.</summary>
    public static Task<List<AccountAddress>> OfAsync(
        IApplicationDbContext context, Guid userId, CancellationToken cancellationToken, bool tracking = false)
    {
        var query = context.AccountAddresses.Where(a => a.UserId == userId);
        if (!tracking)
            query = query.AsNoTracking();
        return query
            .OrderByDescending(a => a.LastUsedAt.HasValue)
            .ThenByDescending(a => a.LastUsedAt)
            .ThenByDescending(a => a.CreatedAt)
            .Take(AccountAddress.MaxPerAccount + 1)
            .ToListAsync(cancellationToken);
    }
}

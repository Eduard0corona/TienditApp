using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Commands;

/// <summary>Saves a delivery address for a customer; the first one becomes the default.</summary>
[RequiresPermission(TenantPermission.ManageCustomers, TenantPermission.ManageWhatsApp, TenantPermission.ManageOrders)]
public record AddCustomerAddressCommand : IRequest<Result<CustomerDto>>
{
    public Guid CustomerId { get; init; }
    public string? Label { get; init; }
    public string AddressLine { get; init; } = null!;
    public string? References { get; init; }
    public bool IsDefault { get; init; }
}

/// <summary>Corrects an address or makes it the default one.</summary>
[RequiresPermission(TenantPermission.ManageCustomers, TenantPermission.ManageWhatsApp, TenantPermission.ManageOrders)]
public record UpdateCustomerAddressCommand : IRequest<Result<CustomerDto>>
{
    public Guid CustomerId { get; init; }
    public Guid AddressId { get; init; }
    public string? Label { get; init; }
    public string AddressLine { get; init; } = null!;
    public string? References { get; init; }
    public bool IsDefault { get; init; }
}

[RequiresPermission(TenantPermission.ManageCustomers, TenantPermission.ManageWhatsApp, TenantPermission.ManageOrders)]
public record RemoveCustomerAddressCommand(Guid CustomerId, Guid AddressId) : IRequest<Result<CustomerDto>>;

public class AddCustomerAddressCommandValidator : AbstractValidator<AddCustomerAddressCommand>
{
    public AddCustomerAddressCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Label).MaximumLength(CustomerAddress.LabelMaxLength);
        RuleFor(x => x.AddressLine).NotEmpty().WithMessage("Escribe la dirección.").MaximumLength(CustomerAddress.AddressMaxLength);
        RuleFor(x => x.References).MaximumLength(CustomerAddress.ReferencesMaxLength);
    }
}

public class UpdateCustomerAddressCommandValidator : AbstractValidator<UpdateCustomerAddressCommand>
{
    public UpdateCustomerAddressCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.AddressId).NotEmpty();
        RuleFor(x => x.Label).MaximumLength(CustomerAddress.LabelMaxLength);
        RuleFor(x => x.AddressLine).NotEmpty().WithMessage("Escribe la dirección.").MaximumLength(CustomerAddress.AddressMaxLength);
        RuleFor(x => x.References).MaximumLength(CustomerAddress.ReferencesMaxLength);
    }
}

public class CustomerAddressCommandHandlers :
    IRequestHandler<AddCustomerAddressCommand, Result<CustomerDto>>,
    IRequestHandler<UpdateCustomerAddressCommand, Result<CustomerDto>>,
    IRequestHandler<RemoveCustomerAddressCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;

    public CustomerAddressCommandHandlers(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Result<CustomerDto>> Handle(AddCustomerAddressCommand request, CancellationToken cancellationToken)
        => ChangeAsync(request.CustomerId, customer => customer.AddAddress(request.Label ?? string.Empty, request.AddressLine, request.References, request.IsDefault), cancellationToken);

    public Task<Result<CustomerDto>> Handle(UpdateCustomerAddressCommand request, CancellationToken cancellationToken)
        => ChangeAsync(request.CustomerId, customer => customer.UpdateAddress(request.AddressId, request.Label ?? string.Empty, request.AddressLine, request.References, request.IsDefault), cancellationToken);

    public Task<Result<CustomerDto>> Handle(RemoveCustomerAddressCommand request, CancellationToken cancellationToken)
        => ChangeAsync(request.CustomerId, customer => customer.RemoveAddress(request.AddressId), cancellationToken);

    // Customers are filtered by business: another business's customer is not found.
    private async Task<Result<CustomerDto>> ChangeAsync(Guid customerId, Action<Customer> change, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null)
            return Result.Failure<CustomerDto>("El cliente no existe.");
        if (customer.IsPublicGeneral)
            return Result.Failure<CustomerDto>("El público en general no guarda direcciones.");

        change(customer);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CustomerDto.From(customer));
    }
}

/// <summary>What the team remembers about the customer, editable from the chat and the orders screen too.</summary>
[RequiresPermission(TenantPermission.ManageCustomers, TenantPermission.ManageWhatsApp, TenantPermission.ManageOrders)]
public record UpdateCustomerNotesCommand(Guid CustomerId, string? Notes) : IRequest<Result<CustomerDto>>;

public class UpdateCustomerNotesCommandValidator : AbstractValidator<UpdateCustomerNotesCommand>
{
    public UpdateCustomerNotesCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(Customer.NotesMaxLength);
    }
}

public class UpdateCustomerNotesCommandHandler : IRequestHandler<UpdateCustomerNotesCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateCustomerNotesCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CustomerDto>> Handle(UpdateCustomerNotesCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
            return Result.Failure<CustomerDto>("El cliente no existe.");
        if (customer.IsPublicGeneral)
            return Result.Failure<CustomerDto>("El público en general no guarda notas.");

        customer.UpdateNotes(request.Notes);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CustomerDto.From(customer));
    }
}

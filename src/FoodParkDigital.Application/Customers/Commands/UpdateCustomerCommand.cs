using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Commands;

[RequiresPermission(TenantPermission.ManageCustomers)]
public record UpdateCustomerCommand : IRequest<Result<CustomerDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    /// <summary>Null keeps the notes; an empty text clears them.</summary>
    public string? Notes { get; init; }
}

public class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("El teléfono es obligatorio.")
            .MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Notes).MaximumLength(Customer.NotesMaxLength);
    }
}

public class UpdateCustomerCommandHandler : IRequestHandler<UpdateCustomerCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;

    public UpdateCustomerCommandHandler(IApplicationDbContext context, IPhoneNormalizationService phones)
    {
        _context = context;
        _phones = phones;
    }

    public async Task<Result<CustomerDto>> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.Id);

        if (customer.IsPublicGeneral)
            return Result.Failure<CustomerDto>("No puedes editar el cliente público en general.");

        var name = request.Name.Trim();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (string.IsNullOrWhiteSpace(request.Phone))
            return Result.Failure<CustomerDto>("El teléfono es obligatorio.");
        if (!CustomerPhones.TryNormalize(_phones, request.Phone, out var phone))
            return Result.Failure<CustomerDto>(CustomerPhones.InvalidMessage);

        var forms = CustomerPhones.StoredForms(phone);
        var taken = await _context.Customers
            .AnyAsync(c => c.Id != customer.Id && c.Phone != null && forms.Contains(c.Phone) && !c.IsPublicGeneral, cancellationToken);
        if (taken)
            return Result.Failure<CustomerDto>("Otro cliente ya tiene ese teléfono.");

        customer.Update(name, phone, email);
        if (request.Notes is not null)
            customer.UpdateNotes(request.Notes);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(CustomerDto.From(customer));
    }
}

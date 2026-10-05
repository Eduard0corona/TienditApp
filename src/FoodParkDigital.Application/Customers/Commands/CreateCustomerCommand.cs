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

[RequiresPermission(TenantPermission.ManageCustomers)]
public record CreateCustomerCommand : IRequest<Result<CustomerDto>>
{
    public string Name { get; init; } = null!;
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Notes { get; init; }
}

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("El teléfono es obligatorio.")
            .MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Notes).MaximumLength(Customer.NotesMaxLength);
    }
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPhoneNormalizationService _phones;

    public CreateCustomerCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IPhoneNormalizationService phones)
    {
        _context = context;
        _tenantService = tenantService;
        _phones = phones;
    }

    public async Task<Result<CustomerDto>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (string.IsNullOrWhiteSpace(request.Phone))
            return Result.Failure<CustomerDto>("El teléfono es obligatorio.");
        if (!CustomerPhones.TryNormalize(_phones, request.Phone, out var phone))
            return Result.Failure<CustomerDto>(CustomerPhones.InvalidMessage);

        // The same number is the same person, however it was typed before.
        var forms = CustomerPhones.StoredForms(phone);
        var exists = await _context.Customers
            .AnyAsync(c => c.Phone != null && forms.Contains(c.Phone) && !c.IsPublicGeneral, cancellationToken);
        if (exists)
            return Result.Failure<CustomerDto>("Ya existe un cliente con ese teléfono.");

        var customer = new Customer(
            tenantId: _tenantService.TenantId,
            name: name,
            phone: phone,
            email: email,
            isPublicGeneral: false);
        customer.UpdateNotes(request.Notes);

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(CustomerDto.From(customer));
    }
}

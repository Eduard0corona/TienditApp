using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Domain.Customers.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Commands;

public record CreateCustomerCommand : IRequest<Result<CustomerDto>>
{
    public string Name { get; init; } = null!;
    public string? Phone { get; init; }
    public string? Email { get; init; }
}

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("El telefono es obligatorio.")
            .MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public class CreateCustomerCommandHandler : IRequestHandler<CreateCustomerCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public CreateCustomerCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<CustomerDto>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

        if (phone is null)
            return Result.Failure<CustomerDto>("El telefono es obligatorio.");

        var exists = await _context.Customers
            .AnyAsync(c => c.Name == name && c.Phone == phone, cancellationToken);
        if (exists)
            return Result.Failure<CustomerDto>("Ya existe un cliente con esos datos.");

        var customer = new Customer(
            tenantId: _tenantService.TenantId,
            name: name,
            phone: phone,
            email: email,
            isPublicGeneral: false);

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(_mapper.Map<CustomerDto>(customer));
    }
}

using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Common.ValueObjects;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

public record UpdateMyTenantCommand : IRequest<Result<TenantProfileDto>>
{
    // General
    public string Name { get; init; } = null!;
    public string ContactEmail { get; init; } = null!;
    public string? ContactPhone { get; init; }

    // MX Business fields
    public string? Rfc { get; init; }
    public string? LegalName { get; init; }
    public string? Industry { get; init; }
    public string? TaxRegime { get; init; }

    // Address
    public string? Street { get; init; }
    public string? ExteriorNumber { get; init; }
    public string? InteriorNumber { get; init; }
    public string? Neighborhood { get; init; }
    public string? ZipCode { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
}

public class UpdateMyTenantCommandValidator : AbstractValidator<UpdateMyTenantCommand>
{
    public UpdateMyTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.ContactPhone).MaximumLength(20);
        RuleFor(x => x.Rfc)
            .Matches(@"^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$")
            .When(x => !string.IsNullOrEmpty(x.Rfc))
            .WithMessage("El RFC no tiene un formato válido.");
        RuleFor(x => x.LegalName).MaximumLength(300);
        RuleFor(x => x.Industry).MaximumLength(200);
        RuleFor(x => x.TaxRegime).MaximumLength(200);
        RuleFor(x => x.Street).MaximumLength(300);
        RuleFor(x => x.ExteriorNumber).MaximumLength(20);
        RuleFor(x => x.InteriorNumber).MaximumLength(20);
        RuleFor(x => x.Neighborhood).MaximumLength(200);
        RuleFor(x => x.ZipCode).MaximumLength(10);
        RuleFor(x => x.City).MaximumLength(200);
        RuleFor(x => x.State).MaximumLength(100);
    }
}

public class UpdateMyTenantCommandHandler : IRequestHandler<UpdateMyTenantCommand, Result<TenantProfileDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public UpdateMyTenantCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<Result<TenantProfileDto>> Handle(UpdateMyTenantCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role != "TENANT_ADMIN")
            return Result.Failure<TenantProfileDto>("Solo el administrador del tenant puede modificar los datos de la empresa.");

        var tenantId = _currentUserService.TenantId;
        if (tenantId is null)
            return Result.Failure<TenantProfileDto>("No se encontró el tenant del usuario.");

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, cancellationToken);

        if (tenant is null)
            return Result.Failure<TenantProfileDto>("Tenant no encontrado.");

        tenant.Update(request.Name, request.ContactEmail, request.ContactPhone, tenant.LogoUrl);

        Address? address = null;
        if (!string.IsNullOrEmpty(request.Street) && !string.IsNullOrEmpty(request.ExteriorNumber))
        {
            address = new Address(
                request.Street,
                request.ExteriorNumber,
                request.InteriorNumber,
                request.Neighborhood ?? "",
                request.ZipCode ?? "",
                request.City ?? "",
                request.State ?? "");
        }

        tenant.SetBusinessInfo(request.Rfc, request.LegalName, request.Industry, request.TaxRegime, address);

        await _context.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<TenantProfileDto>(tenant);
        return Result.Success(dto);
    }
}

using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

/// <summary>
/// Opens another business with the signed-in account, which becomes its owner. It starts like a business from
/// the sign-up (default branch, IVA included, trial of the plan for new businesses) and the person switches to it
/// to finish the guided setup.
/// </summary>
[RequiresAuthentication]
public record CreateBusinessCommand : IRequest<Result<MyTenantDto>>
{
    public string BusinessName { get; init; } = null!;
    public BusinessType BusinessType { get; init; }
}

public class CreateBusinessCommandValidator : AbstractValidator<CreateBusinessCommand>
{
    public CreateBusinessCommandValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BusinessType).IsInEnum();
    }
}

public class CreateBusinessCommandHandler : IRequestHandler<CreateBusinessCommand, Result<MyTenantDto>>
{
    /// <summary>A person runs a handful of businesses; more than this looks like abuse of the free trials.</summary>
    public const int MaxOwnedBusinesses = 10;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public CreateBusinessCommandHandler(IApplicationDbContext context, ICurrentUserService currentUserService, IDateTimeProvider clock)
    {
        _context = context;
        _currentUserService = currentUserService;
        _clock = clock;
    }

    public async Task<Result<MyTenantDto>> Handle(CreateBusinessCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure<MyTenantDto>("Inicia sesión para abrir un negocio.");

        // The account is global (no tenant filter applies to it).
        var user = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<MyTenantDto>("Inicia sesión para abrir un negocio.");

        // A customer account is customer everywhere and the platform admin manages businesses from the console.
        if (user.Role == UserRole.Customer)
            return Result.Failure<MyTenantDto>("Una cuenta de cliente no puede abrir un negocio: regístrate como negocio con otro correo.");
        if (user.Role == UserRole.SuperAdmin)
            return Result.Failure<MyTenantDto>("El administrador de la plataforma no abre negocios propios.");

        // Memberships of the account in every business, not only the current one.
        var ownedBusinesses = await _context.TenantUsers
            .IgnoreQueryFilters()
            .CountAsync(x => x.UserId == userId && x.Role == TenantUserRole.Owner, cancellationToken);
        if (ownedBusinesses >= MaxOwnedBusinesses)
            return Result.Failure<MyTenantDto>($"Una cuenta puede ser dueña de hasta {MaxOwnedBusinesses} negocios.");

        var name = request.BusinessName.Trim();
        var slug = await BusinessProvisioning.UniqueSlugAsync(_context, name, cancellationToken);
        var tenant = new Tenant(name: name, slug: slug, businessType: request.BusinessType, contactEmail: user.Email ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            tenant.Update(tenant.Name, tenant.ContactEmail, user.PhoneNumber, tenant.LogoUrl);

        var created = await _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            // One save: rows of a business created in the same unit of work may be written while the request
            // runs under the business the person is in.
            await BusinessProvisioning.AddAsync(_context, tenant, _clock.UtcNow, ct);
            _context.TenantUsers.Add(new TenantUser(tenant.Id, userId, TenantUserRole.Owner, TenantUserStatus.Active));
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenant.Id,
                entityName: nameof(Tenant),
                entityId: tenant.Id.ToString(),
                action: "BusinessOpenedByExistingAccount",
                performedBy: _currentUserService.UserName ?? user.Email ?? "system",
                metadata: JsonSerializer.Serialize(new { OwnerUserId = userId, tenant.BusinessType })));
            await _context.SaveChangesAsync(ct);
            return Result.Success();
        }, cancellationToken);
        if (created.IsFailure)
            return Result.Failure<MyTenantDto>(created.Error!);

        return Result.Success(new MyTenantDto
        {
            TenantId = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            Role = TenantUserRole.Owner.ToString(),
            Status = TenantUserStatus.Active.ToString()
        });
    }
}

using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.PublicDirectory;

/// <summary>Whether Explorar lists the business now, and when not, the one thing its owner can do about it.</summary>
public enum DirectoryVisibility
{
    /// <summary>Explorar lists it.</summary>
    Shown = 0,
    /// <summary>Its owner chose not to appear.</summary>
    HiddenByOwner = 1,
    /// <summary>Its menu is not published.</summary>
    MenuOff = 2,
    /// <summary>Its menu shows no product: none available in an active category, or only services.</summary>
    NoProducts = 3,
    /// <summary>The business is not active.</summary>
    Inactive = 4
}

/// <summary>«Cómo te ven en TienditApp»: how the business shows in Explorar.</summary>
public class DirectoryProfileDto
{
    public BusinessCategory Category { get; set; }
    /// <summary>The category its giro (or its type) gives, to suggest it.</summary>
    public BusinessCategory SuggestedCategory { get; set; }
    public string? Description { get; set; }
    /// <summary>The owner's choice: appear in Explorar while the menu is published.</summary>
    public bool Listed { get; set; }
    public DirectoryVisibility Visibility { get; set; }
}

/// <summary>How the signed-in user's business shows in Explorar (owners and admins).</summary>
[RequiresTenantManager]
public record GetDirectoryProfileQuery : IRequest<Result<DirectoryProfileDto>>;

public class GetDirectoryProfileQueryHandler : IRequestHandler<GetDirectoryProfileQuery, Result<DirectoryProfileDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDirectoryProfileQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<DirectoryProfileDto>> Handle(GetDirectoryProfileQuery request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken);
        return tenant is null
            ? Result.Failure<DirectoryProfileDto>("Negocio no encontrado.")
            : Result.Success(await DirectoryProfile.ReadAsync(_context, tenant, tenant.Settings, cancellationToken));
    }
}

/// <summary>
/// The category, the description and whether the business appears in Explorar (owners and admins). Explorar shows the
/// change within <see cref="PublicDirectoryCache.Lifetime"/>.
/// </summary>
/// <param name="Description">One line; empty removes it.</param>
/// <param name="Listed">Appear in Explorar while the menu is published.</param>
[RequiresTenantManager]
public record UpdateDirectoryProfileCommand(BusinessCategory Category, string? Description, bool Listed)
    : IRequest<Result<DirectoryProfileDto>>;

public class UpdateDirectoryProfileCommandValidator : AbstractValidator<UpdateDirectoryProfileCommand>
{
    public UpdateDirectoryProfileCommandValidator()
    {
        RuleFor(x => x.Category).IsInEnum().WithMessage("La categoría no existe.");
        RuleFor(x => x.Description)
            .Must(description => description is null || description.Trim().Length <= Tenant.MaxPublicDescriptionLength)
            .WithMessage($"La descripción admite como máximo {Tenant.MaxPublicDescriptionLength} caracteres.")
            .Must(description => description is null || Tenant.IsOneLine(description.Trim()))
            .WithMessage("La descripción va en un solo renglón.");
    }
}

public class UpdateDirectoryProfileCommandHandler : IRequestHandler<UpdateDirectoryProfileCommand, Result<DirectoryProfileDto>>
{
    /// <summary>The audit action of a change; the rollback of AddBusinessDirectory stops once there is one.</summary>
    public const string AuditAction = "DirectoryProfileUpdated";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public UpdateDirectoryProfileCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DirectoryProfileDto>> Handle(UpdateDirectoryProfileCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<DirectoryProfileDto>("Negocio no encontrado.");

        var settings = tenant.Settings;
        if (settings is null)
        {
            settings = new TenantSettings(tenant.Id, TenantCapabilityDefaults.FromBusinessType(tenant.BusinessType));
            _context.TenantSettings.Add(settings);
        }

        var before = (tenant.DirectoryCategory, tenant.PublicDescription, settings.ListedInDirectory);
        tenant.SetDirectoryProfile(request.Category, request.Description);
        settings.SetDirectoryListing(request.Listed);
        var after = (tenant.DirectoryCategory, tenant.PublicDescription, settings.ListedInDirectory);
        if (before != after)
        {
            // What strangers read about the business, and whether they find it, is a decision worth recording. The
            // description is public text, but the record keeps only that it changed.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenant.Id,
                entityName: nameof(Tenant),
                entityId: tenant.Id.ToString(),
                action: AuditAction,
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    PreviousCategory = before.DirectoryCategory.ToString(),
                    Category = after.DirectoryCategory.ToString(),
                    PreviousListed = before.ListedInDirectory,
                    Listed = after.ListedInDirectory,
                    DescriptionChanged = before.PublicDescription != after.PublicDescription
                })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await DirectoryProfile.ReadAsync(_context, tenant, settings, cancellationToken));
    }
}

internal static class DirectoryProfile
{
    /// <summary>The profile and why Explorar lists the business or not, in the order its owner would fix it.</summary>
    public static async Task<DirectoryProfileDto> ReadAsync(
        IApplicationDbContext context, Tenant tenant, TenantSettings? settings, CancellationToken cancellationToken)
    {
        var listed = settings?.ListedInDirectory ?? true;
        var visibility = tenant.Status != TenantStatus.Active ? DirectoryVisibility.Inactive
            : settings is null || settings.OnlineMenuMode == OnlineMenuMode.Off ? DirectoryVisibility.MenuOff
            : !listed ? DirectoryVisibility.HiddenByOwner
            : !await PublicDirectorySnapshot.MenuProducts(context).AnyAsync(p => p.TenantId == tenant.Id, cancellationToken)
                ? DirectoryVisibility.NoProducts
                : DirectoryVisibility.Shown;
        return new DirectoryProfileDto
        {
            Category = tenant.DirectoryCategory,
            SuggestedCategory = BusinessCategoryDefaults.Suggest(tenant.Industry, tenant.BusinessType),
            Description = tenant.PublicDescription,
            Listed = listed,
            Visibility = visibility
        };
    }
}

using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

[RequiresPermission(TenantPermission.ManageCatalog)]
public record DeleteProductCommand(Guid Id) : IRequest<Result>;

public class DeleteProductCommandValidator : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;

    public DeleteProductCommandHandler(
        IApplicationDbContext context,
        IFileStorageService fileStorageService)
    {
        _context = context;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            product.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanManageProductUnit(tenantCapabilities, product.Unit))
        {
            return Result.Failure("El negocio no tiene habilitado el módulo requerido para este tipo de artículo.");
        }

        if (!string.IsNullOrEmpty(product.ImageUrl))
            await _fileStorageService.DeleteVariantsAsync(
                product.ImageUrl,
                TenantStorageFolders.ProductImages(product.TenantId),
                cancellationToken);

        _context.Products.Remove(product);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

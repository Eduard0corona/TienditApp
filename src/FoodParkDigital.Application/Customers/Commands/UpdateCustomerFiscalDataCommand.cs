using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Commands;

/// <summary>
/// The customer's fiscal data (Fase 28A): the next invoice of their orders proposes it. Without data it is forgotten.
/// </summary>
[RequiresPermission(TenantPermission.ManageCustomers)]
public record UpdateCustomerFiscalDataCommand(Guid CustomerId, CfdiReceiverInput? FiscalData) : IRequest<Result<CustomerDto>>;

public class UpdateCustomerFiscalDataCommandValidator : AbstractValidator<UpdateCustomerFiscalDataCommand>
{
    public UpdateCustomerFiscalDataCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.FiscalData!).SetValidator(new CfdiReceiverInputValidator()).When(x => x.FiscalData is not null);
    }
}

public class UpdateCustomerFiscalDataCommandHandler : IRequestHandler<UpdateCustomerFiscalDataCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateCustomerFiscalDataCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CustomerDto>> Handle(UpdateCustomerFiscalDataCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.CustomerId);

        try
        {
            var data = request.FiscalData;
            customer.SetFiscalData(data is null
                ? null
                : CfdiReceiver.Create(data.Rfc, data.Name, data.PostalCode, data.FiscalRegime, data.CfdiUse));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CustomerDto>(ex.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CustomerDto.From(customer));
    }
}

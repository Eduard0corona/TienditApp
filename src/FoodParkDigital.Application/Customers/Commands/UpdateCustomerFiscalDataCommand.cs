using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Customers.Entities;
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
    private readonly ICurrentUserService _currentUser;

    public UpdateCustomerFiscalDataCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerDto>> Handle(UpdateCustomerFiscalDataCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.CustomerId);

        var previous = customer.FiscalData;
        try
        {
            var data = request.FiscalData;
            customer.SetFiscalData(data is null
                ? null
                : CfdiReceiver.Create(data.Rfc, data.Name, data.PostalCode, data.FiscalRegime, data.CfdiUse), customer.InvoiceEmail);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CustomerDto>(ex.Message);
        }

        if (CustomerFiscalDataAudit.Entry(customer, previous, _currentUser.UserName, "Clientes") is { } changed)
            _context.AuditLogs.Add(changed);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CustomerDto.From(customer));
    }
}

/// <summary>
/// The customer's next invoices propose their fiscal data, whether it was changed in Clientes or kept while invoicing in
/// Pedidos: who changed it, from where and from which RFC to which is recorded.
/// </summary>
public static class CustomerFiscalDataAudit
{
    public const string Action = "CustomerFiscalDataUpdated";

    /// <summary>The audit entry of the change, or none when the data stayed the same.</summary>
    public static AuditLog? Entry(Customer customer, CfdiReceiver? previous, string? performedBy, string from)
        => Equals(previous, customer.FiscalData)
            ? null
            : new AuditLog(
                tenantId: customer.TenantId,
                entityName: nameof(Customer),
                entityId: customer.Id.ToString(),
                action: Action,
                performedBy: performedBy ?? "system",
                metadata: JsonSerializer.Serialize(new { From = from, PreviousRfc = previous?.Rfc, Rfc = customer.FiscalData?.Rfc }));
}

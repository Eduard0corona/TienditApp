using System.Globalization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>Who the invoice is for; without it the sale is invoiced to the general public.</summary>
public record CfdiReceiverInput
{
    public string? Rfc { get; init; }
    public string? Name { get; init; }
    public string? PostalCode { get; init; }
    public string? FiscalRegime { get; init; }
    public string? CfdiUse { get; init; }
}

/// <summary>Previews the CFDI 4.0 invoice of an order. Nothing is stamped.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record GetOrderCfdiDraftQuery : IRequest<Result<CfdiDraftDto>>
{
    public Guid OrderId { get; init; }
    public CfdiReceiverInput? Receiver { get; init; }
}

public class GetOrderCfdiDraftQueryValidator : AbstractValidator<GetOrderCfdiDraftQuery>
{
    public GetOrderCfdiDraftQueryValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        When(x => x.Receiver is not null, () =>
        {
            RuleFor(x => x.Receiver!.Rfc)
                .NotEmpty()
                .Must(rfc => SatCatalog.IsValidRfc(rfc?.Trim().ToUpperInvariant()))
                .WithMessage("El RFC del cliente no tiene un formato válido.");
            RuleFor(x => x.Receiver!.Name)
                .NotEmpty().WithMessage("Escribe el nombre o razón social del cliente como aparece en su constancia.")
                .MaximumLength(254);
            RuleFor(x => x.Receiver!.PostalCode)
                .Must(SatCatalog.IsValidPostalCode)
                .WithMessage("El código postal fiscal del cliente tiene 5 dígitos.");
            RuleFor(x => x.Receiver!.FiscalRegime)
                .Must(regime => regime is not null && SatCatalog.FiscalRegimes.Contains(regime))
                .WithMessage("Elige el régimen fiscal del cliente.");
            RuleFor(x => x.Receiver!.CfdiUse)
                .Must(use => use is not null && SatCatalog.CfdiUses.Contains(use))
                .WithMessage("Elige el uso del CFDI.");
        });
    }
}

public class GetOrderCfdiDraftQueryHandler : IRequestHandler<GetOrderCfdiDraftQuery, Result<CfdiDraftDto>>
{
    private readonly IApplicationDbContext _context;

    public GetOrderCfdiDraftQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CfdiDraftDto>> Handle(GetOrderCfdiDraftQuery request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, order.TenantId, cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, order.OrderType))
            throw new EntityNotFoundException("Order", request.OrderId);

        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<CfdiDraftDto>("Una orden cancelada no se factura.");

        var tenant = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == order.TenantId, cancellationToken)
            ?? throw new EntityNotFoundException("Tenant", order.TenantId);

        var items = order.Items.ToList();
        var amounts = CfdiAmountCalculator.Calculate(
            order.PricesIncludeTax,
            items.Select(item => new CfdiLineInput(item.Id, item.Quantity, item.TotalPrice, item.VatRate, item.TaxObject)).ToList());
        var amountsByLine = amounts.Concepts.ToDictionary(concept => concept.LineId);

        var draft = new CfdiDraftDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            SubTotal = amounts.SubTotal,
            TotalTransferred = amounts.TotalTransferred,
            Total = amounts.Total,
            OrderTotal = order.Total,
            Difference = amounts.Total - order.Total,
            Concepts = items.Select(item =>
            {
                var concept = amountsByLine[item.Id];
                return new CfdiConceptDto
                {
                    OrderItemId = item.Id,
                    ProductCode = item.SatProductCode,
                    UnitCode = item.SatUnitCode,
                    Description = item.ProductName,
                    Quantity = concept.Quantity,
                    UnitValue = concept.UnitValue,
                    Amount = concept.Amount,
                    TaxObject = SatCatalog.Code(concept.TaxObject),
                    Transfer = concept.Transfer is null ? null : ToDto(concept.Transfer)
                };
            }).ToList(),
            Transfers = amounts.Transfers.Select(ToDto).ToList()
        };

        SetIssuer(draft, tenant.Rfc, tenant.LegalName, tenant.TaxRegime, tenant.Address?.ZipCode);
        SetReceiver(draft, request.Receiver);
        SetPayment(draft, order.Balance, order.Payments.Select(payment => (payment.Method, payment.Amount)).ToList());

        if (draft.Difference != 0)
        {
            draft.Blockers.Add(
                $"La factura suma {Money(draft.Total)} y la venta {Money(draft.OrderTotal)}: el redondeo no se pudo repartir entre los conceptos.");
        }

        var genericLines = draft.Concepts.Count(concept => concept.ProductCode == SatCatalog.GenericProductCode);
        if (genericLines > 0)
        {
            draft.Warnings.Add(genericLines == 1
                ? "Un concepto usa la clave genérica 01010101: asígnale su clave del SAT en el producto."
                : $"{genericLines} conceptos usan la clave genérica 01010101: asígnales su clave del SAT en cada producto.");
        }

        if (order.RefundedAmount > 0)
        {
            draft.Warnings.Add(
                $"La venta tiene reembolsos por {Money(order.RefundedAmount)}: esta factura ampara la venta completa y lo devuelto requiere una nota de crédito.");
        }

        draft.IsReadyToStamp = draft.Blockers.Count == 0;
        return Result.Success(draft);
    }

    private static void SetIssuer(CfdiDraftDto draft, string? rfc, string? legalName, string? taxRegime, string? postalCode)
    {
        var normalizedRfc = rfc?.Trim().ToUpperInvariant();
        draft.Issuer = new CfdiPartyDto
        {
            Rfc = normalizedRfc,
            Name = legalName?.Trim(),
            FiscalRegime = SatCatalog.RegimeCode(taxRegime)
        };
        draft.IssuedAtPostalCode = postalCode?.Trim();

        if (!SatCatalog.IsValidRfc(normalizedRfc))
            draft.Blockers.Add("Captura el RFC del negocio en Mi negocio.");
        if (string.IsNullOrWhiteSpace(draft.Issuer.Name))
            draft.Blockers.Add("Captura la razón social del negocio, tal como aparece en su constancia de situación fiscal.");
        if (draft.Issuer.FiscalRegime is null)
            draft.Blockers.Add("Elige el régimen fiscal del negocio en Mi negocio.");
        if (!SatCatalog.IsValidPostalCode(draft.IssuedAtPostalCode))
            draft.Blockers.Add("Captura el código postal fiscal del negocio (5 dígitos) en Mi negocio.");
    }

    private static void SetReceiver(CfdiDraftDto draft, CfdiReceiverInput? receiver)
    {
        if (receiver is null)
        {
            // A customer without fiscal data: the sale goes to the general public with the issuer's postal code.
            draft.IsGeneralPublic = true;
            draft.Receiver = new CfdiPartyDto
            {
                Rfc = SatCatalog.GeneralPublicRfc,
                Name = SatCatalog.GeneralPublicName,
                FiscalRegime = SatCatalog.GeneralPublicRegime,
                PostalCode = draft.IssuedAtPostalCode,
                CfdiUse = SatCatalog.GeneralPublicUse
            };
            draft.Warnings.Add(
                "Sin datos fiscales del cliente la venta va a público en general y se factura en la factura global del periodo.");
            return;
        }

        draft.Receiver = new CfdiPartyDto
        {
            Rfc = receiver.Rfc!.Trim().ToUpperInvariant(),
            Name = receiver.Name!.Trim().ToUpperInvariant(),
            FiscalRegime = receiver.FiscalRegime,
            PostalCode = receiver.PostalCode,
            CfdiUse = receiver.CfdiUse
        };
    }

    private static void SetPayment(CfdiDraftDto draft, decimal balance, IReadOnlyList<(OrderPaymentMethod Method, decimal Amount)> payments)
    {
        if (balance > 0 || payments.Count == 0)
        {
            draft.PaymentMethod = "PPD";
            draft.PaymentForm = SatCatalog.PaymentFormToBeDefined;
            draft.Warnings.Add(
                "La venta no está pagada por completo: se factura en parcialidades (PPD) y cada cobro requiere un complemento de pago.");
            return;
        }

        // A sale paid in full declares the form that paid the largest amount.
        var main = payments
            .GroupBy(payment => payment.Method)
            .Select(group => (Method: group.Key, Amount: group.Sum(payment => payment.Amount)))
            .OrderByDescending(group => group.Amount)
            .ThenBy(group => group.Method)
            .First();
        draft.PaymentMethod = "PUE";
        draft.PaymentForm = SatCatalog.PaymentForm(main.Method);
    }

    private static CfdiTransferDto ToDto(CfdiTransfer transfer) => new()
    {
        Base = transfer.Base,
        FactorType = transfer.FactorType,
        RateOrQuota = transfer.RateOrQuota,
        Amount = transfer.IsExempt ? null : transfer.Amount
    };

    private static string Money(decimal amount) => amount.ToString("$#,0.00", CultureInfo.InvariantCulture);
}

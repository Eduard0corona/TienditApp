using System.Globalization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
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
        RuleFor(x => x.Receiver!).SetValidator(new CfdiReceiverInputValidator()).When(x => x.Receiver is not null);
    }
}

public class GetOrderCfdiDraftQueryHandler : IRequestHandler<GetOrderCfdiDraftQuery, Result<CfdiDraftDto>>
{
    public const string DeliveryFeeDescription = "Envío a domicilio";
    public const string DeliveryFeeWarning =
        "El envío a domicilio va como concepto propio con la clave 78102203 (servicios de envío) y la unidad E48: "
        + "confirma con tu contador que es la clave que te corresponde.";
    public const string VisitChargeDescription = "Cargo por visita a domicilio";

    /// <summary>The visit charge goes with the code of the service sold (or the generic one) and the unit of a service.</summary>
    public static string VisitChargeWarning(string productCode)
        => $"El cargo por visita va como concepto propio con la clave {productCode} y la unidad E48: "
            + "confirma con tu contador que es la clave que te corresponde.";

    private readonly IApplicationDbContext _context;

    public GetOrderCfdiDraftQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CfdiDraftDto>> Handle(GetOrderCfdiDraftQuery request, CancellationToken cancellationToken)
    {
        var built = await BuildAsync(_context, request.OrderId, request.Receiver, cancellationToken);
        return built.IsSuccess ? Result.Success(built.Value.Draft) : Result.Failure<CfdiDraftDto>(built.Error!);
    }

    /// <summary>
    /// The CFDI draft of an order with the order and the business it was built from: what the preview shows is what an
    /// invoice stamps (Fase 28A).
    /// </summary>
    public static async Task<Result<OrderCfdiDraft>> BuildAsync(
        IApplicationDbContext context, Guid orderId, CfdiReceiverInput? receiver, CancellationToken cancellationToken)
    {
        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Options)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", orderId);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(context, order.TenantId, cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, order.OrderType))
            throw new EntityNotFoundException("Order", orderId);

        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<OrderCfdiDraft>("Una orden cancelada no se factura.");

        var tenant = await context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == order.TenantId, cancellationToken)
            ?? throw new EntityNotFoundException("Tenant", order.TenantId);

        var items = order.Items.Where(item => item.ActiveQuantity > 0).ToList();
        var deliveryFee = order.Delivery is { Fee: > 0 } delivery ? delivery : null;
        var visitCharge = order.VisitCharge;
        var amounts = CfdiAmountCalculator.Calculate(order.PricesIncludeTax, Lines(order));
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
                    // The chosen variants and extras are part of what was sold.
                    Description = item.Describe(),
                    Quantity = concept.Quantity,
                    UnitValue = concept.UnitValue,
                    Amount = concept.Amount,
                    TaxObject = SatCatalog.Code(concept.TaxObject),
                    Transfer = concept.Transfer is null ? null : ToDto(concept.Transfer)
                };
            }).ToList(),
            Transfers = amounts.Transfers.Select(ToDto).ToList()
        };
        if (deliveryFee is not null)
        {
            var concept = amountsByLine[deliveryFee.Id];
            draft.Concepts.Add(new CfdiConceptDto
            {
                OrderItemId = deliveryFee.Id,
                IsDeliveryFee = true,
                ProductCode = SatCatalog.DeliveryFeeProductCode,
                UnitCode = SatCatalog.ServiceUnitCode,
                Description = DeliveryFeeDescription,
                Quantity = concept.Quantity,
                UnitValue = concept.UnitValue,
                Amount = concept.Amount,
                TaxObject = SatCatalog.Code(concept.TaxObject),
                Transfer = concept.Transfer is null ? null : ToDto(concept.Transfer)
            });
            draft.Warnings.Add(DeliveryFeeWarning);
        }
        if (visitCharge is not null)
        {
            // Going to the customer is part of the service: the code of the first service sold, or the generic one.
            var productCode = items.FirstOrDefault(item => OrderProductClassifier.IsServiceUnit(item.Unit))?.SatProductCode
                ?? SatCatalog.GenericProductCode;
            var concept = amountsByLine[visitCharge.Id];
            draft.Concepts.Add(new CfdiConceptDto
            {
                OrderItemId = visitCharge.Id,
                IsVisitCharge = true,
                ProductCode = productCode,
                UnitCode = SatCatalog.ServiceUnitCode,
                Description = VisitChargeDescription,
                Quantity = concept.Quantity,
                UnitValue = concept.UnitValue,
                Amount = concept.Amount,
                TaxObject = SatCatalog.Code(concept.TaxObject),
                Transfer = concept.Transfer is null ? null : ToDto(concept.Transfer)
            });
            draft.Warnings.Add(VisitChargeWarning(productCode));
        }

        SetIssuer(draft, tenant.Rfc, tenant.LegalName, tenant.TaxRegime, tenant.Address?.ZipCode);
        SetReceiver(draft, receiver);
        // What the business forgave was not paid (Fase 26B): the invoice goes for the whole sale, so it stays unpaid here.
        SetPayment(draft, order.Total - order.PaidAmount, order.Payments.Select(payment => (payment.Method, payment.Amount)).ToList());
        if (order.WrittenOffAmount > 0)
            draft.Warnings.Add(WrittenOffWarning(order.WrittenOffAmount));
        if (order.IsDeliveryPlatformOrder)
            AddDeliveryPlatformWarnings(draft, order, hasReceiver: receiver is not null);
        // Paid online (Fase 24I): Mercado Pago charged it for the business, an intermediary of payments.
        if (order.Payments.Any(payment => payment.Method == OrderPaymentMethod.Online))
            draft.Warnings.Add(
                "Este pedido se pagó en línea con Mercado Pago: la factura lleva la forma de pago 31 (intermediario de pagos). "
                + "Mercado Pago te descuenta su comisión y puede retenerte ISR e IVA; confirma con tu contador cómo facturarlo.");
        // Paid with the balance the customer left before (Fase 26D): an advance applied.
        if (order.Payments.Any(payment => payment.Method == OrderPaymentMethod.CustomerBalance))
            draft.Warnings.Add(CustomerBalanceWarning);

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
        return Result.Success(new OrderCfdiDraft(draft, order, tenant));
    }

    /// <summary>
    /// What a sale charged, as the lines of its CFDI: dishes cancelled from a tab are not invoiced. The delivery fee, or the
    /// visit charge of the sale of an appointment at the customer's address, is one more line, with the rate it was charged
    /// with. The global invoice (Fase 28B) adds up the same lines.
    /// </summary>
    public static List<CfdiLineInput> Lines(Order order)
    {
        var lines = order.Items
            .Where(item => item.ActiveQuantity > 0)
            .Select(item => new CfdiLineInput(item.Id, item.ActiveQuantity, item.ChargedPrice, item.VatRate, item.TaxObject))
            .ToList();
        if (order.Delivery is { Fee: > 0 } delivery)
            lines.Add(new CfdiLineInput(delivery.Id, 1m, delivery.Fee, delivery.FeeVatRate, CfdiTaxObject.Subject));
        if (order.VisitCharge is { } visitCharge)
            lines.Add(new CfdiLineInput(visitCharge.Id, 1m, visitCharge.Fee, visitCharge.FeeVatRate, CfdiTaxObject.Subject));
        return lines;
    }

    private static void SetIssuer(CfdiDraftDto draft, string? rfc, string? legalName, string? taxRegime, string? postalCode)
    {
        draft.Issuer = Issuer(rfc, legalName, taxRegime);
        draft.IssuedAtPostalCode = postalCode?.Trim();
        draft.Blockers.AddRange(IssuerBlockers(draft.Issuer, draft.IssuedAtPostalCode));
    }

    /// <summary>The business as the CFDI's issuer, from Mi negocio.</summary>
    public static CfdiPartyDto Issuer(string? rfc, string? legalName, string? taxRegime) => new()
    {
        Rfc = rfc?.Trim().ToUpperInvariant(),
        Name = legalName?.Trim(),
        FiscalRegime = SatCatalog.RegimeCode(taxRegime)
    };

    /// <summary>What Mi negocio is missing for the business to issue a CFDI.</summary>
    public static IEnumerable<string> IssuerBlockers(CfdiPartyDto issuer, string? postalCode)
    {
        if (!SatCatalog.IsValidRfc(issuer.Rfc))
            yield return "Captura el RFC del negocio en Mi negocio.";
        if (string.IsNullOrWhiteSpace(issuer.Name))
            yield return "Captura la razón social del negocio, tal como aparece en su constancia de situación fiscal.";
        if (issuer.FiscalRegime is null)
            yield return "Elige el régimen fiscal del negocio en Mi negocio.";
        if (!SatCatalog.IsValidPostalCode(postalCode))
            yield return "Captura el código postal fiscal del negocio (5 dígitos) en Mi negocio.";
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

    /// <summary>
    /// Part of the sale was forgiven (Fase 26B, ADR 0034): the invoice goes for the whole sale, and what was forgiven needs a
    /// credit note (CFDI de egreso) that TienditApp does not issue yet.
    /// </summary>
    public static string WrittenOffWarning(decimal writtenOff) =>
        $"Se condonaron {OrderStatusLabels.FormatMoney(writtenOff)} de esta venta: la factura va por el total y lo condonado " +
        "requiere una nota de crédito (CFDI de egreso), que TienditApp todavía no genera. Consúltalo con tu contador.";

    private static void SetPayment(CfdiDraftDto draft, decimal balance, IReadOnlyList<(OrderPaymentMethod Method, decimal Amount)> payments)
    {
        if (balance > 0 || payments.Count == 0)
        {
            draft.PaymentMethod = CfdiInvoice.PaidInInstallments;
            draft.PaymentForm = SatCatalog.PaymentFormToBeDefined;
            draft.Warnings.Add(InstallmentsWarning);
            // «Otro» and the customer's balance have no form of the SAT for a payment (Fase 28C): their complement is not issued here.
            if (payments.Any(payment => SatCatalog.PaymentComplementForm(payment.Method) is null))
                draft.Warnings.Add(PaymentWithoutComplementWarning);
            return;
        }

        draft.PaymentMethod = CfdiInvoice.PaidInOneExhibition;
        draft.PaymentForm = SatCatalog.PaymentForm(MainPaymentMethod(payments));
        // «Otro» is none of the SAT's forms, and "to be defined" (99) is only for a sale paid in installments (PPD).
        if (draft.PaymentForm == SatCatalog.PaymentFormToBeDefined)
            draft.Blockers.Add(OtherPaymentBlocker);
    }

    /// <summary>
    /// A sale not paid in full is invoiced in installments (PPD, form 99, Fase 28C): TienditApp issues the complement of each of
    /// its payments, the ones received before the invoice included.
    /// </summary>
    public const string InstallmentsWarning =
        "La venta no está pagada por completo: se factura en parcialidades (PPD) con la forma de pago 99 (por definir), y TienditApp "
        + "emite el complemento de pago de cada cobro, también de los que ya recibiste. Cada complemento vence el día 5 del mes siguiente al cobro.";

    public const string PaymentWithoutComplementWarning =
        "Hay cobros registrados como «Otro» o con saldo a favor del cliente: no tienen una forma de pago del SAT para su complemento "
        + "y TienditApp no lo emite. Consúltalo con tu contador.";

    /// <summary>A sale paid in full declares the form that paid the largest amount (and so does a global invoice, Fase 28B).</summary>
    public static OrderPaymentMethod MainPaymentMethod(IEnumerable<(OrderPaymentMethod Method, decimal Amount)> payments)
        => payments
            .GroupBy(payment => payment.Method)
            .Select(group => (Method: group.Key, Amount: group.Sum(payment => payment.Amount)))
            .OrderByDescending(group => group.Amount)
            .ThenBy(group => group.Method)
            .First()
            .Method;

    /// <summary>A sale paid in full (PUE) is invoiced with the form it was paid with: «Otro» says none.</summary>
    public const string OtherPaymentBlocker =
        "El pago principal de la venta es «Otro», que no es una forma de pago del SAT: una venta pagada se factura con la forma "
        + "en que se pagó. Cobra con efectivo, tarjeta, transferencia o monedero las ventas que vayas a facturar.";

    /// <summary>
    /// What a customer paid with the balance they left before (Fase 26D, ADR 0036) is an advance applied: when it is the main
    /// method the invoice declares form 30. The CFDI of the advance when it was received and the credit note that applies
    /// it are the accountant's: this draft does not issue them.
    /// </summary>
    public const string CustomerBalanceWarning =
        "Parte de esta venta se pagó con saldo a favor del cliente (un anticipo): si es la forma de pago principal, la factura "
        + "lleva la forma de pago 30 (aplicación de anticipos). El anticipo se factura cuando se recibe y su aplicación lleva "
        + "un CFDI de egreso: TienditApp no los genera; confirma con tu contador cómo facturarlos.";

    /// <summary>
    /// An order of Rappi, Uber Eats or DiDi Food. When the platform charged it, the invoice declares form 31
    /// (intermediary of payments) and the platform withholds ISR and IVA from the sale; either way such orders usually
    /// go in the global invoice of the period. The platform shares no customer data, so an invoice to a named customer
    /// only makes sense when they gave theirs. To be confirmed with the accountant: the CFDI of withholdings is the
    /// platform's, not part of this draft.
    /// </summary>
    private static void AddDeliveryPlatformWarnings(CfdiDraftDto draft, Order order, bool hasReceiver)
    {
        var platform = OrderChannelRules.DisplayName(order.Channel);
        draft.Warnings.Add(order.Payments.Any(payment => payment.Method == OrderPaymentMethod.Platform)
            ? $"{platform} cobró este pedido en su app: la factura lleva la forma de pago 31 (intermediario de pagos) y "
              + $"normalmente va en la factura global del periodo. {platform} te retiene ISR e IVA de la venta; confirma con "
              + "tu contador cómo facturar los pedidos de plataformas."
            : $"Pedido de {platform} que cobra el negocio: normalmente va en la factura global del periodo. Confirma con tu "
              + "contador cómo facturar los pedidos de plataformas.");
        if (hasReceiver)
        {
            draft.Warnings.Add(
                $"{platform} no comparte los datos de sus clientes: factura este pedido a nombre de alguien solo si te dio "
                + "sus datos fiscales, y déjalo fuera de la factura global.");
        }
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

/// <summary>The CFDI draft of an order, with the order (read without tracking) and its business.</summary>
public sealed record OrderCfdiDraft(CfdiDraftDto Draft, Order Order, Tenant Tenant);

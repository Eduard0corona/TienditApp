using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Fiscal;
using FluentValidation;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>The receiver of an invoice as the business types it: the SAT checks it against the customer's constancia.</summary>
public class CfdiReceiverInputValidator : AbstractValidator<CfdiReceiverInput>
{
    public CfdiReceiverInputValidator()
    {
        RuleFor(x => x.Rfc)
            .NotEmpty()
            .Must(rfc => SatCatalog.IsValidRfc(rfc?.Trim().ToUpperInvariant()))
            .WithMessage("El RFC del cliente no tiene un formato válido.");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Escribe el nombre o razón social del cliente como aparece en su constancia.")
            .MaximumLength(CfdiReceiver.MaxNameLength);
        RuleFor(x => x.PostalCode)
            .Must(SatCatalog.IsValidPostalCode)
            .WithMessage("El código postal fiscal del cliente tiene 5 dígitos.");
        RuleFor(x => x.FiscalRegime)
            .Must(regime => regime is not null && SatCatalog.FiscalRegimes.Contains(regime))
            .WithMessage("Elige el régimen fiscal del cliente.");
        RuleFor(x => x.CfdiUse)
            .Must(use => use is not null && SatCatalog.CfdiUses.Contains(use))
            .WithMessage("Elige el uso del CFDI.");
        RuleFor(x => x.CfdiUse)
            .Must((input, use) => SatCatalog.UseAppliesToRegime(use!, input.FiscalRegime!))
            .When(x => x.CfdiUse is not null && SatCatalog.CfdiUses.Contains(x.CfdiUse)
                && x.FiscalRegime is not null && SatCatalog.FiscalRegimes.Contains(x.FiscalRegime))
            .WithMessage(input => CfdiReceiver.UseNotForRegimeMessage(input.CfdiUse!, input.FiscalRegime!));
    }
}

/// <summary>A receiver as an invoice and a customer keep it.</summary>
public class CfdiReceiverDto
{
    public string Rfc { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string PostalCode { get; set; } = null!;
    public string FiscalRegime { get; set; } = null!;
    public string CfdiUse { get; set; } = null!;

    public static CfdiReceiverDto From(CfdiReceiver receiver) => new()
    {
        Rfc = receiver.Rfc,
        Name = receiver.Name,
        PostalCode = receiver.PostalCode,
        FiscalRegime = receiver.FiscalRegime,
        CfdiUse = receiver.CfdiUse
    };
}

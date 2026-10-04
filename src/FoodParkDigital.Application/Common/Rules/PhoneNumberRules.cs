namespace FoodParkDigital.Application.Common.Rules;

public static class PhoneNumberRules
{
    /// <summary>Message for a typed phone that does not normalize to E.164 (answered as 400, not 500).</summary>
    public const string InvalidPhoneMessage =
        "El teléfono no es válido. Escribe 10 dígitos o el número con lada internacional (por ejemplo +52 55 1234 5678).";
}

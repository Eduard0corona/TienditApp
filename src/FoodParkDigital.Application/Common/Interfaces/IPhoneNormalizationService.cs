namespace FoodParkDigital.Application.Common.Interfaces;

public interface IPhoneNormalizationService
{
    string NormalizeToE164(string rawPhone, string defaultCountry = "MX");
}

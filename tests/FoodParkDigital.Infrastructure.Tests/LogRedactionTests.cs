using FoodParkDigital.Infrastructure.Services;

namespace FoodParkDigital.Infrastructure.Tests;

public class LogRedactionTests
{
    [Theory]
    [InlineData("+525512345678", "***5678")]
    [InlineData("55 1234 5678", "***5678")]
    [InlineData("+1 (555) 010-9999", "***9999")]
    [InlineData("1234567", "***")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    public void MaskPhoneNumber_KeepsOnlyTheLastFourDigitsOfFullNumbers(string? phoneNumber, string expected)
    {
        Assert.Equal(expected, LogRedaction.MaskPhoneNumber(phoneNumber));
    }

    [Theory]
    [InlineData("ana.cliente@example.com", "a***@e***.com")]
    [InlineData("owner@negocio.com.mx", "o***@n***.mx")]
    [InlineData("ana@localhost", "a***@l***")]
    [InlineData("sin-arroba", "***")]
    [InlineData("@example.com", "***")]
    [InlineData("ana@", "***")]
    [InlineData(null, "***")]
    public void MaskEmail_HidesMailboxAndDomainNames(string? email, string expected)
    {
        Assert.Equal(expected, LogRedaction.MaskEmail(email));
    }

    [Theory]
    [InlineData("""{"code": 21211, "message": "The 'To' number +525512345678 is not a valid phone number."}""", 21211)]
    [InlineData("""{"error": {"message": "Recipient phone number not in allowed list", "code": 131030}}""", 131030)]
    [InlineData("""{"code": "21211"}""", null)]
    [InlineData("""["unexpected"]""", null)]
    [InlineData("<html>502 Bad Gateway</html>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetProviderErrorCode_ReturnsOnlyNumericProviderCodes(string? responseBody, int? expected)
    {
        Assert.Equal(expected, LogRedaction.GetProviderErrorCode(responseBody));
    }
}

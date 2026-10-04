using FoodParkDigital.Infrastructure.Services;

namespace FoodParkDigital.Application.Tests;

public class PhoneNormalizationServiceTests
{
    private readonly PhoneNormalizationService _service = new();

    [Theory]
    [InlineData("+52 55 1234 5678", "+525512345678")]
    [InlineData("5512345678", "+525512345678")]
    [InlineData("00 1 415 555 2671", "+14155552671")]
    public void NormalizeToE164_ShouldNormalizeCommonFormats(string rawPhone, string expected)
    {
        var normalized = _service.NormalizeToE164(rawPhone, "MX");
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void NormalizeToE164_ShouldThrow_WhenInvalidPhone()
    {
        Assert.Throws<ArgumentException>(() => _service.NormalizeToE164("abc", "MX"));
    }

    [Theory]
    [InlineData("55 1234 5678", "+525512345678")]
    [InlineData("+52 55 1234 5678", "+525512345678")]
    public void TryNormalizeToE164_ShouldNormalizeValidPhones(string rawPhone, string expected)
    {
        Assert.True(_service.TryNormalizeToE164(rawPhone, "MX", out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("+0 55 1234 5678")]
    [InlineData("")]
    [InlineData(null)]
    public void TryNormalizeToE164_ShouldReturnFalse_InsteadOfThrowing(string? rawPhone)
    {
        Assert.False(_service.TryNormalizeToE164(rawPhone, "MX", out var normalized));
        Assert.Null(normalized);
    }
}

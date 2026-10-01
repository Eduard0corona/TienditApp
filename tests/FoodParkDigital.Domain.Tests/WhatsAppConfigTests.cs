using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

public class WhatsAppConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_ShouldStoreNullPhoneNumberId_WhenValueIsBlank(string? phoneNumberId)
    {
        var config = new WhatsAppConfig(Guid.NewGuid());

        config.Update(false, null, phoneNumberId, null, null);

        Assert.Null(config.PhoneNumberId);
    }

    [Fact]
    public void Update_ShouldTrimPhoneNumberId_WhenValueIsPresent()
    {
        var config = new WhatsAppConfig(Guid.NewGuid());

        config.Update(true, "token", "  1234567890  ", "waba", null);

        Assert.Equal("1234567890", config.PhoneNumberId);
    }

    [Fact]
    public void Update_ShouldClearPhoneNumberId_WhenPreviouslyConfiguredValueIsBlanked()
    {
        var config = new WhatsAppConfig(Guid.NewGuid());
        config.Update(true, "token", "1234567890", "waba", null);

        config.Update(false, null, "", "waba", null);

        Assert.Null(config.PhoneNumberId);
    }
}

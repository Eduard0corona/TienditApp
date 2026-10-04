using FoodParkDigital.Infrastructure.Services;

namespace FoodParkDigital.Infrastructure.Tests;

public class EmailConfirmationLinkTests
{
    [Fact]
    public void ConfirmationLink_ShouldOpenTheAppPage_NotTheApi()
    {
        var userId = Guid.NewGuid().ToString();

        var link = EmailService.BuildEmailConfirmationLink("https://app.tienditapp.mx/", userId, "CfDJ8+abc/def==");

        Assert.Equal($"https://app.tienditapp.mx/confirm-email?userId={userId}&token=CfDJ8%2Babc%2Fdef%3D%3D", link);
        Assert.DoesNotContain("/api/", link);
    }
}

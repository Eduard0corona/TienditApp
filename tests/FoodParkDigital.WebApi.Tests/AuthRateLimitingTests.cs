using System.Reflection;
using FoodParkDigital.WebApi.Controllers;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Only credential and code endpoints carry the "auth" rate limit. Refresh, profile and tenant endpoints
/// run on every screen of every device of a business: limiting them made the client end sessions.
/// </summary>
public class AuthRateLimitingTests
{
    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.Login))]
    [InlineData(typeof(AuthController), nameof(AuthController.RegisterBusiness))]
    [InlineData(typeof(AuthController), nameof(AuthController.RegisterCustomer))]
    [InlineData(typeof(AuthController), nameof(AuthController.ConfirmEmail))]
    [InlineData(typeof(AuthController), nameof(AuthController.ResendConfirmationEmail))]
    [InlineData(typeof(AuthController), nameof(AuthController.ConfirmPhone))]
    [InlineData(typeof(AuthController), nameof(AuthController.ResendPhoneConfirmation))]
    [InlineData(typeof(AuthController), nameof(AuthController.ForgotPassword))]
    [InlineData(typeof(AuthController), nameof(AuthController.ResetPassword))]
    [InlineData(typeof(AuthController), nameof(AuthController.ChangePassword))]
    [InlineData(typeof(TwoFactorController), nameof(TwoFactorController.Login2FA))]
    [InlineData(typeof(TwoFactorController), nameof(TwoFactorController.Verify2FA))]
    public void CredentialAndCodeEndpoints_ShouldBeRateLimited(Type controller, string action)
    {
        Assert.Equal("auth", PolicyOf(controller, action));
    }

    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.RefreshToken))]
    [InlineData(typeof(AuthController), nameof(AuthController.Logout))]
    [InlineData(typeof(AuthController), nameof(AuthController.GetProfile))]
    [InlineData(typeof(AuthController), nameof(AuthController.SwitchTenant))]
    public void SessionEndpoints_ShouldNotBeRateLimited(Type controller, string action)
    {
        Assert.Null(PolicyOf(controller, action));
    }

    private static string? PolicyOf(Type controller, string action)
    {
        var method = controller.GetMethod(action)
            ?? throw new InvalidOperationException($"{controller.Name}.{action} does not exist.");
        return method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
            ?? controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
    }
}

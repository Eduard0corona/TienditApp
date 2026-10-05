using System.Net;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests;

public class SmsServiceLoggingTests
{
    private const string PhoneNumber = "+525512345678";
    private const string NationalNumber = "5512345678";
    private const string MaskedPhoneNumber = "***5678";
    private const string OtpCode = "908172";
    private const string TwilioAuthToken = "twilio-test-auth-token";
    private const string OwnerAccessToken = "owner-whatsapp-access-token";

    private const string TwilioInvalidNumberResponse = """
        {"code": 21211, "message": "The 'To' number +525512345678 is not a valid phone number.", "more_info": "https://www.twilio.com/docs/errors/21211", "status": 400}
        """;

    private const string MetaRecipientNotAllowedResponse = """
        {"error": {"message": "(#131030) Recipient phone number not in allowed list", "type": "OAuthException", "code": 131030, "error_data": {"messaging_product": "whatsapp", "details": "Recipient phone number +525512345678 not in allowed list"}, "fbtrace_id": "AbC123"}}
        """;

    private static readonly Dictionary<string, string?> NoSmsProvider = new()
    {
        ["SmsSettings:Provider"] = ""
    };

    private static readonly Dictionary<string, string?> TwilioSettings = new()
    {
        ["SmsSettings:Provider"] = "Twilio",
        ["SmsSettings:AccountSid"] = "ACtest-account-sid",
        ["SmsSettings:AuthToken"] = TwilioAuthToken,
        ["SmsSettings:FromNumber"] = "+15005550006"
    };

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task SendVerificationCodeAsync_WithoutSmsProvider_LogsOnlyMaskedWarningOutsideDevelopment(string environmentName)
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.InternalServerError);
        await using var context = InMemoryApplicationDbContext.Create();
        var logger = new CapturingLogger<SmsService>();
        var service = CreateService(environmentName, NoSmsProvider, context, http, logger);

        await service.SendVerificationCodeAsync(PhoneNumber, OtpCode);

        Assert.Empty(http.Requests);
        AssertNoCodeOrPhoneNumber(logger);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(MaskedPhoneNumber, warning.Message);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_WithIncompleteTwilioSettings_LogsOnlyMaskedWarningOutsideDevelopment()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.InternalServerError);
        await using var context = InMemoryApplicationDbContext.Create();
        var logger = new CapturingLogger<SmsService>();
        var settings = new Dictionary<string, string?>(TwilioSettings) { ["SmsSettings:AuthToken"] = "" };
        var service = CreateService(Environments.Production, settings, context, http, logger);

        await service.SendVerificationCodeAsync(PhoneNumber, OtpCode);

        Assert.Empty(http.Requests);
        AssertNoCodeOrPhoneNumber(logger);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains(MaskedPhoneNumber, warning.Message);
    }

    [Fact]
    public async Task SendVerificationCodeAsync_WithoutSmsProviderInDevelopment_LogsCodeForLocalTesting()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.InternalServerError);
        await using var context = InMemoryApplicationDbContext.Create();
        var logger = new CapturingLogger<SmsService>();
        var service = CreateService(Environments.Development, NoSmsProvider, context, http, logger);

        await service.SendVerificationCodeAsync(PhoneNumber, OtpCode);

        Assert.Contains(logger.Entries, entry => entry.Message.Contains(PhoneNumber));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains(OtpCode));
    }

    [Fact]
    public async Task SendVerificationCodeAsync_WhenTwilioRejectsSms_LogsStatusAndProviderCodeOutsideDevelopment()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.BadRequest, TwilioInvalidNumberResponse);
        await using var context = InMemoryApplicationDbContext.Create();
        var logger = new CapturingLogger<SmsService>();
        var service = CreateService(Environments.Production, TwilioSettings, context, http, logger);

        await service.SendVerificationCodeAsync(PhoneNumber, OtpCode);

        var request = Assert.Single(http.Requests);
        Assert.Equal("api.twilio.com", request.Uri.Host);
        Assert.Contains(OtpCode, request.Content);
        AssertNoCodeOrPhoneNumber(logger);
        Assert.Null(logger.FirstMentioning(TwilioAuthToken));
        Assert.Null(logger.FirstMentioning("not a valid phone number"));
        var error = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Equal(HttpStatusCode.BadRequest, error["StatusCode"]);
        Assert.Equal(21211, error["ProviderErrorCode"]);
        Assert.Contains(MaskedPhoneNumber, error.Message);
    }

    [Fact]
    public async Task SendSmsAsync_WhenTwilioRejectsSmsInDevelopment_LogsProviderResponseForDebugging()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.BadRequest, TwilioInvalidNumberResponse);
        await using var context = InMemoryApplicationDbContext.Create();
        var logger = new CapturingLogger<SmsService>();
        var service = CreateService(Environments.Development, TwilioSettings, context, http, logger);

        await service.SendSmsAsync(PhoneNumber, "Mensaje de prueba");

        Assert.Contains(logger.Entries, entry => entry.Message.Contains("not a valid phone number"));
    }

    [Fact]
    public async Task SendVerificationCodeAsync_WhenOwnerWhatsAppRejectsOtp_LogsStatusAndProviderCodeOutsideDevelopment()
    {
        var http = new StubHttpMessageHandler(HttpStatusCode.BadRequest, MetaRecipientNotAllowedResponse);
        await using var context = InMemoryApplicationDbContext.Create();
        // Platform OTPs only go out through a WhatsApp account owned by an active SUPER_ADMIN.
        var owner = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "owner@example.com",
            Email = "owner@example.com",
            FirstName = "Owner",
            LastName = "User",
            Role = UserRole.SuperAdmin,
            IsActive = true
        };
        context.Users.Add(owner);
        var ownerConfig = new OwnerWhatsAppConfig(owner.Id);
        ownerConfig.Update(
            isEnabled: true,
            useForOtp: true,
            accessToken: OwnerAccessToken,
            phoneNumberId: "109876543210",
            wabaId: null,
            displayPhoneNumber: null,
            otpTemplateName: null,
            otpTemplateLanguageCode: null);
        context.OwnerWhatsAppConfigs.Add(ownerConfig);
        await context.SaveChangesAsync();
        var logger = new CapturingLogger<SmsService>();
        var whatsAppLogger = new CapturingLogger<WhatsAppCloudClient>();
        var service = CreateService(Environments.Production, NoSmsProvider, context, http, logger, whatsAppLogger);

        await service.SendVerificationCodeAsync(PhoneNumber, OtpCode);

        // The single Meta client sends the code and logs the rejection (Twilio is not configured, so no SMS follows).
        var request = Assert.Single(http.Requests);
        Assert.Equal("graph.facebook.com", request.Uri.Host);
        Assert.Contains(OtpCode, request.Content);
        AssertNoCodeOrPhoneNumber(logger);
        AssertNoCodeOrPhoneNumber(whatsAppLogger);
        Assert.Null(logger.FirstMentioning(OwnerAccessToken));
        Assert.Null(whatsAppLogger.FirstMentioning(OwnerAccessToken));
        Assert.Null(whatsAppLogger.FirstMentioning("not in allowed list"));
        var error = Assert.Single(whatsAppLogger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(131030, error["ProviderErrorCode"]);
        Assert.Contains(MaskedPhoneNumber, error.Message);
    }

    private static SmsService CreateService(
        string environmentName,
        Dictionary<string, string?> settings,
        ApplicationDbContext context,
        StubHttpMessageHandler http,
        CapturingLogger<SmsService> logger,
        CapturingLogger<WhatsAppCloudClient>? whatsAppLogger = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var environment = new TestHostEnvironment(environmentName);
        var whatsApp = new WhatsAppCloudClient(http, environment, whatsAppLogger ?? new CapturingLogger<WhatsAppCloudClient>());
        return new SmsService(context, configuration, http, whatsApp, environment, logger);
    }

    private static void AssertNoCodeOrPhoneNumber<T>(CapturingLogger<T> logger)
    {
        Assert.Null(logger.FirstMentioning(OtpCode));
        Assert.Null(logger.FirstMentioning(NationalNumber));
    }
}

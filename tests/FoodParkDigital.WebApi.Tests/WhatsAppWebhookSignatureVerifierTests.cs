using System.Text;
using FoodParkDigital.WebApi.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using static FoodParkDigital.WebApi.Tests.WhatsAppWebhookTestSupport;

namespace FoodParkDigital.WebApi.Tests;

public class WhatsAppWebhookSignatureVerifierTests
{
    private static readonly byte[] Body =
        Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[]}""");

    [Fact]
    public void Verify_ShouldAccept_WhenSignatureMatchesBody()
    {
        var verifier = CreateVerifier(Environments.Production, AppSecret);

        Assert.Equal(WhatsAppWebhookSignatureStatus.Valid, verifier.Verify(Body, Sign(Body, AppSecret)));
    }

    [Fact]
    public void Verify_ShouldAccept_WhenSignatureHexIsUppercase()
    {
        var verifier = CreateVerifier(Environments.Production, AppSecret);
        var signature = "sha256=" + Sign(Body, AppSecret)["sha256=".Length..].ToUpperInvariant();

        Assert.Equal(WhatsAppWebhookSignatureStatus.Valid, verifier.Verify(Body, signature));
    }

    [Fact]
    public void Verify_ShouldReject_WhenBodyWasTampered()
    {
        var verifier = CreateVerifier(Environments.Production, AppSecret);
        var tampered = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[{}]}""");

        Assert.Equal(WhatsAppWebhookSignatureStatus.InvalidSignature, verifier.Verify(tampered, Sign(Body, AppSecret)));
    }

    [Fact]
    public void Verify_ShouldReject_WhenSignedWithAnotherSecret()
    {
        var verifier = CreateVerifier(Environments.Production, AppSecret);

        Assert.Equal(
            WhatsAppWebhookSignatureStatus.InvalidSignature,
            verifier.Verify(Body, Sign(Body, "attacker-secret")));
    }

    [Fact]
    public void Verify_ShouldReject_WhenSignatureHeaderIsMissing()
    {
        var verifier = CreateVerifier(Environments.Production, AppSecret);

        Assert.Equal(WhatsAppWebhookSignatureStatus.MissingSignature, verifier.Verify(Body, StringValues.Empty));
        Assert.Equal(WhatsAppWebhookSignatureStatus.MissingSignature, verifier.Verify(Body, ""));
    }

    [Theory]
    [InlineData("sha256=")]
    [InlineData("sha256=not-a-hex-digest")]
    [InlineData("sha1=0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Verify_ShouldReject_WhenSignatureHeaderIsMalformed(string signature)
    {
        var verifier = CreateVerifier(Environments.Production, AppSecret);

        Assert.Equal(WhatsAppWebhookSignatureStatus.InvalidSignature, verifier.Verify(Body, signature));
    }

    [Fact]
    public void Verify_ShouldReject_WhenSignatureHeaderIsRepeated()
    {
        var verifier = CreateVerifier(Environments.Production, AppSecret);
        var valid = Sign(Body, AppSecret);

        Assert.Equal(
            WhatsAppWebhookSignatureStatus.InvalidSignature,
            verifier.Verify(Body, new StringValues([valid, valid])));
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Staging", "")]
    [InlineData("Development", "   ")]
    public void Verify_ShouldFailClosed_WhenAppSecretIsNotConfigured(string environment, string? appSecret)
    {
        var verifier = CreateVerifier(environment, appSecret);

        Assert.Equal(
            WhatsAppWebhookSignatureStatus.SecretNotConfigured,
            verifier.Verify(Body, Sign(Body, "any-secret")));
    }

    [Fact]
    public void Verify_ShouldSkipVerification_WhenUnsignedWebhooksAreAllowedInDevelopment()
    {
        var verifier = CreateVerifier(Environments.Development, appSecret: null, allowUnsignedWebhooks: "true");

        Assert.Equal(WhatsAppWebhookSignatureStatus.VerificationDisabled, verifier.Verify(Body, StringValues.Empty));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Verify_ShouldIgnoreUnsignedWebhooksFlag_OutsideDevelopment(string environment)
    {
        var withoutSecret = CreateVerifier(environment, appSecret: null, allowUnsignedWebhooks: "true");
        var withSecret = CreateVerifier(environment, AppSecret, allowUnsignedWebhooks: "true");

        Assert.Equal(WhatsAppWebhookSignatureStatus.SecretNotConfigured, withoutSecret.Verify(Body, StringValues.Empty));
        Assert.Equal(WhatsAppWebhookSignatureStatus.MissingSignature, withSecret.Verify(Body, StringValues.Empty));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("1")]
    public void Verify_ShouldFailClosed_WhenUnsignedWebhooksFlagIsNotExplicitlyTrue(string flag)
    {
        var verifier = CreateVerifier(Environments.Development, appSecret: null, allowUnsignedWebhooks: flag);

        Assert.Equal(WhatsAppWebhookSignatureStatus.SecretNotConfigured, verifier.Verify(Body, StringValues.Empty));
    }
}

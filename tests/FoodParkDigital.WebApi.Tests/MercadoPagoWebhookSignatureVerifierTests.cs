using FoodParkDigital.Infrastructure.OnlinePayments;
using FoodParkDigital.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace FoodParkDigital.WebApi.Tests;

public class MercadoPagoWebhookSignatureVerifierTests
{
    private const string Secret = "mp-webhook-secret-for-tests";
    private const string RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";

    // Computed outside .NET with the template Mercado Pago documents: id:{data.id};request-id:{x-request-id};ts:{ts};
    private const string SignedPayment = "ts=1704908010,v1=c5dec4ab1220730a9a4b119ed3e47f1a30472bebc2f2d4581f687b6c11a32ea0";
    private const string SignedWithoutRequestId = "ts=1704908010,v1=4d9928b9b13682e94cbeb631c72c9c21c3513a68be7aadcdd12d67529aa029c8";
    private const string SignedAlphanumericId = "ts=1704908010,v1=e2ae6be09e9acf4a00fad288bc7251d87c00d5ebf89eb82aee81a8b36eb07147";

    private static MercadoPagoWebhookSignatureVerifier CreateVerifier(string? secret = Secret) =>
        new(Options.Create(new MercadoPagoOptions { WebhookSecret = secret }),
            NullLogger<MercadoPagoWebhookSignatureVerifier>.Instance);

    [Fact]
    public void Verify_ShouldAccept_WhatMercadoPagoSigned()
    {
        var verifier = CreateVerifier();

        Assert.Equal(MercadoPagoWebhookSignatureStatus.Valid, verifier.Verify("123456789", SignedPayment, RequestId));
        Assert.Equal(MercadoPagoWebhookSignatureStatus.Valid, verifier.Verify("123456789", " ts=1704908010 , v1=C5DEC4AB1220730A9A4B119ED3E47F1A30472BEBC2F2D4581F687B6C11A32EA0 ", RequestId));
    }

    [Fact]
    public void Verify_ShouldLeaveOutOfTheTemplate_WhatTheNotificationDoesNotCarry()
    {
        var verifier = CreateVerifier();

        Assert.Equal(MercadoPagoWebhookSignatureStatus.Valid, verifier.Verify("123456789", SignedWithoutRequestId, StringValues.Empty));
        Assert.Equal(MercadoPagoWebhookSignatureStatus.Valid, verifier.Verify("ABC123DEF", SignedAlphanumericId, RequestId));
    }

    [Theory]
    [InlineData("987654321", RequestId)]
    [InlineData("123456789", "another-request")]
    public void Verify_ShouldReject_WhenTheNotifiedIdOrRequestChanged(string dataId, string requestId)
    {
        Assert.Equal(
            MercadoPagoWebhookSignatureStatus.InvalidSignature,
            CreateVerifier().Verify(dataId, SignedPayment, requestId));
    }

    [Fact]
    public void Verify_ShouldReject_WhenTheTimeOrTheSecretIsAnother()
    {
        Assert.Equal(
            MercadoPagoWebhookSignatureStatus.InvalidSignature,
            CreateVerifier().Verify("123456789", SignedPayment.Replace("ts=1704908010", "ts=1704908011"), RequestId));
        Assert.Equal(
            MercadoPagoWebhookSignatureStatus.InvalidSignature,
            CreateVerifier("attacker-secret").Verify("123456789", SignedPayment, RequestId));
    }

    [Theory]
    [InlineData("v1=c5dec4ab1220730a9a4b119ed3e47f1a30472bebc2f2d4581f687b6c11a32ea0")]
    [InlineData("ts=1704908010")]
    [InlineData("ts=,v1=")]
    [InlineData("garbage")]
    public void Verify_ShouldReject_AMalformedSignature(string header)
    {
        Assert.Equal(MercadoPagoWebhookSignatureStatus.InvalidSignature, CreateVerifier().Verify("123456789", header, RequestId));
    }

    [Fact]
    public void Verify_ShouldReject_WithoutSignatureOrWithTwo()
    {
        var verifier = CreateVerifier();

        Assert.Equal(MercadoPagoWebhookSignatureStatus.MissingSignature, verifier.Verify("123456789", StringValues.Empty, RequestId));
        Assert.Equal(MercadoPagoWebhookSignatureStatus.MissingSignature, verifier.Verify("123456789", "", RequestId));
        Assert.Equal(
            MercadoPagoWebhookSignatureStatus.InvalidSignature,
            verifier.Verify("123456789", new StringValues([SignedPayment, SignedPayment]), RequestId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Verify_ShouldFailClosed_WithoutTheSecret(string? secret)
    {
        Assert.Equal(
            MercadoPagoWebhookSignatureStatus.SecretNotConfigured,
            CreateVerifier(secret).Verify("123456789", SignedPayment, RequestId));
    }
}

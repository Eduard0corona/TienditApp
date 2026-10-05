using System.Text;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Transfer receipts and service photos are private: a business keeps "/files/{key}" references to its own uploads
/// and only its members open them, through the API.
/// </summary>
public class PrivateOrderFilesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();

    [Fact]
    public void ServicePhotos_ShouldKeepOnlyTheBusinessesOwnUploads_AsPrivatePaths()
    {
        var uploaded = PhotoKey(Tenant);
        var legacy = PhotoKey(Tenant);
        var json = JsonSerializer.Serialize(new object[]
        {
            new { id = "a1", url = $"/files/{uploaded}", fileName = "  frente.jpg  " },
            // Uploaded before photos became private: kept as the private path of the same file.
            $"https://cuenta.blob.core.windows.net/uploads/{legacy}"
        });

        var error = OrderUploadRules.NormalizeServicePhotos(json, Tenant, out var normalized);

        Assert.Null(error);
        Assert.Equal(
            $$"""[{"url":"/files/{{uploaded}}","fileName":"frente.jpg"},{"url":"/files/{{legacy}}"}]""",
            normalized);
    }

    [Theory]
    [InlineData("""[{"url":"https://tracker.example/pixel.png"}]""")]
    [InlineData("""[{"url":"/files/orders/{other}/service-intake/202610/{file}.webp"}]""")]
    [InlineData("""[{"url":"/files/orders/{tenant}/payment-evidence/202610/{file}.webp"}]""")]
    [InlineData("""[{"fileName":"sin direccion.jpg"}]""")]
    [InlineData("""{"url":"/files/orders/{tenant}/service-intake/202610/{file}.webp"}""")]
    [InlineData("not json")]
    public void ServicePhotos_ShouldRefuseAnythingElse(string template)
    {
        var json = template
            .Replace("{tenant}", Tenant.ToString())
            .Replace("{other}", OtherTenant.ToString())
            .Replace("{file}", Guid.NewGuid().ToString());

        var error = OrderUploadRules.NormalizeServicePhotos(json, Tenant, out var normalized);

        Assert.Equal(OrderUploadRules.ServicePhotoError, error);
        Assert.Null(normalized);
    }

    [Fact]
    public void ServicePhotos_ShouldBeLimited()
    {
        var json = JsonSerializer.Serialize(
            Enumerable.Range(0, OrderUploadRules.MaxServicePhotos + 1).Select(_ => new { url = $"/files/{PhotoKey(Tenant)}" }));

        Assert.NotNull(OrderUploadRules.NormalizeServicePhotos(json, Tenant, out _));
        Assert.Null(OrderUploadRules.NormalizeServicePhotos("  ", Tenant, out var none));
        Assert.Null(none);
    }

    [Fact]
    public void ServiceOrder_ShouldRefuseAPhotoThatIsNotTheBusinesses()
    {
        var order = new Order(Tenant, Guid.NewGuid(), null, true, "ORD-000001", BusinessType.Service, OrderType.Service,
            "Cliente", "+525511112222", null, null);

        var error = OrderPlacement.ApplyServiceDetail(order, new ServiceOrderDetailInput
        {
            BeforePhotosJson = $$"""[{"url":"/files/{{PhotoKey(OtherTenant)}}"}]"""
        }, DateTimeOffset.UtcNow);

        Assert.Equal(OrderUploadRules.ServicePhotoError, error);
        Assert.Null(order.ServiceDetail);
    }

    [Fact]
    public async Task PrivateFile_ShouldOpenOnlyForItsBusiness_AndOnlyInItsArea()
    {
        var evidence = $"orders/{Tenant}/payment-evidence/202610/{Guid.NewGuid()}.pdf";
        var otherEvidence = $"orders/{OtherTenant}/payment-evidence/202610/{Guid.NewGuid()}.pdf";
        var storage = new MemoryStorage(evidence, otherEvidence);
        var handler = new GetPrivateOrderFileQueryHandler(new FixedTenant(Tenant), storage);

        var own = await handler.Handle(new GetPaymentEvidenceFileQuery(evidence), CancellationToken.None);
        var other = await handler.Handle(new GetPaymentEvidenceFileQuery(otherEvidence), CancellationToken.None);
        var wrongArea = await handler.Handle(new GetServicePhotoFileQuery(evidence), CancellationToken.None);
        var traversal = await handler.Handle(new GetPaymentEvidenceFileQuery($"orders/{Tenant}/payment-evidence/../{Guid.NewGuid()}.pdf"), CancellationToken.None);
        var missing = await handler.Handle(new GetPaymentEvidenceFileQuery($"orders/{Tenant}/payment-evidence/202610/{Guid.NewGuid()}.pdf"), CancellationToken.None);

        Assert.True(own.IsSuccess, own.Error);
        Assert.Equal("application/pdf", own.Value!.ContentType);
        Assert.Equal(Path.GetFileName(evidence), own.Value.FileName);
        // Another business's file, a key in another area or out of the business's folder, and a missing file all
        // answer the same way.
        Assert.All([other, wrongArea, traversal, missing], result =>
            Assert.Equal(GetPrivateOrderFileQueryHandler.NotFoundError, result.Error));
        Assert.Equal([evidence], storage.Opened);
    }

    [Theory]
    [InlineData("orders/{tenant}/payment-evidence/202610/{file}.webp", true)]
    [InlineData("orders/{tenant}/service-intake/202610/{file}.webp", true)]
    [InlineData("orders/{tenant}/payment-evidence/2026/{file}.webp", false)]
    [InlineData("orders/{tenant}/payment-evidence/202610/{file}.html", false)]
    [InlineData("orders/{tenant}/products/202610/{file}.webp", false)]
    [InlineData("products/{tenant}/202610/{file}.webp", false)]
    [InlineData("/orders/{tenant}/payment-evidence/202610/{file}.webp", false)]
    public void PrivateKeys_ShouldBeExactlyTheOnesTheAppCreates(string template, bool expected)
    {
        var key = template.Replace("{tenant}", Tenant.ToString()).Replace("{file}", Guid.NewGuid().ToString());

        Assert.Equal(expected, OrderUploadRules.TryParsePrivateKey(key, out var tenant, out _));
        if (expected)
            Assert.Equal(Tenant, tenant);
    }

    private static string PhotoKey(Guid tenantId) => $"orders/{tenantId}/service-intake/202610/{Guid.NewGuid()}.webp";

    private sealed class FixedTenant(Guid tenantId) : ICurrentTenantService
    {
        public Guid TenantId { get; } = tenantId;
        public void SetTenant(Guid tenantId) => throw new NotSupportedException();
    }

    private sealed class MemoryStorage(params string[] keys) : IPrivateFileStorage
    {
        public List<string> Opened { get; } = [];

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!keys.Contains(key))
                return Task.FromResult<Stream?>(null);

            Opened.Add(key);
            return Task.FromResult<Stream?>(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7")));
        }
    }
}

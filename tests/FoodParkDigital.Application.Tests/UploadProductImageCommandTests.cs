using System.Reflection;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class UploadProductImageCommandTests
{
    // Se comparan todas las propiedades públicas para que un campo nuevo de Product
    // quede cubierto sin tener que actualizar estas pruebas.
    private static readonly string[] PropertiesExpectedToChange =
    [
        nameof(Product.ImageUrl),
        nameof(Product.UpdatedAt),
        nameof(Product.UpdatedBy),
        nameof(Product.Category),
        nameof(Product.DomainEvents)
    ];

    [Fact]
    public async Task Handle_ShouldOnlyChangeImageUrl_ForRetailProductWithSkuAndSchedule()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var product = await fixture.SeedProductAsync(new Product(
            fixture.TenantId,
            fixture.CategoryId,
            "Cafe de olla",
            "Cafe con canela y piloncillo",
            45m,
            "MXN",
            preparationTimeMinutes: 5,
            sku: "CAF-001",
            barcode: "7501234567890",
            sortOrder: 3,
            isFeatured: true,
            tags: "bebidas,calientes",
            compareAtPrice: 55m,
            taxRate: 16m,
            calories: 120,
            allergens: "lactosa",
            stockQuantity: 40,
            lowStockThreshold: 5,
            maxQuantityPerOrder: 4,
            unit: UnitType.Portion,
            availableFromTime: new TimeSpan(7, 0, 0),
            availableToTime: new TimeSpan(12, 0, 0)));

        var persisted = await UploadAndAssertOnlyImageChangedAsync(fixture, product);

        // Fuera de su horario no debe quedar disponible las 24 horas.
        Assert.False(persisted.IsAvailableAt(new TimeSpan(20, 0, 0)));
    }

    [Fact]
    public async Task Handle_ShouldOnlyChangeImageUrl_ForTrackableServiceWithSchedule()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var previousImageUrl = $"https://storage.test/products/{fixture.TenantId}/previous";
        var service = await fixture.SeedProductAsync(new Product(
            fixture.TenantId,
            fixture.CategoryId,
            "Lavado premium",
            "Lavado profundo de calzado",
            180m,
            "MXN",
            imageUrl: previousImageUrl,
            preparationTimeMinutes: 90,
            sortOrder: 2,
            tags: "lavado,calzado",
            unit: UnitType.Service,
            availableFromTime: new TimeSpan(9, 0, 0),
            availableToTime: new TimeSpan(18, 0, 0),
            isTrackable: true,
            notifyCustomer: true));

        var persisted = await UploadAndAssertOnlyImageChangedAsync(fixture, service);

        // Debe seguir siendo un servicio rastreable para generar piezas y consumo.
        Assert.Equal(UnitType.Service, persisted.Unit);
        Assert.True(persisted.IsTrackable);
        Assert.Equal(previousImageUrl, Assert.Single(fixture.Storage.DeletedUrls));
    }

    [Fact]
    public async Task Handle_ShouldNotTouchProductOrFiles_WhenProductBelongsToAnotherTenant()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var foreignImageUrl = $"https://storage.test/products/{fixture.OtherTenantId}/existing";
        var foreignProduct = await fixture.SeedProductAsync(new Product(
            fixture.OtherTenantId,
            fixture.OtherTenantCategoryId,
            "Producto de otro tenant",
            null,
            100m,
            "MXN",
            imageUrl: foreignImageUrl));

        var handler = fixture.CreateHandler();

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => handler.Handle(CreateCommand(foreignProduct.Id), CancellationToken.None));

        Assert.Empty(fixture.Storage.UploadedUrls);
        Assert.Empty(fixture.Storage.DeletedUrls);

        fixture.SetTenant(fixture.OtherTenantId);
        var persisted = await fixture.Context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == foreignProduct.Id);
        Assert.Equal(foreignImageUrl, persisted.ImageUrl);
    }

    private static async Task<Product> UploadAndAssertOnlyImageChangedAsync(TestFixture fixture, Product product)
    {
        var previousImageUrl = product.ImageUrl;
        var before = Snapshot(product);

        var result = await fixture.CreateHandler().Handle(CreateCommand(product.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var persisted = await fixture.Context.Products
            .AsNoTracking()
            .SingleAsync(p => p.Id == product.Id);

        Assert.NotEqual(previousImageUrl, persisted.ImageUrl);
        Assert.StartsWith($"https://storage.test/products/{fixture.TenantId}/", persisted.ImageUrl);
        Assert.Equal($"{persisted.ImageUrl}_original.webp", result.Value.Original);
        Assert.Empty(ChangedProperties(before, persisted));

        return persisted;
    }

    private static UploadProductImageCommand CreateCommand(Guid productId) => new()
    {
        ProductId = productId,
        FileStream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]),
        FileName = "foto.png",
        ContentType = "image/png",
        FileSize = 4
    };

    private static Dictionary<string, object?> Snapshot(Product product) =>
        typeof(Product)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !PropertiesExpectedToChange.Contains(property.Name))
            .ToDictionary(property => property.Name, property => SnapshotValue(property.GetValue(product)));

    // Collections are read-only wrappers created on each read: compare the items they hold instead.
    private static object? SnapshotValue(object? value) => value is System.Collections.IEnumerable items and not string
        ? string.Join("|", items.Cast<object>().Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode))
        : value;

    private static List<string> ChangedProperties(Dictionary<string, object?> before, Product after) =>
        Snapshot(after)
            .Where(property => !Equals(before[property.Key], property.Value))
            .Select(property => $"{property.Key}: {before[property.Key] ?? "null"} -> {property.Value ?? "null"}")
            .ToList();

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly TestCurrentTenantService _tenantService;

        private TestFixture(
            ApplicationDbContext context,
            TestCurrentTenantService tenantService,
            Guid tenantId,
            Guid categoryId,
            Guid otherTenantId,
            Guid otherTenantCategoryId)
        {
            Context = context;
            _tenantService = tenantService;
            TenantId = tenantId;
            CategoryId = categoryId;
            OtherTenantId = otherTenantId;
            OtherTenantCategoryId = otherTenantCategoryId;
        }

        public ApplicationDbContext Context { get; }
        public FakeFileStorageService Storage { get; } = new();
        public Guid TenantId { get; }
        public Guid CategoryId { get; }
        public Guid OtherTenantId { get; }
        public Guid OtherTenantCategoryId { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new ApplicationDbContext(options, tenantService, new NullMediator());

            var tenant = new Tenant("Tienda Centro", "tienda-centro", BusinessType.Retail, "centro@test.com");
            var otherTenant = new Tenant("Tienda Norte", "tienda-norte", BusinessType.Retail, "norte@test.com");
            var category = new Category(tenant.Id, "General", null);
            var otherCategory = new Category(otherTenant.Id, "General", null);
            context.Tenants.AddRange(tenant, otherTenant);
            context.Categories.AddRange(category, otherCategory);
            await context.SaveChangesAsync();

            tenantService.SetTenant(tenant.Id);
            return new TestFixture(context, tenantService, tenant.Id, category.Id, otherTenant.Id, otherCategory.Id);
        }

        public async Task<Product> SeedProductAsync(Product product)
        {
            Context.Products.Add(product);
            await Context.SaveChangesAsync();
            return product;
        }

        public void SetTenant(Guid tenantId) => _tenantService.SetTenant(tenantId);

        public UploadProductImageCommandHandler CreateHandler() => new(
            Context,
            Storage,
            new FakeImageProcessingService(),
            _tenantService);

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public List<string> UploadedUrls { get; } = [];
        public List<string> DeletedUrls { get; } = [];

        public Task<string> UploadAsync(
            Stream fileStream,
            string fileName,
            string contentType,
            string folder,
            CancellationToken cancellationToken = default)
        {
            var url = $"https://storage.test/{folder}/{fileName}";
            UploadedUrls.Add(url);
            return Task.FromResult(url);
        }

        public Task DeleteAsync(string fileUrl, string folder, CancellationToken cancellationToken = default)
        {
            DeletedUrls.Add(fileUrl);
            return Task.CompletedTask;
        }

        public Task DeleteVariantsAsync(string baseImageUrl, string folder, CancellationToken cancellationToken = default)
        {
            DeletedUrls.Add(baseImageUrl);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeImageProcessingService : IImageProcessingService
    {
        private static readonly string[] Suffixes = ["_original", "_lg", "_md", "_sm", "_xs"];

        public Task<IReadOnlyList<ImageVariantResult>> GenerateVariantsAsync(
            Stream originalStream,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ImageVariantResult> variants = Suffixes
                .Select(suffix => new ImageVariantResult(suffix, new MemoryStream()))
                .ToList();
            return Task.FromResult(variants);
        }
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<object?>();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
    }
}

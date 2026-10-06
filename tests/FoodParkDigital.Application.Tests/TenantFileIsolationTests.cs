using System.Text.Json;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

// El tenant actual ("Tienda Centro") ejecuta los comandos con almacenamiento local real en un
// directorio temporal; los archivos de "Tienda Norte" deben sobrevivir a cualquier URL guardada.
public class TenantFileIsolationTests
{
    private static readonly string[] VariantSuffixes = ["_original", "_lg", "_md", "_sm", "_xs"];

    // URL guardada en un producto del tenant actual (dato heredado o manipulado) y ruta base, relativa a
    // uploads, de los archivos a los que apunta. {own} = tenant actual, {other} = otro tenant.
    public static TheoryData<string, string> ProductImageUrlsOutsideOwnFolder => new()
    {
        { "/uploads/products/{other}/victima", "products/{other}/victima" },
        { "/uploads/products/{own}/../{other}/victima", "products/{other}/victima" },
        { "/uploads/products/{own}/..\\{other}/victima", "products/{other}/victima" },
        { "/uploads/logos/{own}/victima", "logos/{own}/victima" }
    };

    [Theory]
    [MemberData(nameof(ProductImageUrlsOutsideOwnFolder))]
    public async Task DeleteProduct_ShouldKeepFiles_WhenImageUrlResolvesOutsideOwnFolder(
        string imageUrlTemplate,
        string targetTemplate)
    {
        await using var fixture = await TestFixture.CreateAsync();
        var targetFiles = fixture.SeedVariantFiles(fixture.Resolve(targetTemplate));
        var product = await fixture.SeedProductAsync(fixture.Resolve(imageUrlTemplate));

        var result = await new DeleteProductCommandHandler(fixture.Context, fixture.Storage)
            .Handle(new DeleteProductCommand(product.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await fixture.Context.Products.AsNoTracking().AnyAsync(p => p.Id == product.Id));
        AssertAllExist(targetFiles);
    }

    [Fact]
    public async Task DeleteProduct_ShouldDeleteOwnImageVariants()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var ownFiles = fixture.SeedVariantFiles($"products/{fixture.TenantId}/propia");
        var product = await fixture.SeedProductAsync($"/uploads/products/{fixture.TenantId}/propia");

        var result = await new DeleteProductCommandHandler(fixture.Context, fixture.Storage)
            .Handle(new DeleteProductCommand(product.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertNoneExist(ownFiles);
    }

    [Theory]
    [MemberData(nameof(ProductImageUrlsOutsideOwnFolder))]
    public async Task UploadProductImage_ShouldKeepFiles_WhenPreviousImageUrlResolvesOutsideOwnFolder(
        string imageUrlTemplate,
        string targetTemplate)
    {
        await using var fixture = await TestFixture.CreateAsync();
        var targetFiles = fixture.SeedVariantFiles(fixture.Resolve(targetTemplate));
        var product = await fixture.SeedProductAsync(fixture.Resolve(imageUrlTemplate));

        var result = await fixture.CreateUploadProductImageHandler()
            .Handle(UploadProductImage(product.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertAllExist(targetFiles);

        var persisted = await fixture.Context.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        Assert.StartsWith($"/uploads/products/{fixture.TenantId}/", persisted.ImageUrl);
        AssertAllExist(fixture.VariantPathsOfUrl(persisted.ImageUrl!));
    }

    [Fact]
    public async Task UploadProductImage_ShouldReplaceOwnPreviousVariants()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var previousFiles = fixture.SeedVariantFiles($"products/{fixture.TenantId}/anterior");
        var product = await fixture.SeedProductAsync($"/uploads/products/{fixture.TenantId}/anterior");

        var result = await fixture.CreateUploadProductImageHandler()
            .Handle(UploadProductImage(product.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertNoneExist(previousFiles);
    }

    [Fact]
    public async Task CreateProduct_ShouldIgnoreImageUrlSentByClient()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var otherTenantFiles = fixture.SeedVariantFiles($"products/{fixture.OtherTenantId}/victima");
        var json = $$"""
            {
              "categoryId": "{{fixture.CategoryId}}",
              "name": "Cafe de olla",
              "priceAmount": 45,
              "priceCurrency": "MXN",
              "imageUrl": "/uploads/products/{{fixture.OtherTenantId}}/victima"
            }
            """;
        // Mismas opciones que usa el model binding de ASP.NET Core por defecto.
        var command = JsonSerializer.Deserialize<CreateProductCommand>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var created = await new CreateProductCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(command, CancellationToken.None);

        Assert.True(created.IsSuccess, created.Error);
        Assert.Null(created.Value.ImageUrl);
        Assert.Null(created.Value.Images);
        var persisted = await fixture.Context.Products.AsNoTracking().SingleAsync(p => p.Id == created.Value.Id);
        Assert.Null(persisted.ImageUrl);

        // Cadena completa del ataque: subir una imagen (permitido a STAFF) ya no alcanza al otro tenant.
        var uploaded = await fixture.CreateUploadProductImageHandler()
            .Handle(UploadProductImage(created.Value.Id), CancellationToken.None);

        Assert.True(uploaded.IsSuccess, uploaded.Error);
        AssertAllExist(otherTenantFiles);
    }

    [Fact]
    public async Task UploadTenantLogo_ShouldKeepOtherTenantFiles_WhenLogoUrlPointsToTheirFolder()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var otherTenantFiles = fixture.SeedVariantFiles($"logos/{fixture.OtherTenantId}/logo");
        await fixture.SetLogoUrlAsync($"/uploads/logos/{fixture.OtherTenantId}/logo");

        var result = await new UploadTenantLogoCommandHandler(
                fixture.Context,
                fixture.TenantService,
                fixture.Storage,
                new FakeImageProcessingService())
            .Handle(
                new UploadTenantLogoCommand
                {
                    FileStream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]),
                    FileName = "logo.png",
                    ContentType = "image/png",
                    FileSize = 4
                },
                CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertAllExist(otherTenantFiles);
        var tenant = await fixture.Context.Tenants.AsNoTracking().SingleAsync(t => t.Id == fixture.TenantId);
        Assert.StartsWith($"/uploads/logos/{fixture.TenantId}/", tenant.LogoUrl);
    }

    [Fact]
    public async Task DeleteTenantLogo_ShouldKeepOtherTenantFiles_WhenLogoUrlPointsToTheirFolder()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var otherTenantFiles = fixture.SeedVariantFiles($"logos/{fixture.OtherTenantId}/logo");
        await fixture.SetLogoUrlAsync($"/uploads/logos/{fixture.OtherTenantId}/logo");

        var result = await fixture.CreateDeleteTenantLogoHandler()
            .Handle(new DeleteTenantLogoCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertAllExist(otherTenantFiles);
        var tenant = await fixture.Context.Tenants.AsNoTracking().SingleAsync(t => t.Id == fixture.TenantId);
        Assert.Null(tenant.LogoUrl);
    }

    [Fact]
    public async Task DeleteTenantLogo_ShouldDeleteOwnLogoVariants()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var ownFiles = fixture.SeedVariantFiles($"logos/{fixture.TenantId}/logo");
        await fixture.SetLogoUrlAsync($"/uploads/logos/{fixture.TenantId}/logo");

        var result = await fixture.CreateDeleteTenantLogoHandler()
            .Handle(new DeleteTenantLogoCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertNoneExist(ownFiles);
    }

    [Fact]
    public async Task UploadAvatar_ShouldKeepOtherTenantFiles_WhenAvatarUrlPointsToTheirFolder()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var otherTenantFiles = fixture.SeedVariantFiles($"avatars/{fixture.OtherTenantId}/avatar");
        await fixture.SetAvatarUrlAsync($"/uploads/avatars/{fixture.OtherTenantId}/avatar");

        var result = await fixture.UploadAvatarAsync();

        Assert.True(result.IsSuccess, result.Error);
        AssertAllExist(otherTenantFiles);
        var user = await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.UserId);
        // The account's own folder: the same avatar in every business of the person.
        Assert.StartsWith($"/uploads/avatars/users/{fixture.UserId}/", user.AvatarUrl);
    }

    [Fact]
    public async Task UploadAvatar_ShouldDeleteThePreviousOne_UploadedInAnotherOfTheirBusinesses()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.JoinOtherTenantAsync();
        var previous = fixture.SeedVariantFiles($"avatars/{fixture.OtherTenantId}/avatar");
        await fixture.SetAvatarUrlAsync($"/uploads/avatars/{fixture.OtherTenantId}/avatar");

        var first = await fixture.UploadAvatarAsync();
        var firstUrl = (await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.UserId)).AvatarUrl!;
        var second = await fixture.UploadAvatarAsync();

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        AssertNoneExist(previous);
        AssertNoneExist(fixture.VariantPathsOfUrl(firstUrl));
    }

    [Fact]
    public async Task DeleteAvatar_ShouldKeepOtherTenantFiles_WhenAvatarUrlPointsToTheirFolder()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var otherTenantFiles = fixture.SeedVariantFiles($"avatars/{fixture.OtherTenantId}/avatar");
        await fixture.SetAvatarUrlAsync($"/uploads/avatars/{fixture.OtherTenantId}/avatar");

        var result = await fixture.CreateDeleteAvatarHandler()
            .Handle(new DeleteAvatarCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertAllExist(otherTenantFiles);
        var user = await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.UserId);
        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public async Task DeleteAvatar_ShouldDeleteTheAvatarOfTheAccountFolder()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var ownFiles = fixture.SeedVariantFiles($"avatars/users/{fixture.UserId}/avatar");
        await fixture.SetAvatarUrlAsync($"/uploads/avatars/users/{fixture.UserId}/avatar");

        var result = await fixture.CreateDeleteAvatarHandler()
            .Handle(new DeleteAvatarCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertNoneExist(ownFiles);
        Assert.Null((await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.UserId)).AvatarUrl);
    }

    [Fact]
    public async Task DeleteAvatar_ShouldDeleteTheAvatarUploadedInASessionWithoutBusiness()
    {
        // Until Phase 9 a session without a business uploaded avatars to the folder of Guid.Empty.
        await using var fixture = await TestFixture.CreateAsync();
        var legacyFiles = fixture.SeedVariantFiles($"avatars/{Guid.Empty}/avatar");
        await fixture.SetAvatarUrlAsync($"/uploads/avatars/{Guid.Empty}/avatar");

        var result = await fixture.CreateDeleteAvatarHandler()
            .Handle(new DeleteAvatarCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertNoneExist(legacyFiles);
    }

    [Fact]
    public async Task DeleteAvatar_ShouldDeleteOwnAvatarVariants()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var ownFiles = fixture.SeedVariantFiles($"avatars/{fixture.TenantId}/avatar");
        await fixture.SetAvatarUrlAsync($"/uploads/avatars/{fixture.TenantId}/avatar");

        var result = await fixture.CreateDeleteAvatarHandler()
            .Handle(new DeleteAvatarCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        AssertNoneExist(ownFiles);
    }

    private static UploadProductImageCommand UploadProductImage(Guid productId) => new()
    {
        ProductId = productId,
        FileStream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]),
        FileName = "foto.png",
        ContentType = "image/png",
        FileSize = 4
    };

    private static void AssertAllExist(IEnumerable<string> paths) =>
        Assert.All(paths, path => Assert.True(File.Exists(path), $"Se borró {path}"));

    private static void AssertNoneExist(IEnumerable<string> paths) =>
        Assert.All(paths, path => Assert.False(File.Exists(path), $"No se borró {path}"));

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly TestCurrentTenantService _tenantService;
        private readonly string _contentRoot;

        private TestFixture(
            ApplicationDbContext context,
            TestCurrentTenantService tenantService,
            string contentRoot,
            Tenant tenant,
            Guid otherTenantId,
            Guid categoryId,
            ApplicationUser user)
        {
            Context = context;
            _tenantService = tenantService;
            _contentRoot = contentRoot;
            Tenant = tenant;
            OtherTenantId = otherTenantId;
            CategoryId = categoryId;
            User = user;
            Storage = new LocalFileStorageService(
                new TestWebHostEnvironment(contentRoot),
                NullLogger<LocalFileStorageService>.Instance);
            CurrentUser = new TestCurrentUserService(user.Id, tenant.Id);
            UserManager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(context),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                [],
                [],
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(),
                new NullLogger<UserManager<ApplicationUser>>());
        }

        public ApplicationDbContext Context { get; }
        public LocalFileStorageService Storage { get; }
        public TestCurrentUserService CurrentUser { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public ICurrentTenantService TenantService => _tenantService;
        public Tenant Tenant { get; }
        public ApplicationUser User { get; }
        public Guid TenantId => Tenant.Id;
        public Guid OtherTenantId { get; }
        public Guid CategoryId { get; }
        public Guid UserId => User.Id;

        private string UploadsRoot => Path.Combine(_contentRoot, "wwwroot", "uploads");

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
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "admin@centro.test",
                NormalizedUserName = "ADMIN@CENTRO.TEST",
                Email = "admin@centro.test",
                NormalizedEmail = "ADMIN@CENTRO.TEST",
                SecurityStamp = Guid.NewGuid().ToString(),
                FirstName = "Ana",
                LastName = "Centro",
                Role = UserRole.TenantAdmin,
                DefaultTenantId = tenant.Id,
                IsActive = true
            };
            context.Tenants.AddRange(tenant, otherTenant);
            context.Categories.Add(category);
            context.Users.Add(user);
            context.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Owner));
            await context.SaveChangesAsync();

            tenantService.SetTenant(tenant.Id);

            var contentRoot = Path.Combine(Path.GetTempPath(), "fpd-tenant-files", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(contentRoot);

            return new TestFixture(context, tenantService, contentRoot, tenant, otherTenant.Id, category.Id, user);
        }

        public string Resolve(string template) => template
            .Replace("{own}", TenantId.ToString())
            .Replace("{other}", OtherTenantId.ToString());

        // Crea las cinco variantes de basePath (relativa a wwwroot/uploads) y devuelve sus rutas.
        public IReadOnlyList<string> SeedVariantFiles(string basePath)
        {
            var paths = VariantPaths(basePath);
            foreach (var path in paths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]);
            }

            return paths;
        }

        public IReadOnlyList<string> VariantPathsOfUrl(string baseUrl) =>
            VariantPaths(baseUrl["/uploads/".Length..]);

        public async Task<Product> SeedProductAsync(string imageUrl)
        {
            var product = new Product(TenantId, CategoryId, "Producto Centro", null, 100m, "MXN", imageUrl: imageUrl);
            Context.Products.Add(product);
            await Context.SaveChangesAsync();
            return product;
        }

        public async Task SetLogoUrlAsync(string logoUrl)
        {
            Tenant.Update(Tenant.Name, Tenant.ContactEmail, Tenant.ContactPhone, logoUrl);
            await Context.SaveChangesAsync();
        }

        public async Task SetAvatarUrlAsync(string avatarUrl)
        {
            User.AvatarUrl = avatarUrl;
            await Context.SaveChangesAsync();
        }

        public UploadProductImageCommandHandler CreateUploadProductImageHandler() => new(
            Context,
            Storage,
            new FakeImageProcessingService(),
            _tenantService);

        public DeleteTenantLogoCommandHandler CreateDeleteTenantLogoHandler() => new(
            Context,
            _tenantService,
            Storage);

        public DeleteAvatarCommandHandler CreateDeleteAvatarHandler() => new(
            UserManager,
            CurrentUser,
            Context,
            Storage);

        public Task<Result<AvatarImageUrlsDto>> UploadAvatarAsync() =>
            new UploadAvatarCommandHandler(UserManager, CurrentUser, Context, Storage, new FakeImageProcessingService())
                .Handle(
                    new UploadAvatarCommand
                    {
                        FileStream = new MemoryStream([0x89, 0x50, 0x4E, 0x47]),
                        FileName = "avatar.png",
                        ContentType = "image/png",
                        FileSize = 4
                    },
                    CancellationToken.None);

        /// <summary>The account also belongs to the other business (its avatar may have been uploaded there).</summary>
        public async Task JoinOtherTenantAsync()
        {
            Context.TenantUsers.Add(new TenantUser(OtherTenantId, UserId, TenantUserRole.Staff));
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            UserManager.Dispose();
            await Context.DisposeAsync();
            if (Directory.Exists(_contentRoot))
                Directory.Delete(_contentRoot, recursive: true);
        }

        private IReadOnlyList<string> VariantPaths(string basePath) => VariantSuffixes
            .Select(suffix => Path.GetFullPath(Path.Combine(UploadsRoot, $"{basePath}{suffix}.webp")))
            .ToList();
    }

    private sealed class FakeImageProcessingService : IImageProcessingService
    {
        public Task<IReadOnlyList<ImageVariantResult>> GenerateVariantsAsync(
            Stream originalStream,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ImageVariantResult> variants = VariantSuffixes
                .Select(suffix => new ImageVariantResult(suffix, new MemoryStream([0x52, 0x49, 0x46, 0x46])))
                .ToList();
            return Task.FromResult(variants);
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "FoodParkDigital.Application.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRootPath, "wwwroot");
    }

    private sealed class TestCurrentUserService(Guid userId, Guid tenantId) : ICurrentUserService
    {
        public string? UserId => userId.ToString();
        public string? UserName => "admin@centro.test";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "OWNER";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => tenantId;
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

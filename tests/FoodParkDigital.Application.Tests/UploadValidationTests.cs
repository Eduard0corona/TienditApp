using System.Buffers.Binary;
using System.Text;
using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace FoodParkDigital.Application.Tests;

public class UploadValidationTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 }, UploadedFileKind.Jpeg)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, UploadedFileKind.Png)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, UploadedFileKind.Webp)]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, UploadedFileKind.Pdf)]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E }, UploadedFileKind.Unknown)]
    [InlineData(new byte[] { 0xFF, 0xD8 }, UploadedFileKind.Unknown)]
    public void FileSignature_ShouldDetectTheRealType(byte[] header, UploadedFileKind expected)
    {
        Assert.Equal(expected, FileSignature.Detect(header));
    }

    [Fact]
    public async Task IntakePhoto_ShouldBeReencodedAsWebp_WithoutMetadataAndScaledDown()
    {
        var storage = new RecordingStorage();
        await using var jpeg = CreateJpegWithLocation(3000, 1000);

        var result = await NewIntakeHandler(storage).Handle(Command(jpeg, "foto.jpg", "image/jpeg"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var upload = Assert.Single(storage.Uploads);
        Assert.Equal("image/webp", upload.ContentType);
        Assert.EndsWith(".webp", upload.FileName);
        Assert.Equal(UploadedFileKind.Webp, FileSignature.Detect(upload.Content));
        using var stored = Image.Load(upload.Content);
        Assert.Equal(2048, stored.Width);
        Assert.Null(stored.Metadata.ExifProfile);
    }

    [Fact]
    public async Task IntakePhoto_ShouldRejectHtmlRenamedAsImage()
    {
        var storage = new RecordingStorage();
        await using var html = new MemoryStream(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));

        var result = await NewIntakeHandler(storage).Handle(Command(html, "foto.png", "image/png"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
    }

    [Fact]
    public async Task IntakePhoto_ShouldRejectCorruptImagesWithAValidSignature()
    {
        var storage = new RecordingStorage();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Encoding.ASCII.GetBytes("not really a png")).ToArray();
        await using var corrupt = new MemoryStream(bytes);

        var result = await NewIntakeHandler(storage).Handle(Command(corrupt, "foto.png", "image/png"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
    }

    [Fact]
    public async Task ImageNormalization_ShouldRejectImagesDeclaringTooManyPixels()
    {
        // A few bytes that declare a 20000 x 20000 canvas: decoding it would allocate gigabytes.
        await using var bomb = new MemoryStream(CreatePngHeader(20_000, 20_000));

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            new ImageProcessingService().NormalizeToWebpAsync(bomb, 2048));
        Assert.Contains("demasiado grande", exception.Message);
    }

    [Fact]
    public async Task PaymentEvidence_ShouldStorePdfAsPdf_AndRejectOtherDocuments()
    {
        var storage = new RecordingStorage();
        var handler = NewEvidenceHandler(storage);
        await using var pdf = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF"));
        await using var zip = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0]);

        var stored = await handler.Handle(EvidenceCommand(pdf, "comprobante.pdf", "application/pdf"), CancellationToken.None);
        var rejected = await handler.Handle(EvidenceCommand(zip, "comprobante.pdf", "application/pdf"), CancellationToken.None);

        Assert.True(stored.IsSuccess, stored.Error);
        Assert.Equal("application/pdf", Assert.Single(storage.Uploads).ContentType);
        Assert.EndsWith(".pdf", stored.Value!.FileName);
        Assert.True(rejected.IsFailure);
    }

    [Theory]
    [InlineData("/uploads/orders/{tenant}/payment-evidence/202610/{file}.webp", true)]
    [InlineData("https://cuenta.blob.core.windows.net/archivos/orders/{tenant}/payment-evidence/202610/{file}.pdf", true)]
    [InlineData("", true)]
    [InlineData("/uploads/orders/{other}/payment-evidence/202610/{file}.webp", false)]
    [InlineData("http://cuenta.blob.core.windows.net/archivos/orders/{tenant}/payment-evidence/202610/{file}.pdf", false)]
    [InlineData("javascript:alert(1)//orders/{tenant}/payment-evidence/202610/{file}.webp", false)]
    [InlineData("//evil.example/orders/{tenant}/payment-evidence/202610/{file}.webp", false)]
    [InlineData("/uploads/../orders/{tenant}/payment-evidence/202610/{file}.webp", false)]
    [InlineData("/uploads/orders/{tenant}/payment-evidence/202610/{file}.html", false)]
    public void PaymentEvidenceUrl_ShouldOnlyAcceptFilesUploadedForTheTenant(string template, bool expected)
    {
        var tenantId = Guid.NewGuid();
        var url = template
            .Replace("{tenant}", tenantId.ToString())
            .Replace("{other}", Guid.NewGuid().ToString())
            .Replace("{file}", Guid.NewGuid().ToString());

        Assert.Equal(expected, OrderUploadRules.IsOwnPaymentEvidence(url, tenantId));
    }

    [Fact]
    public async Task AddPayment_ShouldRejectEvidenceThatWasNotUploadedForTheTenant()
    {
        var tenant = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new TestDbContext(options, tenant);
        var order = new Order(tenant.TenantId, Guid.NewGuid(), null, true, "ORD-000001", BusinessType.Retail, OrderType.Retail,
            "Cliente", "+525511112222", null, null);
        order.AddItem(Guid.NewGuid(), "Producto", 1, 100m);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();
        var handler = new AddOrderPaymentCommandHandler(context, new FixedClock(DateTimeOffset.UtcNow), mapper);

        var external = await handler.Handle(new AddOrderPaymentCommand
        {
            OrderId = order.Id,
            Amount = 50m,
            Method = OrderPaymentMethod.Transfer,
            EvidenceUrl = "https://evil.example/comprobante.png"
        }, CancellationToken.None);
        var uploaded = await handler.Handle(new AddOrderPaymentCommand
        {
            OrderId = order.Id,
            Amount = 50m,
            Method = OrderPaymentMethod.Transfer,
            EvidenceUrl = $"/uploads/orders/{tenant.TenantId}/payment-evidence/202610/{Guid.NewGuid()}.webp"
        }, CancellationToken.None);

        Assert.True(external.IsFailure);
        Assert.True(uploaded.IsSuccess, uploaded.Error);
        Assert.Equal(50m, uploaded.Value!.PaidAmount);
    }

    private static MemoryStream CreateJpegWithLocation(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Make, "Camara de prueba");
        image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        stream.Position = 0;
        return stream;
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth; color type 0 (grayscale), default compression, filter and interlace

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        stream.Write(number);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    private static UploadServiceIntakePhotoCommand Command(Stream stream, string fileName, string contentType) => new()
    {
        FileStream = stream,
        FileName = fileName,
        ContentType = contentType,
        FileSize = stream.Length
    };

    private static UploadOrderPaymentEvidenceCommand EvidenceCommand(Stream stream, string fileName, string contentType) => new()
    {
        FileStream = stream,
        FileName = fileName,
        ContentType = contentType,
        FileSize = stream.Length
    };

    private static UploadServiceIntakePhotoCommandHandler NewIntakeHandler(IFileStorageService storage) =>
        new(new TestCurrentTenantService(), storage, new ImageProcessingService());

    private static UploadOrderPaymentEvidenceCommandHandler NewEvidenceHandler(IFileStorageService storage) =>
        new(new TestCurrentTenantService(), storage, new ImageProcessingService());

    private sealed class RecordingStorage : IFileStorageService
    {
        public List<(string FileName, string ContentType, string Folder, byte[] Content)> Uploads { get; } = [];

        public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, string folder, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await fileStream.CopyToAsync(copy, cancellationToken);
            Uploads.Add((fileName, contentType, folder, copy.ToArray()));
            return $"/uploads/{folder}/{fileName}";
        }

        public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteVariantsAsync(string baseImageUrl, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService)
        : ApplicationDbContext(options, tenantService, new NullMediator())
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property(o => o.Version).IsRequired(false).ValueGeneratedNever();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
    }
}

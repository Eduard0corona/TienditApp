using System.Text;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using static FoodParkDigital.Application.Tests.QuoteFlowTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The photos of what happens a customer sends with the visit they requested online (Fase 29H, ADR 0062): from the link of the
/// request and without an account, up to five, while the visit waits or is agendada and before its time; read for what they
/// are, saved again without where they were taken and kept private for the business, which sees them in the agenda and in the
/// quote of the visit, and nobody else opens.
/// </summary>
public class VisitProblemPhotoFlowTests
{
    private const string Token = "vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv";

    [Fact]
    public async Task FromItsLink_TheCustomer_ShouldSendAPhoto_KeptPrivateForTheBusiness_WithoutWhereItWasTaken()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await RequestedAsync(fixture, tenant.Id);
        // Another business is in context: the link carries its own.
        await TradeAsync(fixture, slug: "otro");
        var storage = new MemoryPrivateStorage();

        var sent = await SendAsync(fixture, storage, Token, Jpeg(3000, 1000, withLocation: true));

        Assert.True(sent!.IsSuccess, sent.Error);
        Assert.Equal((1, true), (sent.Value!.ProblemPhotos, sent.Value.CanAddPhotos));
        var (key, file) = Assert.Single(storage.Files);
        Assert.True(OrderUploadRules.TryParsePrivateKey(key, out var owner, out var area));
        Assert.Equal((tenant.Id, OrderUploadRules.VisitProblemArea, "image/webp"), (owner, area, file.ContentType));
        Assert.Equal(UploadedFileKind.Webp, FileSignature.Detect(file.Content));
        using (var stored = Image.Load(file.Content))
        {
            Assert.Equal(2048, stored.Width);
            Assert.Null(stored.Metadata.ExifProfile);
        }
        var photo = await fixture.Context.AppointmentProblemPhotos.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((tenant.Id, visit.Id, 0, key), (photo.TenantId, photo.AppointmentId, photo.Position, photo.FileKey));
        // That the appointment stays as it was, its rowversion too, is read on SQL Server (CrossTenantVisitProblemPhotosSqlServerTests).
        fixture.Context.ChangeTracker.Clear();

        // The business sees it in its agenda and opens it; the link shows only how many.
        fixture.TenantService.SetTenant(tenant.Id);
        var agenda = await new GetAppointmentsQueryHandler(fixture.Context).Handle(new GetAppointmentsQuery(), CancellationToken.None);
        Assert.Equal([$"/files/{key}"], Assert.Single(agenda.Items).ProblemPhotos);
        var one = await new GetAppointmentByIdQueryHandler(fixture.Context).Handle(new GetAppointmentByIdQuery(visit.Id), CancellationToken.None);
        Assert.Equal([$"/files/{key}"], one.ProblemPhotos);
        var opened = await OpenAsync(fixture, storage, key);
        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal("image/webp", opened.Value!.ContentType);
        var tracked = await new GetPublicAppointmentQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicAppointmentQuery(Token), CancellationToken.None);
        Assert.Equal((1, true), (tracked!.ProblemPhotos, tracked.CanAddPhotos));
    }

    [Fact]
    public async Task UpToFive_ThenRefused_BeforeItsFileIsRead()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        await RequestedAsync(fixture, tenant.Id);
        var storage = new MemoryPrivateStorage();
        var images = new CountingImages();

        Result<CustomerAppointmentDto>? last = null;
        for (var n = 0; n < HomeVisitRules.MaxProblemPhotos; n++)
            last = await SendAsync(fixture, storage, Token, Jpeg(40, 30), images: images);
        await using var sixthPhoto = Jpeg(40, 30);
        var sixth = await SendAsync(fixture, storage, Token, sixthPhoto, images: images);

        Assert.True(last!.IsSuccess, last.Error);
        Assert.Equal((5, false), (last.Value!.ProblemPhotos, last.Value.CanAddPhotos));
        Assert.Equal(HomeVisitRules.ProblemPhotosFullMessage, sixth!.Error);
        // Refused before its file is read: the sixth is neither read nor decoded.
        Assert.Equal((5, 0L), (images.Decoded, sixthPhoto.Position));
        Assert.Equal(5, storage.Files.Count);
        Assert.Equal([0, 1, 2, 3, 4],
            await fixture.Context.AppointmentProblemPhotos.IgnoreQueryFilters().OrderBy(p => p.Position).Select(p => p.Position).ToListAsync());
    }

    [Fact]
    public async Task FromItsTime_OnceDeclined_OrForABookingAtTheBusiness_ShouldBeRefused_AndNothingSaved()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await RequestedAsync(fixture, tenant.Id);
        var declined = await RequestedAsync(fixture, tenant.Id, token: new string('d', 43), start: SalesTestFixture.Now.AddDays(4));
        declined.Decline();
        var atTheBusiness = Appointment.RequestOnline(
            tenant.Id, "Valeria", "5522107765", Guid.NewGuid(), "Revisión", SalesTestFixture.Now.AddDays(2), SalesTestFixture.Now.AddDays(2).AddHours(1),
            null, null, Guid.NewGuid(), new string('b', 43));
        fixture.Context.Appointments.Add(atTheBusiness);
        await fixture.Context.SaveChangesAsync();
        var storage = new MemoryPrivateStorage();
        var images = new CountingImages();

        Assert.Equal(HomeVisitRules.ProblemPhotosClosedMessage, (await SendAsync(fixture, storage, new string('d', 43), Jpeg(40, 30), images: images))!.Error);
        Assert.Equal(HomeVisitRules.ProblemPhotosNotAVisitMessage, (await SendAsync(fixture, storage, new string('b', 43), Jpeg(40, 30), images: images))!.Error);
        fixture.Clock.UtcNow = visit.StartAt;
        Assert.Equal(HomeVisitRules.ProblemPhotosClosedMessage, (await SendAsync(fixture, storage, Token, Jpeg(40, 30), images: images))!.Error);

        // Refused before decoding anything.
        Assert.Equal(0, images.Decoded);
        Assert.Empty(storage.Files);
        Assert.Equal(0, await fixture.Context.AppointmentProblemPhotos.IgnoreQueryFilters().CountAsync());
        var tracked = await new GetPublicAppointmentQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicAppointmentQuery(new string('b', 43)), CancellationToken.None);
        Assert.Equal((0, false), (tracked!.ProblemPhotos, tracked.CanAddPhotos));
    }

    [Fact]
    public async Task ALinkUnknown_InOtherCase_OrExpired_ShouldNotExist()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await RequestedAsync(fixture, tenant.Id);
        var storage = new MemoryPrivateStorage();

        Assert.Null(await SendAsync(fixture, storage, new string('x', 43), Jpeg(40, 30)));
        Assert.Null(await SendAsync(fixture, storage, Token.ToUpperInvariant(), Jpeg(40, 30)));
        fixture.Clock.UtcNow = visit.StartAt.AddDays(CustomerAppointments.LinkDays + 1);
        Assert.Null(await SendAsync(fixture, storage, Token, Jpeg(40, 30)));
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task WhatIsNotAPhoto_ShouldBeRefused_ByWhatItIs_AndNothingSaved()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        await RequestedAsync(fixture, tenant.Id);
        var storage = new MemoryPrivateStorage();
        var corrupt = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Encoding.ASCII.GetBytes("not really a png")).ToArray();

        var html = await SendAsync(fixture, storage, Token, new MemoryStream(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")));
        var broken = await SendAsync(fixture, storage, Token, new MemoryStream(corrupt));

        Assert.Equal(VisitProblemPhotos.NotAPhotoMessage, html!.Error);
        Assert.True(broken!.IsFailure);
        Assert.Empty(storage.Files);
        Assert.Equal(0, await fixture.Context.AppointmentProblemPhotos.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task TooManyBeingRead_ShouldBeRefused_ToSendItAgain()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        await RequestedAsync(fixture, tenant.Id);
        var storage = new MemoryPrivateStorage();
        var gate = new PublicPhotoGate(1, TimeSpan.Zero);
        using var busy = await gate.EnterAsync(CancellationToken.None);

        var refused = await SendAsync(fixture, storage, Token, Jpeg(40, 30), gate);
        busy!.Dispose();
        var sent = await SendAsync(fixture, storage, Token, Jpeg(40, 30), gate);

        Assert.Equal(VisitProblemPhotos.BusyMessage, refused!.Error);
        Assert.True(sent!.IsSuccess, sent.Error);
        Assert.Single(storage.Files);
    }

    [Fact]
    public async Task TwoSentAtOnce_ShouldTakeTheNextPlace_AndNeverMakeASixth()
    {
        var meanwhile = new PhotoSentMeanwhile();
        await using var fixture = SalesTestFixture.Create(null, meanwhile);
        var tenant = await TradeAsync(fixture);
        var visit = await RequestedAsync(fixture, tenant.Id);
        meanwhile.Tenant = fixture.TenantService;
        var storage = new MemoryPrivateStorage();

        meanwhile.Armed = 1;
        var second = await SendAsync(fixture, storage, Token, Jpeg(40, 30));

        Assert.True(second!.IsSuccess, second.Error);
        Assert.Equal(2, second.Value!.ProblemPhotos);
        Assert.Equal([0, 1],
            await fixture.Context.AppointmentProblemPhotos.IgnoreQueryFilters().Where(p => p.AppointmentId == visit.Id)
                .OrderBy(p => p.Position).Select(p => p.Position).ToListAsync());

        // The other photos filled the visit meanwhile: this one is refused.
        meanwhile.Armed = 3;
        var sixth = await SendAsync(fixture, storage, Token, Jpeg(40, 30));
        Assert.Equal(HomeVisitRules.ProblemPhotosFullMessage, sixth!.Error);
        Assert.Equal(5, await fixture.Context.AppointmentProblemPhotos.IgnoreQueryFilters().CountAsync());

        // Anything else that fails is not tried again as another place.
        meanwhile.Armed = 0;
        meanwhile.FailsAnyway = true;
        await using var empty = SalesTestFixture.Create(null, meanwhile);
        var other = await TradeAsync(empty);
        await RequestedAsync(empty, other.Id);
        meanwhile.Tenant = empty.TenantService;
        meanwhile.Saves = 0;
        await Assert.ThrowsAsync<DbUpdateException>(() => SendAsync(empty, new MemoryPrivateStorage(), Token, Jpeg(40, 30)));
        Assert.Equal(1, meanwhile.Saves);
    }

    [Fact]
    public async Task OnlyAPhotoOfTheBusiness_ShouldOpen()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        await RequestedAsync(fixture, tenant.Id);
        var storage = new MemoryPrivateStorage();
        var key = (await SendAsync(fixture, storage, Token, Jpeg(40, 30)))!.IsSuccess ? storage.Files.Keys.Single() : null!;
        // A file saved but kept by no photo, another business's photo and another area's key.
        var loose = $"{OrderUploadRules.VisitProblemFolder(tenant.Id, SalesTestFixture.Now)}/{Guid.NewGuid()}.webp";
        await storage.SaveAsync(loose, new MemoryStream([1, 2, 3]), "image/webp");
        var other = await TradeAsync(fixture, slug: "otro");
        var theirs = key.Replace(tenant.Id.ToString(), other.Id.ToString(), StringComparison.Ordinal);
        await storage.SaveAsync(theirs, new MemoryStream([1, 2, 3]), "image/webp");
        var intake = key.Replace(OrderUploadRules.VisitProblemArea, OrderUploadRules.ServiceIntakeArea, StringComparison.Ordinal);

        fixture.TenantService.SetTenant(tenant.Id);
        Assert.True((await OpenAsync(fixture, storage, key)).IsSuccess);
        foreach (var refused in new[] { loose, theirs, intake, "orders/../secret.webp" })
            Assert.Equal(GetPrivateOrderFileQueryHandler.NotFoundError, (await OpenAsync(fixture, storage, refused)).Error);
        fixture.TenantService.SetTenant(other.Id);
        Assert.Equal(GetPrivateOrderFileQueryHandler.NotFoundError, (await OpenAsync(fixture, storage, key)).Error);
    }

    [Fact]
    public async Task TheQuoteOfTheVisit_ShouldShowWhatHappens_AndItsPhotos_OnlyForAVisitRequestedOnline()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await RequestedAsync(fixture, tenant.Id);
        var storage = new MemoryPrivateStorage();
        Assert.True((await SendAsync(fixture, storage, Token, Jpeg(40, 30)))!.IsSuccess);
        Assert.True((await SendAsync(fixture, storage, Token, Jpeg(40, 30)))!.IsSuccess);
        fixture.TenantService.SetTenant(tenant.Id);
        var accepted = await fixture.Context.Appointments.SingleAsync(a => a.Id == visit.Id);
        accepted.Accept(SalesTestFixture.Now);
        await fixture.Context.SaveChangesAsync();

        var quote = (await CreateAsync(fixture, new CreateQuoteCommand(visit.Id, null, null, null, null))).Value!;

        var paths = await fixture.Context.AppointmentProblemPhotos.OrderBy(p => p.Position).Select(p => "/files/" + p.FileKey).ToListAsync();
        Assert.Equal("Gotea el calentador", quote.Appointment!.Problem);
        Assert.Equal(2, paths.Count);
        Assert.Equal(paths, quote.Appointment.ProblemPhotos);

        // A visit the team arranged keeps the team's notes: they are not what the customer said.
        var zone = await fixture.Context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync();
        var arranged = new Appointment(tenant.Id, "Luis", "5512345678", "Visita", SalesTestFixture.Now.AddDays(5),
            SalesTestFixture.Now.AddDays(5).AddHours(1), notes: "Llevar escalera");
        arranged.ArrangeVisit(DeliveryAddress.ForVisit("72000", "Av. Reforma 5", null), zone, SalesTestFixture.Now);
        fixture.Context.Appointments.Add(arranged);
        await fixture.Context.SaveChangesAsync();
        var theirs = (await CreateAsync(fixture, new CreateQuoteCommand(arranged.Id, null, null, null, null))).Value!;
        Assert.Equal(((string?)null, 0), (theirs.Appointment!.Problem, theirs.Appointment.ProblemPhotos.Count));
    }

    [Fact]
    public async Task FilledWhileItWasRead_ShouldBeRefused_WithoutWritingItsFile()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var visit = await RequestedAsync(fixture, tenant.Id);
        var storage = new MemoryPrivateStorage();
        // While this photo is decoded, the other five arrive.
        var others = new FillsWhileDecoding(async () =>
        {
            for (var n = 0; n < HomeVisitRules.MaxProblemPhotos; n++)
                fixture.Context.AppointmentProblemPhotos.Add(visit.AddProblemPhoto(n, $"{OrderUploadRules.VisitProblemFolder(tenant.Id, SalesTestFixture.Now)}/{Guid.NewGuid()}.webp", SalesTestFixture.Now));
            await fixture.Context.SaveChangesAsync();
        });

        var refused = await new AddVisitProblemPhotoCommandHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, storage, others, new PublicPhotoGate())
            .Handle(new AddVisitProblemPhotoCommand { Token = Token, FileStream = Jpeg(40, 30), FileSize = 1 }, CancellationToken.None);

        Assert.Equal(HomeVisitRules.ProblemPhotosFullMessage, refused!.Error);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task TheAccountsAppointments_ShouldSayHowManyPhotos_InEveryBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var account = Guid.NewGuid();
        var zone = await fixture.Context.DeliveryZones.IgnoreQueryFilters().Include(z => z.PostalCodes).SingleAsync(z => z.TenantId == tenant.Id);
        var at = SalesTestFixture.Now.AddDays(3);
        var visit = Appointment.RequestHomeVisit(
            tenant.Id, "Valeria", "5522107765", null, HomeVisitRules.DefaultVisitName, at, at.AddHours(1), "Gotea el calentador", null,
            DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone, HomeVisitChargePolicy.Charged, account, Guid.NewGuid(), Token);
        fixture.Context.Appointments.Add(visit);
        await fixture.Context.SaveChangesAsync();
        Assert.True((await SendAsync(fixture, new MemoryPrivateStorage(), Token, Jpeg(40, 30)))!.IsSuccess);
        // The account reads its appointments of every business, with no business in context.
        fixture.TenantService.SetTenant(Guid.Empty);
        fixture.CurrentUser.UserId = account.ToString();

        var mine = await new GetMyAppointmentsQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyAppointmentsQuery(), CancellationToken.None);

        Assert.Equal((1, true), (Assert.Single(mine.Items).ProblemPhotos, mine.Items[0].CanAddPhotos));
    }

    [Fact]
    public void TheirFiles_ShouldOpenOnlyForWhoeverWorksTheAgendaOrQuotesTheVisit()
    {
        var permissions = typeof(GetVisitProblemPhotoFileQuery).GetCustomAttributes(typeof(Common.Security.RequiresPermissionAttribute), false)
            .Cast<Common.Security.RequiresPermissionAttribute>().Single();
        var capabilities = typeof(GetVisitProblemPhotoFileQuery).GetCustomAttributes(typeof(Common.Security.RequiresCapabilityAttribute), false)
            .Cast<Common.Security.RequiresCapabilityAttribute>().Single();

        Assert.Equal(
            new HashSet<Domain.Tenants.Enums.TenantPermission> { Domain.Tenants.Enums.TenantPermission.ManageAppointments, Domain.Tenants.Enums.TenantPermission.ManageOrders },
            permissions.AnyOf.ToHashSet());
        Assert.Equal(Domain.Tenants.Enums.TenantCapability.Appointments | Domain.Tenants.Enums.TenantCapability.Service, capabilities.AnyOf);
    }

    [Fact]
    public void Validator_ShouldTakeAPhotoUpTo10MB_WithItsLink()
    {
        var validator = new AddVisitProblemPhotoCommandValidator();
        AddVisitProblemPhotoCommand Command(string token, long size) => new() { Token = token, FileStream = new MemoryStream(), FileSize = size };

        Assert.True(validator.Validate(Command(Token, VisitProblemPhotos.MaxFileBytes)).IsValid);
        Assert.Equal(VisitProblemPhotos.TooLargeMessage, validator.Validate(Command(Token, VisitProblemPhotos.MaxFileBytes + 1)).Errors.Single().ErrorMessage);
        Assert.False(validator.Validate(Command(Token, 0)).IsValid);
        Assert.False(validator.Validate(Command("", 10)).IsValid);
        Assert.False(validator.Validate(Command(new string('v', 101), 10)).IsValid);
    }

    /// <summary>A visit requested online at 72000, «Por confirmar», three days ahead unless said.</summary>
    private static async Task<Appointment> RequestedAsync(SalesTestFixture fixture, Guid tenantId, string token = Token, DateTimeOffset? start = null)
    {
        var at = start ?? SalesTestFixture.Now.AddDays(3);
        var zone = await fixture.Context.DeliveryZones.IgnoreQueryFilters().Include(z => z.PostalCodes).SingleAsync(z => z.TenantId == tenantId);
        var visit = Appointment.RequestHomeVisit(
            tenantId, "Valeria", "5522107765", null, HomeVisitRules.DefaultVisitName, at, at.AddHours(1), "Gotea el calentador", null,
            DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone, HomeVisitChargePolicy.Charged, null, Guid.NewGuid(), token);
        fixture.Context.Appointments.Add(visit);
        await fixture.Context.SaveChangesAsync();
        return visit;
    }

    private static Task<Result<CustomerAppointmentDto>?> SendAsync(
        SalesTestFixture fixture, IPrivateFileStorage storage, string token, Stream photo, PublicPhotoGate? gate = null,
        IImageNormalizationService? images = null)
        => new AddVisitProblemPhotoCommandHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, storage, images ?? new ImageProcessingService(), gate ?? new PublicPhotoGate())
            .Handle(new AddVisitProblemPhotoCommand { Token = token, FileStream = photo, FileSize = photo.Length }, CancellationToken.None);

    private static Task<Result<PrivateFileDownload>> OpenAsync(SalesTestFixture fixture, IPrivateFileStorage storage, string key)
        => new GetVisitProblemPhotoFileQueryHandler(fixture.Context, fixture.TenantService, storage)
            .Handle(new GetVisitProblemPhotoFileQuery(key), CancellationToken.None);

    private static MemoryStream Jpeg(int width, int height, bool withLocation = false)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 120, 200));
        if (withLocation)
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Make, "Teléfono del cliente");
            image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        }
        var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>The real decoding, counting each photo it decodes.</summary>
    private sealed class CountingImages : IImageNormalizationService
    {
        private readonly ImageProcessingService _images = new();

        public int Decoded { get; private set; }

        public Task<Stream> NormalizeToWebpAsync(Stream imageStream, int maxDimension, CancellationToken cancellationToken = default)
        {
            Decoded++;
            return _images.NormalizeToWebpAsync(imageStream, maxDimension, cancellationToken);
        }
    }

    /// <summary>The visit fills up while the photo is decoded.</summary>
    private sealed class FillsWhileDecoding(Func<Task> meanwhile) : IImageNormalizationService
    {
        public async Task<Stream> NormalizeToWebpAsync(Stream imageStream, int maxDimension, CancellationToken cancellationToken = default)
        {
            await meanwhile();
            return new MemoryStream([1, 2, 3]);
        }
    }

    /// <summary>
    /// Other photos of the same visit saved first, between the count the handler read and its own save, which then fails as
    /// the base refuses a place taken twice; or a save that fails for something else.
    /// </summary>
    private sealed class PhotoSentMeanwhile : SaveChangesInterceptor
    {
        public int Armed { get; set; }
        public bool FailsAnyway { get; set; }
        /// <summary>How many saves carried a photo.</summary>
        public int Saves { get; set; }
        public ICurrentTenantService? Tenant { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var sending = eventData.Context!.ChangeTracker.Entries<AppointmentProblemPhoto>()
                .FirstOrDefault(entry => entry.State == EntityState.Added)?.Entity;
            if (sending is null)
                return result;
            Saves++;
            if (FailsAnyway)
                throw new DbUpdateException("The base did not answer.");
            if (Armed == 0)
                return result;

            var options = (DbContextOptions<ApplicationDbContext>)eventData.Context.GetService<IDbContextOptions>();
            await using var other = new SalesDbContext(options, Tenant!);
            var visit = await other.Appointments.AsNoTracking().SingleAsync(a => a.Id == sending.AppointmentId, cancellationToken);
            var saved = await other.AppointmentProblemPhotos.CountAsync(p => p.AppointmentId == visit.Id, cancellationToken);
            for (var n = 0; n < Armed; n++)
                other.AppointmentProblemPhotos.Add(visit.AddProblemPhoto(saved + n, $"{OrderUploadRules.VisitProblemFolder(visit.TenantId, SalesTestFixture.Now)}/{Guid.NewGuid()}.webp", SalesTestFixture.Now));
            Armed = 0;
            await other.SaveChangesAsync(cancellationToken);
            throw new DbUpdateException("The place was taken meanwhile.");
        }
    }
}

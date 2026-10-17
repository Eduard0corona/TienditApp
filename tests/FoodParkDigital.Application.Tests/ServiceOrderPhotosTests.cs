using System.Text.Json;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The photos of a service (Fase 25, ADR 0024): only files the business uploaded, kept as their private path, with who
/// added them and when; removed only while the order is open, by whoever took them or a manager; and shown with the
/// ones taken when the order was received.
/// </summary>
public class ServiceOrderPhotosTests
{
    private static readonly Guid OtherTenant = Guid.NewGuid();

    [Fact]
    public async Task Add_ShouldKeepTheBusinessesOwnFile_WithWhoAndWhen()
    {
        await using var fixture = await ServiceFixtureAsync();
        var order = await ServiceOrderAsync(fixture);
        var key = PhotoKey(fixture.TenantService.TenantId);

        var result = await AddAsync(fixture, order.Id, $"/files/{key}", ServicePhotoStage.After, note: "  Sin la mancha  ", fileName: "saco.jpg");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal((ServicePhotoStage.After, $"/files/{key}", "saco.jpg", "Sin la mancha", false, (DateTimeOffset?)fixture.Clock.UtcNow, "cajero", true),
            (result.Value.Stage, result.Value.Url, result.Value.FileName, result.Value.Note, result.Value.AtIntake, result.Value.TakenAt,
                result.Value.TakenBy, result.Value.CanRemove));
        var stored = await StoredPhotoAsync(fixture, order.Id);
        Assert.Equal((result.Value.Id, $"/files/{key}", (Guid?)Guid.Parse(fixture.CurrentUser.UserId!), fixture.TenantService.TenantId),
            ((Guid?)stored.Id, stored.FileUrl, stored.TakenByUserId, stored.TenantId));
    }

    [Fact]
    public async Task Add_ShouldKeepTheFileOfAnOlderAddress_AsItsPrivatePath()
    {
        await using var fixture = await ServiceFixtureAsync();
        var order = await ServiceOrderAsync(fixture);
        var key = PhotoKey(fixture.TenantService.TenantId);

        var result = await AddAsync(fixture, order.Id, $"https://cuenta.blob.core.windows.net/uploads/{key}");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal($"/files/{key}", (await StoredPhotoAsync(fixture, order.Id)).FileUrl);
    }

    [Theory]
    [InlineData("https://tracker.example/pixel.png")]
    [InlineData("/files/orders/{other}/service-intake/202610/{file}.webp")]
    [InlineData("/files/orders/{tenant}/payment-evidence/202610/{file}.webp")]
    [InlineData("/files/orders/{tenant}/service-intake/202610/../../{file}.webp")]
    public async Task Add_ShouldRefuseAnyOtherFile(string template)
    {
        await using var fixture = await ServiceFixtureAsync();
        var order = await ServiceOrderAsync(fixture);
        var url = template
            .Replace("{tenant}", fixture.TenantService.TenantId.ToString())
            .Replace("{other}", OtherTenant.ToString())
            .Replace("{file}", Guid.NewGuid().ToString());

        var result = await AddAsync(fixture, order.Id, url);

        Assert.Equal(OrderUploadRules.ServicePhotoError, result.Error);
        Assert.Empty(await fixture.Context.Set<ServiceOrderPhoto>().ToListAsync());
    }

    [Fact]
    public async Task Add_ShouldSayWhyTheOrderTakesNoPhoto()
    {
        await using var fixture = await ServiceFixtureAsync();
        var cancelled = await ServiceOrderAsync(fixture, order => order.Cancel(new ServiceOrderStateMachine(), "admin", "No regresó"));
        var sale = new Order(fixture.TenantService.TenantId, null, null, true, "ORD-000009", BusinessType.Service, OrderType.Retail,
            "Mostrador", null, null, null);
        sale.AddItem(Guid.NewGuid(), "Cepillo", 1, 50m);
        fixture.Context.Orders.Add(sale);
        await fixture.Context.SaveChangesAsync();
        var file = $"/files/{PhotoKey(fixture.TenantService.TenantId)}";

        Assert.Equal(Order.PhotosOfCancelledOrderMessage, (await AddAsync(fixture, cancelled.Id, file)).Error);
        Assert.Equal(Order.PhotosOnlyForServicesMessage, (await AddAsync(fixture, sale.Id, file)).Error);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => AddAsync(fixture, Guid.NewGuid(), file));
        Assert.Empty(await fixture.Context.Set<ServiceOrderPhoto>().ToListAsync());
    }

    [Fact]
    public async Task Remove_ShouldBeForWhoTookIt_OrAManager()
    {
        await using var fixture = await ServiceFixtureAsync();
        var order = await ServiceOrderAsync(fixture);
        var taken = (await AddAsync(fixture, order.Id, $"/files/{PhotoKey(fixture.TenantService.TenantId)}")).Value.Id!.Value;
        var other = (await AddAsync(fixture, order.Id, $"/files/{PhotoKey(fixture.TenantService.TenantId)}")).Value.Id!.Value;

        // Another employee of the business sees it, and cannot remove it.
        AsStaff(fixture, Guid.NewGuid());
        var photos = await PhotosAsync(fixture, order.Id);
        Assert.All(photos.Photos, photo => Assert.False(photo.CanRemove));
        Assert.Equal(RemoveServiceOrderPhotoCommandHandler.NotYoursMessage, (await RemoveAsync(fixture, order.Id, taken)).Error);

        // Whoever took it can, and so can a manager.
        var author = (await StoredPhotoAsync(fixture, order.Id, taken)).TakenByUserId!.Value;
        AsStaff(fixture, author);
        Assert.True((await RemoveAsync(fixture, order.Id, taken)).IsSuccess);
        fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        fixture.CurrentUser.TenantRole = nameof(TenantUserRole.Admin);
        Assert.True((await RemoveAsync(fixture, order.Id, other)).IsSuccess);

        Assert.Empty(await fixture.Context.Set<ServiceOrderPhoto>().ToListAsync());
        Assert.Equal(Order.PhotoNotFoundMessage, (await RemoveAsync(fixture, order.Id, other)).Error);
    }

    [Fact]
    public async Task Remove_ShouldKeepThePhotosOfAFinishedOrder_EvenForAManager()
    {
        await using var fixture = await ServiceFixtureAsync();
        var order = await ServiceOrderAsync(fixture);
        var photo = (await AddAsync(fixture, order.Id, $"/files/{PhotoKey(fixture.TenantService.TenantId)}", ServicePhotoStage.After))
            .Value.Id!.Value;
        var tracked = await fixture.Context.Orders.SingleAsync(o => o.Id == order.Id);
        tracked.TransitionTo(OrderStatus.Completed, new ServiceOrderStateMachine(), "mostrador");
        await fixture.Context.SaveChangesAsync();

        var result = await RemoveAsync(fixture, order.Id, photo);
        var photos = await PhotosAsync(fixture, order.Id);

        Assert.Equal(Order.PhotosOfFinishedOrderMessage, result.Error);
        Assert.False(Assert.Single(photos.Photos).CanRemove);
        // A finished order still takes the photo of the delivery.
        Assert.True(photos.CanAdd);
        Assert.Single(await fixture.Context.Set<ServiceOrderPhoto>().ToListAsync());
    }

    [Fact]
    public async Task Photos_ShouldShowTheOnesOfTheIntakeFirst_ThenTheAddedOnesInTheOrderTheyWereTaken()
    {
        await using var fixture = await ServiceFixtureAsync();
        var tenant = fixture.TenantService.TenantId;
        var before = PhotoKey(tenant);
        var after = PhotoKey(tenant);
        var order = await ServiceOrderAsync(fixture, beforePhotosJson: JsonSerializer.Serialize(new object[]
        {
            new { url = $"/files/{before}", fileName = "frente.jpg" },
            // Written before the rules: not one of the business's files, so it is left out.
            new { url = "https://tracker.example/pixel.png" }
        }), afterPhotosJson: JsonSerializer.Serialize(new[] { $"/files/{after}" }));
        var later = $"/files/{PhotoKey(tenant)}";
        var first = $"/files/{PhotoKey(tenant)}";
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(2);
        await AddAsync(fixture, order.Id, later, ServicePhotoStage.After, note: "Al entregar");
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(1);
        await AddAsync(fixture, order.Id, first, ServicePhotoStage.Before);

        var photos = await PhotosAsync(fixture, order.Id);

        Assert.Equal(
        [
            (ServicePhotoStage.Before, $"/files/{before}", "frente.jpg", true, false),
            (ServicePhotoStage.After, $"/files/{after}", (string?)null, true, false),
            (ServicePhotoStage.Before, first, null, false, true),
            (ServicePhotoStage.After, later, null, false, true)
        ], photos.Photos.Select(p => (p.Stage, p.Url, p.FileName, p.AtIntake, p.CanRemove)).ToList());
        Assert.All(photos.Photos.Where(p => p.AtIntake), p => Assert.Equal(((Guid?)null, (DateTimeOffset?)null), (p.Id, p.TakenAt)));
        Assert.True(photos.CanAdd);
    }

    [Fact]
    public async Task Photos_OfACancelledOrder_ShouldShowButTakeNoMore_AndAStoreOrderHasNone()
    {
        await using var fixture = await ServiceFixtureAsync();
        var order = await ServiceOrderAsync(fixture);
        await AddAsync(fixture, order.Id, $"/files/{PhotoKey(fixture.TenantService.TenantId)}");
        var tracked = await fixture.Context.Orders.SingleAsync(o => o.Id == order.Id);
        tracked.Cancel(new ServiceOrderStateMachine(), "admin", "No regresó");
        await fixture.Context.SaveChangesAsync();
        var sale = new Order(fixture.TenantService.TenantId, null, null, true, "ORD-000009", BusinessType.Service, OrderType.Retail,
            "Mostrador", null, null, null);
        sale.AddItem(Guid.NewGuid(), "Cepillo", 1, 50m);
        fixture.Context.Orders.Add(sale);
        await fixture.Context.SaveChangesAsync();

        var photos = await PhotosAsync(fixture, order.Id);
        var ofASale = await new GetServiceOrderPhotosQueryHandler(fixture.Context, fixture.CurrentUser, Access(fixture))
            .Handle(new GetServiceOrderPhotosQuery(sale.Id), CancellationToken.None);

        Assert.False(photos.CanAdd);
        Assert.False(Assert.Single(photos.Photos).CanRemove);
        Assert.Equal(Order.PhotosOnlyForServicesMessage, ofASale.Error);
    }

    [Fact]
    public void Validator_ShouldTakeANoteUpToItsLength_AndAKnownStage()
    {
        var validator = new AddServiceOrderPhotoCommandValidator();
        var valid = new AddServiceOrderPhotoCommand
        {
            OrderId = Guid.NewGuid(),
            Stage = ServicePhotoStage.Before,
            FileUrl = "/files/orders/x/service-intake/202610/a.webp",
            Note = new string('a', ServiceOrderPhoto.MaxNoteLength)
        };

        Assert.True(validator.Validate(valid).IsValid);
        Assert.Equal(Order.NoteTooLongMessage,
            Assert.Single(validator.Validate(valid with { Note = new string('a', ServiceOrderPhoto.MaxNoteLength + 1) }).Errors).ErrorMessage);
        Assert.Equal("La etapa de la foto no es válida.",
            Assert.Single(validator.Validate(valid with { Stage = (ServicePhotoStage)5 }).Errors).ErrorMessage);
        Assert.Equal("Falta la foto.", Assert.Single(validator.Validate(valid with { FileUrl = "" }).Errors).ErrorMessage);
    }

    private static async Task<SalesTestFixture> ServiceFixtureAsync()
    {
        var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service);
        return fixture;
    }

    private static async Task<Order> ServiceOrderAsync(
        SalesTestFixture fixture,
        Action<Order>? change = null,
        string? beforePhotosJson = null,
        string? afterPhotosJson = null)
    {
        var order = new Order(fixture.TenantService.TenantId, null, null, true, $"S-{Guid.NewGuid():N}"[..10], BusinessType.Service,
            OrderType.Service, "Valeria", "+525522107765", null, null);
        order.AddItem(Guid.NewGuid(), "Lavado de saco", 1, 180m);
        order.SetServiceDetail(SalesTestFixture.Now, null, 3, null, null, null, beforePhotosJson, afterPhotosJson);
        change?.Invoke(order);
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return order;
    }

    private static Task<Result<ServiceOrderPhotoDto>> AddAsync(
        SalesTestFixture fixture,
        Guid orderId,
        string fileUrl,
        ServicePhotoStage stage = ServicePhotoStage.Before,
        string? note = null,
        string? fileName = null)
        => new AddServiceOrderPhotoCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, Access(fixture), fixture.Clock)
            .Handle(new AddServiceOrderPhotoCommand { OrderId = orderId, Stage = stage, FileUrl = fileUrl, FileName = fileName, Note = note },
                CancellationToken.None);

    private static Task<Result> RemoveAsync(SalesTestFixture fixture, Guid orderId, Guid photoId)
        => new RemoveServiceOrderPhotoCommandHandler(fixture.Context, fixture.CurrentUser, Access(fixture), fixture.Clock)
            .Handle(new RemoveServiceOrderPhotoCommand(orderId, photoId), CancellationToken.None);

    private static async Task<ServiceOrderPhotosDto> PhotosAsync(SalesTestFixture fixture, Guid orderId)
    {
        var result = await new GetServiceOrderPhotosQueryHandler(fixture.Context, fixture.CurrentUser, Access(fixture))
            .Handle(new GetServiceOrderPhotosQuery(orderId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static Task<ServiceOrderPhoto> StoredPhotoAsync(SalesTestFixture fixture, Guid orderId, Guid? photoId = null)
        => fixture.Context.Set<ServiceOrderPhoto>().AsNoTracking().SingleAsync(p => p.OrderId == orderId && (photoId == null || p.Id == photoId));

    /// <summary>An employee of the business who is not a manager.</summary>
    private static void AsStaff(SalesTestFixture fixture, Guid userId)
    {
        fixture.CurrentUser.UserId = userId.ToString();
        fixture.CurrentUser.TenantRole = nameof(TenantUserRole.Staff);
    }

    private static CurrentAccessContext Access(SalesTestFixture fixture)
        => TestAccess.For(fixture.CurrentUser, fixture.TenantService);

    private static string PhotoKey(Guid tenantId) => $"orders/{tenantId}/service-intake/202610/{Guid.NewGuid()}.webp";
}

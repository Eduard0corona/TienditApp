using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The photos of a service (Fase 25, ADR 0024): before or after, with an optional note, who took it and when; up to
/// twenty of each, each file once; none for a cancelled order and only after for a finished one; and, once the order is
/// delivered, finished or cancelled, they stay.
/// </summary>
public class ServiceOrderPhotoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);
    private const string File = "/files/orders/tenant/service-intake/202610/foto.webp";

    /// <summary>Another file each time: the same file goes once on an order.</summary>
    private static string NewFile() => $"/files/orders/tenant/service-intake/202610/{Guid.NewGuid()}.webp";

    [Fact]
    public void AddServicePhoto_ShouldKeepTheStage_TheNote_WhoAndWhen_AndTouchTheOrder()
    {
        var order = NewServiceOrder();
        order.ClearDomainEvents();

        var author = Guid.NewGuid();

        var photo = order.AddServicePhoto(ServicePhotoStage.After, File, "  saco.jpg ", "  Sin la mancha de la manga  ", Now, " Ana ", author);

        Assert.Same(photo, Assert.Single(order.ServicePhotos));
        Assert.Equal((order.TenantId, order.Id, ServicePhotoStage.After, File, "saco.jpg", "Sin la mancha de la manga", Now, "Ana", (Guid?)author),
            (photo.TenantId, photo.OrderId, photo.Stage, photo.FileUrl, photo.FileName, photo.Note, photo.TakenAt, photo.TakenBy, photo.TakenByUserId));
        // The order row changes with its photos, so two added at once check its row version and the limit holds.
        Assert.Equal(Now, order.UpdatedAt);
        var added = Assert.IsType<ServiceOrderPhotoAddedEvent>(Assert.Single(order.DomainEvents));
        Assert.Equal((order.Id, order.TenantId, photo.Id, ServicePhotoStage.After, File),
            (added.OrderId, added.TenantId, added.PhotoId, added.Stage, added.FileUrl));
    }

    [Fact]
    public void AddServicePhoto_ShouldStoreBlanksAsNone_AndCutLongNamesAndAuthors()
    {
        var order = NewServiceOrder();

        var blank = order.AddServicePhoto(ServicePhotoStage.Before, NewFile(), "   ", "   ", Now, "  ");
        var longOne = order.AddServicePhoto(ServicePhotoStage.Before, NewFile(), new string('n', 300), null, Now, new string('a', 300));

        Assert.Equal(((string?)null, (string?)null, (string?)null, (Guid?)null), (blank.FileName, blank.Note, blank.TakenBy, blank.TakenByUserId));
        Assert.Equal(ServiceOrderPhoto.MaxFileNameLength, longOne.FileName!.Length);
        Assert.Equal(ServiceOrderPhoto.MaxTakenByLength, longOne.TakenBy!.Length);
    }

    [Fact]
    public void AddServicePhoto_ShouldTakeANoteUpToItsLength()
    {
        var order = NewServiceOrder();

        order.AddServicePhoto(ServicePhotoStage.Before, NewFile(), null, new string('a', ServiceOrderPhoto.MaxNoteLength), Now, null);
        var error = Assert.Throws<DomainException>(() =>
            order.AddServicePhoto(ServicePhotoStage.Before, NewFile(), null, new string('a', ServiceOrderPhoto.MaxNoteLength + 1), Now, null));

        Assert.Equal(Order.NoteTooLongMessage, error.Message);
        Assert.Single(order.ServicePhotos);
    }

    [Fact]
    public void AddServicePhoto_ShouldTakeUpToTwentyOfEachStage()
    {
        var order = NewServiceOrder();
        for (var i = 0; i < ServiceOrderPhoto.MaxPerStage; i++)
            order.AddServicePhoto(ServicePhotoStage.Before, NewFile(), null, null, Now, null);

        var error = Assert.Throws<DomainException>(() => order.AddServicePhoto(ServicePhotoStage.Before, NewFile(), null, null, Now, null));
        // The after has its own twenty.
        order.AddServicePhoto(ServicePhotoStage.After, NewFile(), null, null, Now, null);

        Assert.Equal("Una orden lleva hasta 20 fotos del antes.", error.Message);
        Assert.Equal("Una orden lleva hasta 20 fotos del después.", Order.TooManyPhotosMessage(ServicePhotoStage.After));
        Assert.Equal(ServiceOrderPhoto.MaxPerStage + 1, order.ServicePhotos.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddServicePhoto_ShouldNeedAFile(string fileUrl)
    {
        var order = NewServiceOrder();

        Assert.Equal("La foto no es válida.",
            Assert.Throws<DomainException>(() => order.AddServicePhoto(ServicePhotoStage.Before, fileUrl, null, null, Now, null)).Message);
        Assert.Equal("La foto no es válida.", Assert.Throws<DomainException>(() =>
            order.AddServicePhoto(ServicePhotoStage.Before, "/files/" + new string('a', ServiceOrderPhoto.MaxFileUrlLength), null, null, Now, null)).Message);
        Assert.Empty(order.ServicePhotos);
    }

    [Fact]
    public void AddServicePhoto_ShouldTakeEachFileOnce()
    {
        var order = NewServiceOrder();
        order.AddServicePhoto(ServicePhotoStage.Before, File, null, null, Now, null);

        var error = Assert.Throws<DomainException>(() => order.AddServicePhoto(ServicePhotoStage.After, File, null, "Otra vez", Now, null));

        Assert.Equal(Order.PhotoAlreadyAddedMessage, error.Message);
        Assert.Single(order.ServicePhotos);
    }

    [Fact]
    public void AddServicePhoto_ShouldRefuseAStageThatDoesNotExist()
    {
        var order = NewServiceOrder();

        Assert.Equal("La etapa de la foto no es válida.",
            Assert.Throws<DomainException>(() => order.AddServicePhoto((ServicePhotoStage)2, File, null, null, Now, null)).Message);
    }

    [Fact]
    public void AddServicePhoto_ShouldBeOnlyForAServiceOrder()
    {
        var order = NewOrder(BusinessType.Retail, OrderType.Retail);

        var error = Assert.Throws<DomainException>(() => order.AddServicePhoto(ServicePhotoStage.Before, File, null, null, Now, null));

        Assert.Equal(Order.PhotosOnlyForServicesMessage, error.Message);
        Assert.Empty(order.ServicePhotos);
    }

    [Fact]
    public void AddServicePhoto_ShouldRefuseACancelledOrder_ButTakeTheOneOfTheDelivery()
    {
        var cancelled = NewServiceOrder();
        cancelled.Cancel(new ServiceOrderStateMachine(), "admin", "No regresó");
        var finished = NewServiceOrder();
        finished.TransitionTo(OrderStatus.Completed, new ServiceOrderStateMachine(), "mostrador");

        var error = Assert.Throws<DomainException>(() => cancelled.AddServicePhoto(ServicePhotoStage.After, File, null, null, Now, null));
        finished.AddServicePhoto(ServicePhotoStage.After, File, null, "Al entregar", Now, "Ana");

        Assert.Equal(Order.PhotosOfCancelledOrderMessage, error.Message);
        Assert.Single(finished.ServicePhotos);
        Assert.False(cancelled.TakesServicePhotos(ServicePhotoStage.After));
    }

    [Fact]
    public void AddServicePhoto_OfAFinishedOrder_ShouldTakeOnlyAfter_SinceItCouldNotBeRemoved()
    {
        var finished = NewServiceOrder();
        finished.TransitionTo(OrderStatus.Completed, new ServiceOrderStateMachine(), "mostrador");
        var open = NewServiceOrder();

        var error = Assert.Throws<DomainException>(() => finished.AddServicePhoto(ServicePhotoStage.Before, File, null, "Daño", Now, "Ana"));

        Assert.Equal(Order.OnlyAfterPhotosMessage, error.Message);
        Assert.Empty(finished.ServicePhotos);
        Assert.Equal((false, true), (finished.TakesServicePhotos(ServicePhotoStage.Before), finished.TakesServicePhotos(ServicePhotoStage.After)));
        Assert.Equal((true, true), (open.TakesServicePhotos(ServicePhotoStage.Before), open.TakesServicePhotos(ServicePhotoStage.After)));
        Assert.False(NewOrder(BusinessType.Retail, OrderType.Retail).TakesServicePhotos(ServicePhotoStage.After));
    }

    [Fact]
    public void RemoveServicePhoto_ShouldRemoveIt_WhileTheOrderIsOpen_AndSayWhatWasRemoved()
    {
        var order = NewServiceOrder();
        var kept = order.AddServicePhoto(ServicePhotoStage.Before, File, "antes.jpg", null, Now, "Ana");
        var mistakeFile = NewFile();
        var mistake = order.AddServicePhoto(ServicePhotoStage.After, mistakeFile, "otra.jpg", "Equivocada", Now, "Luis");
        order.ClearDomainEvents();
        var later = Now.AddMinutes(5);

        Assert.True(order.ServicePhotosRemovable);
        order.RemoveServicePhoto(mistake.Id, later);

        Assert.Same(kept, Assert.Single(order.ServicePhotos));
        Assert.Equal(later, order.UpdatedAt);
        var removed = Assert.IsType<ServiceOrderPhotoRemovedEvent>(Assert.Single(order.DomainEvents));
        Assert.Equal((order.Id, order.TenantId, mistake.Id, ServicePhotoStage.After, mistakeFile, Now, "Luis"),
            (removed.OrderId, removed.TenantId, removed.PhotoId, removed.Stage, removed.FileUrl, removed.TakenAt, removed.TakenBy));
    }

    [Fact]
    public void RemoveServicePhoto_ShouldSayWhenThePhotoIsNotThere()
    {
        var order = NewServiceOrder();
        order.AddServicePhoto(ServicePhotoStage.Before, File, null, null, Now, null);

        var error = Assert.Throws<DomainException>(() => order.RemoveServicePhoto(Guid.NewGuid(), Now));

        Assert.Equal(Order.PhotoNotFoundMessage, error.Message);
        Assert.Single(order.ServicePhotos);
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void RemoveServicePhoto_ShouldKeepThePhotosOfAFinishedOrCancelledOrder(OrderStatus status)
    {
        var order = NewServiceOrder();
        var photo = order.AddServicePhoto(ServicePhotoStage.After, File, null, null, Now, null);
        if (status == OrderStatus.Cancelled)
            order.Cancel(new ServiceOrderStateMachine(), "admin", "No regresó");
        else
            order.TransitionTo(status, new ServiceOrderStateMachine(), "mostrador");
        order.ClearDomainEvents();

        var error = Assert.Throws<DomainException>(() => order.RemoveServicePhoto(photo.Id, Now.AddMinutes(5)));

        Assert.False(order.ServicePhotosRemovable);
        Assert.Equal(Order.PhotosOfFinishedOrderMessage, error.Message);
        Assert.Single(order.ServicePhotos);
        Assert.Empty(order.DomainEvents);
    }

    private static Order NewServiceOrder() => NewOrder(BusinessType.Service, OrderType.Service);

    private static Order NewOrder(BusinessType businessType, OrderType orderType)
    {
        var order = new Order(
            tenantId: Guid.NewGuid(),
            customerUserId: null,
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: businessType,
            orderType: orderType,
            customerName: "Cliente",
            customerPhone: "+525511112222",
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Lavado", 1, 100m);
        return order;
    }
}

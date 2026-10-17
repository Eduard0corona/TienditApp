using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Online bookings on SQL Server (Fase 24D, ADR 0025): requests that arrive at the same time for the last free time of a
/// business wait in line on its booking lock, so only one takes it; and the same request sent twice at once is one
/// appointment.
/// </summary>
public class OnlineBookingSqlServerTests
{
    // Sunday 4 October 2026, 11:00 in Mexico City; the business attends Monday to Friday, 9 to 14.
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MondayAtTen = new(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(-6));

    [SqlServerFact]
    public async Task RequestsAtOnce_ForTheLastFreeTime_ShouldLeaveItToOne()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var business = await SeedAsync(database);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            RequestAsync(database, business, $"55221077{i:00}", Guid.NewGuid())));

        Assert.Single(results, result => result.IsSuccess);
        Assert.All(results.Where(result => result.IsFailure),
            result => Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, result.Error));
        await using var check = database.CreateContext(business.TenantId);
        Assert.Equal(1, await check.Appointments.CountAsync(a => a.Status == AppointmentStatus.Requested));
    }

    [SqlServerFact]
    public async Task SameRequestTwiceAtOnce_ShouldBeOneAppointment_AnsweredToBoth()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var business = await SeedAsync(database);
        var key = Guid.NewGuid();

        var results = await Task.WhenAll(
            RequestAsync(database, business, "5522107765", key),
            RequestAsync(database, business, "5522107765", key));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error));
        Assert.Equal(results[0].Value!.TrackingToken, results[1].Value!.TrackingToken);
        await using var check = database.CreateContext(business.TenantId);
        Assert.Equal(1, await check.Appointments.CountAsync());
    }

    /// <summary>Asks for Monday at 10:00 through the transaction of the pipeline, with the services of production.</summary>
    private static async Task<Result<PublicAppointmentReceiptDto>> RequestAsync(
        SqlServerTestDatabase database, Business business, string phone, Guid key)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        await using var context = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        var clock = new Clock();
        var command = new RequestPublicAppointmentCommand
        {
            Slug = business.Slug,
            ServiceProductId = business.ServiceId,
            StartAt = MondayAtTen,
            CustomerName = "Valeria Ruiz",
            CustomerPhone = phone,
            IdempotencyKey = key
        };
        var handler = new RequestPublicAppointmentCommandHandler(
            context, tenant, new Anonymous(), new PhoneNormalizationService(), clock, new AppointmentBookingLock(context),
            new AppointmentNotices(context, new PhoneNormalizationService(), clock));

        return await new TransactionBehavior<RequestPublicAppointmentCommand, Result<PublicAppointmentReceiptDto>>(context)
            .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
    }

    private static async Task<Business> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Barbería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetOpeningHours(OpeningHours.Create(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                .Select(day => new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(14, 0)))));
        settings.UpdateOnlineBooking(true, 1);
        var category = new Category(tenant.Id, "Servicios", null);
        var haircut = new Product(tenant.Id, category.Id, "Corte de cabello", null, 150m, "MXN", preparationTimeMinutes: 60, unit: UnitType.Service);

        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, Branch.CreateDefault(tenant.Id), category, haircut);
        await seed.SaveChangesAsync();
        return new Business(tenant.Id, slug, haircut.Id);
    }

    private sealed record Business(Guid TenantId, string Slug, Guid ServiceId);

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }

    /// <summary>A visitor without a session.</summary>
    private sealed class Anonymous : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => null;
        public string? Role => null;
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }
}

using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Visits to quote requested online on SQL Server (Fase 29B, ADR 0053): requests that arrive at once for the same time of
/// a business wait on its booking lock, so only one takes it and the next visit leaves the trip free, without one business
/// touching another's times; the visit keeps how it is charged, and the database keeps a visit «Sin costo» at zero.
/// </summary>
public class HomeVisitBookingSqlServerTests
{
    // Sunday 4 October 2026, 11:00 in Mexico City; the trades attend Monday to Friday, 9 to 14, with 30 minutes of trip.
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MondayAtTen = new(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(-6));

    [SqlServerFact]
    public async Task VisitsAtOnce_ForTheSameTime_ShouldLeaveItToOne_InEachBusiness_AndTheNextOneTheTrip()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var plumber = await SeedAsync(database);
        var electrician = await SeedAsync(database);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            RequestAsync(database, i % 2 == 0 ? plumber : electrician, $"55221077{i:00}", Guid.NewGuid())));
        var noTrip = await RequestAsync(database, plumber, "5522108800", Guid.NewGuid(), MondayAtTen.AddHours(1));
        var withTrip = await RequestAsync(database, plumber, "5522108800", Guid.NewGuid(), MondayAtTen.AddMinutes(90));

        Assert.Single(results.Where((_, i) => i % 2 == 0), result => result.IsSuccess);
        Assert.Single(results.Where((_, i) => i % 2 == 1), result => result.IsSuccess);
        Assert.All(results.Where(result => result.IsFailure),
            result => Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, result.Error));
        Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, noTrip.Error);
        Assert.True(withTrip.IsSuccess, withTrip.Error);
        await using var check = database.CreateContext(plumber.TenantId);
        Assert.Equal(2, await check.Appointments.CountAsync(a => a.Status == AppointmentStatus.Requested && a.Visit != null));
    }

    [SqlServerFact]
    public async Task AVisitSinCosto_ShouldBeStoredAtZero_AndTheDatabaseShouldKeepItThere()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var business = await SeedAsync(database, HomeVisitChargePolicy.Free);

        var result = await RequestAsync(database, business, "5522107765", Guid.NewGuid(), email: "valeria@correo.mx");

        Assert.True(result.IsSuccess, result.Error);
        await using var check = database.CreateContext(business.TenantId);
        var stored = await check.Appointments.SingleAsync();
        Assert.Equal((0m, (HomeVisitChargePolicy?)HomeVisitChargePolicy.Free, "valeria@correo.mx"),
            (stored.Visit!.Fee, stored.Visit.ChargePolicy, stored.CustomerEmail));
        var error = await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlRawAsync(
            "UPDATE [AppointmentVisits] SET [Fee] = 50 WHERE [AppointmentId] = {0}", stored.Id));
        Assert.Contains("CK_AppointmentVisits_ChargePolicy", error.Message);
    }

    /// <summary>
    /// Requests a visit (Monday at 10:00 unless another) through the transaction of the pipeline, with the services of
    /// production.
    /// </summary>
    private static async Task<Result<PublicAppointmentReceiptDto>> RequestAsync(
        SqlServerTestDatabase database, Business business, string phone, Guid key, DateTimeOffset? startAt = null, string? email = null)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        await using var context = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        var clock = new Clock();
        var command = new RequestPublicHomeVisitCommand
        {
            Slug = business.Slug,
            StartAt = startAt ?? MondayAtTen,
            Address = new AppointmentVisitInput { PostalCode = "72000", AddressLine = "Av. Juárez 123, Col. Centro" },
            Problem = "Gotea el calentador",
            CustomerName = "Valeria Ruiz",
            CustomerPhone = phone,
            CustomerEmail = email,
            IdempotencyKey = key
        };
        var handler = new RequestPublicHomeVisitCommandHandler(
            context, tenant, new Anonymous(), new PhoneNormalizationService(), clock, new AppointmentBookingLock(context),
            new AppointmentNotices(context, new PhoneNormalizationService(), clock));

        return await new TransactionBehavior<RequestPublicHomeVisitCommand, Result<PublicAppointmentReceiptDto>>(context)
            .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
    }

    /// <summary>A plumber that receives visits to 72000, one at a time, without a visit service (an hour that costs the zone).</summary>
    private static async Task<Business> SeedAsync(SqlServerTestDatabase database, HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetOpeningHours(OpeningHours.Create(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                .Select(day => new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(14, 0)))));
        settings.UpdateHomeVisitRequests(true, null, policy);
        var branch = Branch.CreateDefault(tenant.Id);
        branch.Update(branch.Name, branch.Code, new BranchContact(), BranchServiceMode.DeliveryOnly);
        var zone = new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 50m, 0m, null, ["72000"]));

        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, branch, zone);
        await seed.SaveChangesAsync();
        return new Business(tenant.Id, slug);
    }

    private sealed record Business(Guid TenantId, string Slug);

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

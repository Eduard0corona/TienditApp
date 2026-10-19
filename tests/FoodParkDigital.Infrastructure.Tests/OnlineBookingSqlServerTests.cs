using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
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
/// business wait in line on its booking lock, so only one takes it; the same request sent twice at once is one
/// appointment; a phone at once does not pass its requests waiting; and while the business says who attends (Fase 24J,
/// ADR 0032), a person gets one appointment at a time however many ask for them, or for whoever is free, at once.
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
    public async Task RequestsAtOnce_ForAPerson_OrWhoeverIsFree_ShouldGiveEachPersonOneAppointment()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var business = await SeedAsync(database, people: ["Ana", "Luis"]);
        var ana = business.People[0];

        // Five customers ask for Ana at 10:00 at once: one gets her.
        var withAna = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            RequestAsync(database, business, $"55221077{i:00}", Guid.NewGuid(), staffId: ana)));
        // Five more at once with no preference: only Luis is free, and one gets him.
        var anyone = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            RequestAsync(database, business, $"55221088{i:00}", Guid.NewGuid())));

        Assert.Single(withAna, result => result.IsSuccess);
        Assert.Single(anyone, result => result.IsSuccess);
        Assert.All(withAna.Concat(anyone).Where(result => result.IsFailure),
            result => Assert.Equal(RequestPublicAppointmentCommandHandler.TimeTakenMessage, result.Error));
        await using var check = database.CreateContext(business.TenantId);
        var attending = await check.Appointments.Select(a => a.StaffUserId).ToListAsync();
        Assert.Equal(2, attending.Count);
        Assert.Equal(2, attending.Distinct().Count());
        Assert.DoesNotContain(null, attending);
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

    [SqlServerFact]
    public async Task RequestsAtOnce_OfOnePhone_ShouldNotPassItsRequestsWaiting()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var business = await SeedAsync(database);
        await RequestAsync(database, business, "5522107765", Guid.NewGuid(), MondayAtTen.AddHours(24));
        await RequestAsync(database, business, "5522107765", Guid.NewGuid(), MondayAtTen.AddHours(25));
        Result<PublicAppointmentReceiptDto>[] results;

        // Two waiting already: three more at once, at three free times, all count two before the lock (another booking of
        // the business holds it meanwhile) and wait their turn; only one fits under the limit of three.
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        await using var holder = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        await using (var held = await holder.Database.BeginTransactionAsync())
        {
            await new AppointmentBookingLock(holder).AcquireAsync(business.TenantId, CancellationToken.None);
            var requests = Enumerable.Range(0, 3)
                .Select(i => RequestAsync(database, business, "5522107765", Guid.NewGuid(), MondayAtTen.AddHours(i)))
                .ToList();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await held.CommitAsync();
            results = await Task.WhenAll(requests);
        }

        Assert.Single(results, result => result.IsSuccess);
        Assert.All(results.Where(result => result.IsFailure),
            result => Assert.Equal(RequestPublicAppointmentCommandHandler.TooManyPendingMessage, result.Error));
        await using var check = database.CreateContext(business.TenantId);
        Assert.Equal(3, await check.Appointments.CountAsync(a => a.Status == AppointmentStatus.Requested));
    }

    /// <summary>
    /// Asks for a time (Monday at 10:00 unless another) through the transaction of the pipeline, with the services of
    /// production.
    /// </summary>
    private static async Task<Result<PublicAppointmentReceiptDto>> RequestAsync(
        SqlServerTestDatabase database, Business business, string phone, Guid key, DateTimeOffset? startAt = null, Guid? staffId = null)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        await using var context = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        var clock = new Clock();
        var command = new RequestPublicAppointmentCommand
        {
            Slug = business.Slug,
            ServiceProductId = business.ServiceId,
            StartAt = startAt ?? MondayAtTen,
            CustomerName = "Valeria Ruiz",
            CustomerPhone = phone,
            IdempotencyKey = key,
            StaffId = staffId
        };
        var handler = new RequestPublicAppointmentCommandHandler(
            context, tenant, new Anonymous(), new PhoneNormalizationService(), clock, new AppointmentBookingLock(context),
            new AppointmentNotices(context, new PhoneNormalizationService(), clock));

        return await new TransactionBehavior<RequestPublicAppointmentCommand, Result<PublicAppointmentReceiptDto>>(context)
            .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
    }

    /// <summary>A barbershop that books one appointment at a time, or each of <paramref name="people"/> (Fase 24J).</summary>
    private static async Task<Business> SeedAsync(SqlServerTestDatabase database, string[]? people = null)
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
        var staff = new List<Guid>();
        foreach (var name in people ?? [])
        {
            var email = $"{name.ToLowerInvariant()}.{slug}@demo.com";
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                FirstName = name,
                LastName = "Equipo",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Role = UserRole.Staff,
                IsActive = true
            };
            var person = OnlineBookingStaff.Create(tenant.Id, user.Id, $"{name} E.", true, true, [], null);
            seed.Users.Add(user);
            seed.TenantUsers.Add(new TenantUser(tenant.Id, user.Id, TenantUserRole.Staff, TenantUserStatus.Active));
            seed.OnlineBookingStaff.Add(person);
            staff.Add(person.Id);
        }
        await seed.SaveChangesAsync();
        return new Business(tenant.Id, slug, haircut.Id, staff);
    }

    private sealed record Business(Guid TenantId, string Slug, Guid ServiceId, IReadOnlyList<Guid> People);

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

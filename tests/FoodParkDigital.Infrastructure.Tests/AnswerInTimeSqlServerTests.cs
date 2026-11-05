using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Answering in time on SQL Server (Fase 29L, ADR 0066): the agenda's list and the bell write the limit for SQL (the time its
/// customer asked for, or a day after it arrived, whichever comes first), and say what the domain says.
/// </summary>
public class AnswerInTimeSqlServerTests
{
    // Monday 2027-03-01 at 11:00 in Mexico City: its day ends a tick before 06:00 UTC tomorrow.
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndOfToday = new DateTimeOffset(2027, 3, 2, 6, 0, 0, TimeSpan.Zero).AddTicks(-1);

    [SqlServerFact]
    public async Task TheAgendaAndTheBell_ShouldSayTheLimitOfTheDomain()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedTenantAsync(database);
        Guid today, nextWeek, almostADay, movedLater, movedToday, late, older, endsToday, endsTomorrow, accepted;
        await using (var context = database.CreateContext(tenantId))
        {
            // For this afternoon: before then.
            today = Request(context, tenantId, Now.AddHours(-2), Now.AddHours(5)).Id;
            // For next week, arrived an hour ago: by tomorrow at 10:00.
            nextWeek = Request(context, tenantId, Now.AddHours(-1), Now.AddDays(5)).Id;
            // For a minute before a day after it arrived: by then.
            almostADay = Request(context, tenantId, Now.AddHours(-1), Now.AddHours(23).AddMinutes(-1)).Id;
            // Asked for this afternoon, moved to next week: by the time its customer asked for.
            var later = Request(context, tenantId, Now.AddHours(-1), Now.AddHours(3));
            later.Update("Valeria", "+525522107765", "Corte", Now.AddDays(5), Now.AddDays(5).AddHours(1), null, null);
            movedLater = later.Id;
            // Asked for next week, moved to this afternoon: its limit is tomorrow, but the bell marks it for today.
            var sooner = Request(context, tenantId, Now.AddHours(-1), Now.AddDays(5));
            sooner.Update("Valeria", "+525522107765", "Corte", Now.AddHours(4), Now.AddHours(5), null, null);
            movedToday = sooner.Id;
            // Arrived two days ago for next week: already late.
            late = Request(context, tenantId, Now.AddDays(-2), Now.AddDays(5)).Id;
            // Arrived before Fase 29J kept the time asked for: by its own time, this evening.
            var before = Request(context, tenantId, Now.AddHours(-2), Now.AddHours(6));
            context.Entry(before).Property(nameof(Appointment.RequestedStartAt)).CurrentValue = null;
            older = before.Id;
            // Its day ends as today does; a minute later, tomorrow.
            endsToday = Request(context, tenantId, EndOfToday.AddDays(-1), Now.AddDays(5)).Id;
            endsTomorrow = Request(context, tenantId, EndOfToday.AddDays(-1).AddMinutes(1), Now.AddDays(5)).Id;
            // Accepted: nothing left to answer.
            var answered = Request(context, tenantId, Now.AddHours(-2), Now.AddDays(2));
            answered.Accept(Now.AddHours(-1));
            accepted = answered.Id;
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenantId))
        {
            var agenda = (await new GetAppointmentsQueryHandler(context)
                    .Handle(new GetAppointmentsQuery { PageSize = 50 }, CancellationToken.None))
                .Items.ToDictionary(a => a.Id, a => a.AnswerBy);

            Assert.Equal(Now.AddHours(5), agenda[today]);
            Assert.Equal(Now.AddHours(23), agenda[nextWeek]);
            Assert.Equal(Now.AddHours(23).AddMinutes(-1), agenda[almostADay]);
            Assert.Equal(Now.AddHours(3), agenda[movedLater]);
            Assert.Equal(Now.AddHours(23), agenda[movedToday]);
            Assert.Equal(Now.AddDays(-1), agenda[late]);
            Assert.Equal(Now.AddHours(6), agenda[older]);
            Assert.Equal(EndOfToday, agenda[endsToday]);
            Assert.Equal(EndOfToday.AddMinutes(1), agenda[endsTomorrow]);
            Assert.Null(agenda[accepted]);
            // SQL Server says what the domain says.
            foreach (var appointment in await context.Appointments.AsNoTracking().ToListAsync())
                Assert.Equal(AppointmentDto.From(appointment).AnswerBy, agenda[appointment.Id]);

            var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
            tenant.SetTenant(tenantId);
            var owner = new Owner();
            var attention = await new GetAttentionQueryHandler(context, new CurrentAccessContext(owner, tenant, new Managers()), owner, new Clock())
                .Handle(new GetAttentionQuery(), CancellationToken.None);

            // All but the accepted one wait; urgent, all but the three in time until tomorrow.
            var requests = Assert.Single(attention.Items, item => item.Key == AttentionKeys.AppointmentRequests);
            Assert.Equal((9, 6), (requests.Count, requests.UrgentCount));
        }
    }

    /// <summary>A request online that arrived <paramref name="arrivedAt"/> for <paramref name="startAt"/>, still waiting.</summary>
    private static Appointment Request(DbContext context, Guid tenantId, DateTimeOffset arrivedAt, DateTimeOffset startAt)
    {
        var request = Appointment.RequestOnline(
            tenantId, "Valeria", "+525522107765", Guid.NewGuid(), "Corte", startAt, startAt.AddHours(1), null, null, Guid.NewGuid(),
            (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43]);
        // This context has no audit interceptor: stamp when it arrived, as production does.
        request.CreatedAt = arrivedAt;
        context.Add(request);
        return request;
    }

    /// <summary>A salon that books online, in Mexico City.</summary>
    private static async Task<Guid> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Estética {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        await using var context = database.CreateContext(tenant.Id);
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        settings.UpdateOnlineBooking(true, 1);
        context.AddRange(tenant, settings);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Owner : ICurrentUserService
    {
        public string? UserId { get; } = Guid.NewGuid().ToString();
        public string? UserName => "dueña";
        public string? Role => "TenantAdmin";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    /// <summary>The owner: every permission.</summary>
    private sealed class Managers : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service | TenantCapability.Appointments, [], IsManager: true));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>(Enum.GetNames<TenantPermission>());

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}

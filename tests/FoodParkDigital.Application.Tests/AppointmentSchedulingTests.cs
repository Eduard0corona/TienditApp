using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>The agenda books services of the catalog with people of the team, opens with today and moves appointments.</summary>
public class AppointmentSchedulingTests
{
    // 00:00 UTC of the fixture's day, explicit: a DateTime such as Now.Date takes the machine's offset when it becomes a DateTimeOffset.
    private static readonly DateTimeOffset Today = new(SalesTestFixture.Now.UtcDateTime.Date, TimeSpan.Zero);
    private static readonly DateTimeOffset Tomorrow10 = Today.AddDays(1).AddHours(16);

    [Fact]
    public async Task Appointment_ShouldTakeTheNamesFromTheCatalogAndTheTeam()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        var haircut = await fixture.AddProductAsync(tenant, "Corte de cabello", 150m, unit: UnitType.Service);
        var stylist = await AddMemberAsync(fixture, tenant.Id, "Rosa", "Méndez");

        var result = await CreateAsync(fixture, new CreateAppointmentCommand
        {
            CustomerName = "Luis",
            CustomerPhone = "5512345678",
            ServiceProductId = haircut.Id,
            StaffUserId = stylist,
            StartAt = Tomorrow10,
            EndAt = Tomorrow10.AddMinutes(45)
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Corte de cabello", result.Value!.ServiceName);
        Assert.Equal("Rosa Méndez", result.Value.StaffName);
        Assert.Equal(haircut.Id, result.Value.ServiceProductId);
        Assert.Equal(stylist, result.Value.StaffUserId);
    }

    [Fact]
    public async Task ServiceOrPersonOfAnotherBusiness_ShouldBeRejected()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "otro");
        var foreignService = await fixture.AddProductAsync(other, "Masaje", 400m, unit: UnitType.Service);
        var foreignStaff = await AddMemberAsync(fixture, other.Id, "Ana", "Ajena");
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: "propio");

        var withForeignService = await CreateAsync(fixture, NewCommand() with { ServiceProductId = foreignService.Id, ServiceName = null });
        var withForeignStaff = await CreateAsync(fixture, NewCommand() with { StaffUserId = foreignStaff });

        Assert.Equal("El servicio no existe en el catálogo del negocio.", withForeignService.Error);
        Assert.Equal("La persona elegida no está activa en el equipo del negocio.", withForeignStaff.Error);
        Assert.Equal(0, await fixture.Context.Appointments.CountAsync(a => a.TenantId == tenant.Id));
    }

    [Fact]
    public async Task SamePerson_ShouldNotBeBookedTwiceAtTheSameTime()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        var stylist = await AddMemberAsync(fixture, tenant.Id, "Rosa", "Méndez");
        Assert.True((await CreateAsync(fixture, NewCommand() with { StaffUserId = stylist })).IsSuccess);

        var overlapping = await CreateAsync(fixture, NewCommand() with { StaffUserId = stylist, StartAt = Tomorrow10.AddMinutes(30), EndAt = Tomorrow10.AddMinutes(90) });
        var anotherPerson = await CreateAsync(fixture, NewCommand() with { StaffUserId = await AddMemberAsync(fixture, tenant.Id, "Iván", "Paz") });

        Assert.Equal("Ya existe una cita para ese colaborador en el horario seleccionado.", overlapping.Error);
        Assert.True(anotherPerson.IsSuccess, anotherPerson.Error);
    }

    [Fact]
    public async Task Reschedule_ShouldMoveTheAppointmentAndTellTheCustomer()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        var created = (await CreateAsync(fixture, NewCommand())).Value!;
        var signal = new CountingSignal();
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);

        var moved = await UpdateAsync(fixture, new UpdateAppointmentCommand
        {
            AppointmentId = created.Id,
            CustomerName = created.CustomerName,
            CustomerPhone = created.CustomerPhone,
            ServiceName = created.ServiceName,
            StartAt = Tomorrow10.AddDays(1),
            EndAt = Tomorrow10.AddDays(1).AddHours(1)
        }, signal);

        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(Tomorrow10.AddDays(1), (await fixture.Context.Appointments.AsNoTracking().SingleAsync()).StartAt);
        // 16:00 UTC is 10:00 in Mexico City, the business's time zone.
        Assert.Equal(
            "Hola Luis, tu cita de Corte ahora es el martes 6 de octubre a las 10:00 a. m. en Negocio demo.",
            (await NoticesAsync(fixture)).Last().Body);
        Assert.Equal(1, signal.Count);
    }

    [Fact]
    public async Task Notices_ShouldBeQueuedWithTheChange_OnlyForWhatTheCustomerCaresAbout()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        await ConnectWhatsAppAsync(fixture, tenant.Id);
        var signal = new CountingSignal();

        // A minute between steps keeps the queue in the order they happened.
        void NextMinute() => fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(1);

        var created = (await CreateAsync(fixture, NewCommand(), signal)).Value!;
        NextMinute();
        await ChangeStatusAsync(fixture, created.Id, AppointmentStatus.Confirmed, signal);
        NextMinute();
        await ChangeStatusAsync(fixture, created.Id, AppointmentStatus.Confirmed, signal);
        await ChangeStatusAsync(fixture, created.Id, AppointmentStatus.InService, signal);
        NextMinute();
        var second = (await CreateAsync(fixture, NewCommand() with { CustomerName = "Ana Paz", StartAt = Tomorrow10.AddHours(2), EndAt = Tomorrow10.AddHours(3) }, signal)).Value!;
        NextMinute();
        await ChangeStatusAsync(fixture, second.Id, AppointmentStatus.Cancelled, signal);

        var notices = await NoticesAsync(fixture);
        Assert.Equal(
        [
            "Hola Luis, quedó agendada tu cita de Corte el lunes 5 de octubre a las 10:00 a. m. en Negocio demo.",
            "Hola Luis, tu cita de Corte del lunes 5 de octubre a las 10:00 a. m. en Negocio demo está confirmada.",
            "Hola Ana, quedó agendada tu cita de Corte el lunes 5 de octubre a las 12:00 p. m. en Negocio demo.",
            "Hola Ana, tu cita de Corte del lunes 5 de octubre a las 12:00 p. m. en Negocio demo fue cancelada. Respóndenos si quieres agendar otra."
        ], notices.Select(n => n.Body));
        Assert.All(notices, n => Assert.Equal("+525512345678", n.Phone));
        Assert.Equal([created.Id, created.Id, second.Id, second.Id], notices.Select(n => n.AppointmentId!.Value));
        Assert.Equal(4, signal.Count);
    }

    [Fact]
    public async Task WithoutWhatsApp_TheAgendaShouldWorkAndQueueNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        var signal = new CountingSignal();

        var created = await CreateAsync(fixture, NewCommand(), signal);
        await ChangeStatusAsync(fixture, created.Value!.Id, AppointmentStatus.Cancelled, signal);

        Assert.True(created.IsSuccess, created.Error);
        Assert.Empty(await NoticesAsync(fixture));
        Assert.Equal(0, signal.Count);
    }

    [Fact]
    public async Task ClosedAppointment_ShouldNotBeEditedNorRescheduled()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        var created = (await CreateAsync(fixture, NewCommand())).Value!;
        var stored = await fixture.Context.Appointments.SingleAsync();
        stored.ChangeStatus(AppointmentStatus.Completed);
        await fixture.Context.SaveChangesAsync();

        var result = await UpdateAsync(fixture, new UpdateAppointmentCommand
        {
            AppointmentId = created.Id,
            CustomerName = "Otro",
            CustomerPhone = created.CustomerPhone,
            ServiceName = created.ServiceName,
            StartAt = Tomorrow10.AddDays(3),
            EndAt = Tomorrow10.AddDays(3).AddHours(1)
        });

        Assert.Equal("La cita ya está cerrada y no permite más cambios.", result.Error);
        Assert.Throws<DomainException>(() => stored.Update("Otro", "5512345678", "Corte", Tomorrow10, Tomorrow10.AddHours(1), null, null));
    }

    [Fact]
    public async Task Agenda_ShouldOpenWithTheSoonest_AndHistoryWithTheNewest()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        var now = SalesTestFixture.Now;
        fixture.Context.Appointments.AddRange(
            new Appointment(tenant.Id, "Ayer", "5512345678", "Corte", now.AddDays(-1), now.AddDays(-1).AddHours(1)),
            new Appointment(tenant.Id, "Semana pasada", "5512345678", "Corte", now.AddDays(-7), now.AddDays(-7).AddHours(1)),
            new Appointment(tenant.Id, "Mañana", "5512345678", "Corte", now.AddDays(1), now.AddDays(1).AddHours(1)),
            new Appointment(tenant.Id, "En una hora", "5512345678", "Corte", now.AddHours(1), now.AddHours(2)));
        await fixture.Context.SaveChangesAsync();
        var handler = new GetAppointmentsQueryHandler(fixture.Context);

        var upcoming = await handler.Handle(new GetAppointmentsQuery { FromDate = Today }, CancellationToken.None);
        var history = await handler.Handle(new GetAppointmentsQuery { ToDate = now }, CancellationToken.None);

        Assert.Equal(["En una hora", "Mañana"], upcoming.Items.Select(a => a.CustomerName));
        Assert.Equal(["Ayer", "Semana pasada"], history.Items.Select(a => a.CustomerName));
    }

    private static CreateAppointmentCommand NewCommand() => new()
    {
        CustomerName = "Luis",
        CustomerPhone = "5512345678",
        ServiceName = "Corte",
        StartAt = Tomorrow10,
        EndAt = Tomorrow10.AddHours(1)
    };

    private static Task<Result<Application.Appointments.DTOs.AppointmentDto>> CreateAsync(
        SalesTestFixture fixture, CreateAppointmentCommand command, IOutgoingMessageSignal? signal = null)
        => new CreateAppointmentCommandHandler(fixture.Context, fixture.TenantService, Notices(fixture), signal)
            .Handle(command, CancellationToken.None);

    private static Task<Result<Application.Appointments.DTOs.AppointmentDto>> UpdateAsync(
        SalesTestFixture fixture, UpdateAppointmentCommand command, IOutgoingMessageSignal? signal = null)
        => new UpdateAppointmentCommandHandler(fixture.Context, Notices(fixture), signal)
            .Handle(command, CancellationToken.None);

    private static Task<Result<Application.Appointments.DTOs.AppointmentDto>> ChangeStatusAsync(
        SalesTestFixture fixture, Guid appointmentId, AppointmentStatus status, IOutgoingMessageSignal? signal = null)
        => new UpdateAppointmentStatusCommandHandler(fixture.Context, Notices(fixture), signal)
            .Handle(new UpdateAppointmentStatusCommand { AppointmentId = appointmentId, NewStatus = status }, CancellationToken.None);

    private static AppointmentNotices Notices(SalesTestFixture fixture)
        => new(fixture.Context, new PhoneNormalizationService(), fixture.Clock);

    private static Task<List<Domain.Messaging.Entities.OutgoingMessage>> NoticesAsync(SalesTestFixture fixture)
        => fixture.Context.OutgoingMessages
            .AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.AppointmentNotice)
            .OrderBy(m => m.NextAttemptAt)
            .ToListAsync();

    private static async Task ConnectWhatsAppAsync(SalesTestFixture fixture, Guid tenantId)
    {
        var config = new WhatsAppConfig(tenantId);
        config.Update(true, "token", "123456", "waba", "+52 55 0000 0000");
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task<Guid> AddMemberAsync(SalesTestFixture fixture, Guid tenantId, string firstName, string lastName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName.ToLowerInvariant()}@negocio.test",
            UserName = $"{firstName.ToLowerInvariant()}@negocio.test",
            FirstName = firstName,
            LastName = lastName,
            Role = UserRole.Staff,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        fixture.Context.TenantUsers.Add(new TenantUser(tenantId, user.Id, TenantUserRole.Staff));
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }

    private sealed class CountingSignal : IOutgoingMessageSignal
    {
        public int Count { get; private set; }

        public void Notify() => Count++;
    }
}

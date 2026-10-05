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
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>The agenda books services of the catalog with people of the team, opens with today and moves appointments.</summary>
public class AppointmentSchedulingTests
{
    private static readonly DateTimeOffset Tomorrow10 = SalesTestFixture.Now.AddDays(1).Date.AddHours(16);

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
        var created = (await CreateAsync(fixture, NewCommand())).Value!;
        var whatsApp = new RecordingWhatsApp();

        var moved = await UpdateAsync(fixture, whatsApp, new UpdateAppointmentCommand
        {
            AppointmentId = created.Id,
            CustomerName = created.CustomerName,
            CustomerPhone = created.CustomerPhone,
            ServiceName = created.ServiceName,
            StartAt = Tomorrow10.AddDays(1),
            EndAt = Tomorrow10.AddDays(1).AddHours(1)
        });

        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(Tomorrow10.AddDays(1), (await fixture.Context.Appointments.AsNoTracking().SingleAsync()).StartAt);
        Assert.Contains("cambió", Assert.Single(whatsApp.Messages));
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

        var result = await UpdateAsync(fixture, new RecordingWhatsApp(), new UpdateAppointmentCommand
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
        var handler = new GetAppointmentsQueryHandler(fixture.Context, fixture.TenantService);

        var upcoming = await handler.Handle(new GetAppointmentsQuery { FromDate = now.Date }, CancellationToken.None);
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

    private static Task<Result<Application.Appointments.DTOs.AppointmentDto>> CreateAsync(SalesTestFixture fixture, CreateAppointmentCommand command)
        => new CreateAppointmentCommandHandler(fixture.Context, fixture.TenantService, new RecordingWhatsApp(),
                NullLogger<CreateAppointmentCommandHandler>.Instance)
            .Handle(command, CancellationToken.None);

    private static Task<Result<Application.Appointments.DTOs.AppointmentDto>> UpdateAsync(
        SalesTestFixture fixture, RecordingWhatsApp whatsApp, UpdateAppointmentCommand command)
        => new UpdateAppointmentCommandHandler(fixture.Context, fixture.TenantService, whatsApp,
                NullLogger<UpdateAppointmentCommandHandler>.Instance)
            .Handle(command, CancellationToken.None);

    private static async Task<Guid> AddMemberAsync(SalesTestFixture fixture, Guid tenantId, string firstName, string lastName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName.ToLowerInvariant()}@negocio.test",
            UserName = $"{firstName.ToLowerInvariant()}@negocio.test",
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        };
        fixture.Context.Users.Add(user);
        fixture.Context.TenantUsers.Add(new TenantUser(tenantId, user.Id, TenantUserRole.Staff));
        await fixture.Context.SaveChangesAsync();
        return user.Id;
    }

    private sealed class RecordingWhatsApp : IWhatsAppService
    {
        public List<string> Messages { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode,
            string[] parameters, CancellationToken cancellationToken = default) => Task.FromResult(MessageSendResult.Sent());

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.FromResult(MessageSendResult.Sent());
        }
    }
}

using System.Text.Json;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tenants.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The special days of a business: the owner marks, moves and removes periods in Configuración › Negocio, from the
/// business's own today on. The dates that passed stay as they were, every change is recorded once, a request that
/// changes nothing records nothing, and a change based on an old version is refused.
/// </summary>
public class SpecialDaysFlowTests
{
    // 2026-10-11 18:00 UTC is Sunday 12:00 in Mexico City.
    private static readonly DateTimeOffset Sunday = new(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Christmas = new(2026, 12, 25);

    [Fact]
    public async Task Add_ShouldSaveThePeriodAndRecordWhoAddedIt()
    {
        await using var fixture = Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "fonda");

        var result = await Add(fixture, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 4), reason: "Vacaciones");

        Assert.True(result.IsSuccess, result.Error);
        var period = Assert.Single(result.Value.Upcoming);
        Assert.Equal((new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 4)), (period.From, period.To));
        Assert.Equal(SpecialDayKind.Closed, period.Kind);
        Assert.Empty(period.Openings);
        Assert.Equal("Vacaciones", period.Reason);
        Assert.Equal(1, period.Revision);
        Assert.False(period.InProgress);
        Assert.Equal(15, result.Value.UpcomingDates);
        var rows = await fixture.Context.SpecialDays.ToListAsync();
        Assert.Equal(15, rows.Count);
        Assert.All(rows, row => Assert.Equal((tenant.Id, period.Id, 1), (row.TenantId, row.PeriodId, row.Revision)));

        var audit = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal(("SpecialDayAdded", "SpecialDay", period.Id.ToString()), (audit.Action, audit.EntityName, audit.EntityId));
        Assert.Equal(tenant.Id, audit.TenantId);
        Assert.Equal("cajero", audit.PerformedBy);
        var metadata = Metadata(audit.Metadata);
        Assert.Equal(period.Id, metadata.GetProperty("Period").GetGuid());
        Assert.Equal("2026-12-21..2027-01-04 Closed (Vacaciones)", metadata.GetProperty("New").GetString());
    }

    [Fact]
    public async Task Update_ShouldChangeTheRuleAndRecordWhatItWas()
    {
        await using var fixture = Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var added = Assert.Single((await Add(fixture, new DateOnly(2026, 12, 24), reason: "Nochebuena")).Value.Upcoming);

        var result = await UpdateHandler(fixture).Handle(new UpdateSpecialDayCommand(
            added.Id, added.Revision, new DateOnly(2026, 12, 24), null, SpecialDayKind.SpecialHours,
            [new SpecialOpeningDto { Opens = "09:00", Closes = "18:00" }], "Nochebuena"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var period = Assert.Single(result.Value.Upcoming);
        Assert.Equal((added.Id, 2, SpecialDayKind.SpecialHours), (period.Id, period.Revision, period.Kind));
        Assert.Equal(["09:00-18:00"], period.Openings.Select(o => $"{o.Opens}-{o.Closes}"));
        var audit = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SpecialDayUpdated");
        Assert.Equal((added.Id.ToString(), "cajero"), (audit.EntityId, audit.PerformedBy));
        var metadata = Metadata(audit.Metadata);
        Assert.Equal("2026-12-24 Closed (Nochebuena)", metadata.GetProperty("Previous").GetString());
        Assert.Equal("2026-12-24 SpecialHours 09:00-18:00 (Nochebuena)", metadata.GetProperty("New").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("NewPeriod").ValueKind);
    }

    [Fact]
    public async Task Remove_ShouldRemoveItsDatesAndRecordThem()
    {
        await using var fixture = Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var added = Assert.Single((await Add(fixture, new DateOnly(2026, 12, 21), new DateOnly(2026, 12, 23), "Vacaciones")).Value.Upcoming);

        var result = await RemoveHandler(fixture).Handle(new RemoveSpecialDayCommand(added.Id, added.Revision), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value.Upcoming);
        Assert.Empty(result.Value.Past);
        Assert.Empty(await fixture.Context.SpecialDays.ToListAsync());
        var audit = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SpecialDayRemoved");
        Assert.Equal((added.Id.ToString(), "cajero"), (audit.EntityId, audit.PerformedBy));
        var metadata = Metadata(audit.Metadata);
        Assert.Equal("2026-12-21..2026-12-23 Closed (Vacaciones)", metadata.GetProperty("Previous").GetString());
        Assert.Equal("2026-12-21", metadata.GetProperty("RemovedFrom").GetString());
    }

    [Fact]
    public async Task NewRule_ForAPeriodUnderWay_ShouldStartToday_AndLeaveTheDaysThatPassed()
    {
        await using var fixture = Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var added = Assert.Single((await Add(fixture, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 20), "Remodelación")).Value.Upcoming);
        fixture.Clock.UtcNow = Sunday.AddDays(3);

        var result = await UpdateHandler(fixture).Handle(new UpdateSpecialDayCommand(
            added.Id, added.Revision, added.From, added.To, SpecialDayKind.SpecialHours,
            [new SpecialOpeningDto { Opens = "10:00", Closes = "14:00" }], "Remodelación"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var past = Assert.Single(result.Value.Past);
        Assert.Equal((added.Id, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13), SpecialDayKind.Closed), (past.Id, past.From, past.To, past.Kind));
        var upcoming = Assert.Single(result.Value.Upcoming);
        Assert.NotEqual(added.Id, upcoming.Id);
        Assert.Equal((new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 20), 2), (upcoming.From, upcoming.To, upcoming.Revision));
        Assert.False(upcoming.InProgress);
        var stored = await fixture.Context.SpecialDays.Where(day => day.Date < new DateOnly(2026, 10, 14)).ToListAsync();
        Assert.All(stored, row => Assert.Equal((added.Id, 1, SpecialDayKind.Closed), (row.PeriodId, row.Revision, row.Kind)));

        var metadata = Metadata((await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SpecialDayUpdated")).Metadata);
        Assert.Equal(added.Id, metadata.GetProperty("Period").GetGuid());
        Assert.Equal("2026-10-11..2026-10-20 Closed (Remodelación)", metadata.GetProperty("Previous").GetString());
        Assert.Equal("2026-10-14..2026-10-20 SpecialHours 10:00-14:00 (Remodelación)", metadata.GetProperty("New").GetString());
        Assert.Equal(upcoming.Id, metadata.GetProperty("NewPeriod").GetGuid());
    }

    [Fact]
    public async Task Remove_OfAPeriodUnderWay_ShouldEndItYesterday()
    {
        await using var fixture = Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var added = Assert.Single((await Add(fixture, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 20), "Remodelación")).Value.Upcoming);
        fixture.Clock.UtcNow = Sunday.AddDays(3);
        var inProgress = Assert.Single((await QueryHandler(fixture).Handle(new GetSpecialDaysQuery(), CancellationToken.None)).Value.Upcoming);
        Assert.True(inProgress.InProgress);

        var result = await RemoveHandler(fixture).Handle(new RemoveSpecialDayCommand(added.Id, added.Revision), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value.Upcoming);
        var past = Assert.Single(result.Value.Past);
        Assert.Equal((new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13)), (past.From, past.To));
        Assert.Equal(3, await fixture.Context.SpecialDays.CountAsync());
        var metadata = Metadata((await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SpecialDayRemoved")).Metadata);
        Assert.Equal("2026-10-11..2026-10-20 Closed (Remodelación)", metadata.GetProperty("Previous").GetString());
        Assert.Equal("2026-10-14", metadata.GetProperty("RemovedFrom").GetString());
    }

    [Fact]
    public async Task SameRequestTwice_ShouldSaveAndRecordItOnce()
    {
        await using var fixture = Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);

        var first = await Add(fixture, Christmas, reason: "Navidad");
        var second = await Add(fixture, Christmas, reason: "Navidad");
        var period = Assert.Single(second.Value.Upcoming);
        var unchanged = await UpdateHandler(fixture).Handle(new UpdateSpecialDayCommand(
            period.Id, period.Revision, Christmas, Christmas, SpecialDayKind.Closed, [], "Navidad"), CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        Assert.True(unchanged.IsSuccess, unchanged.Error);
        Assert.Equal(Assert.Single(first.Value.Upcoming).Id, period.Id);
        Assert.Equal(1, Assert.Single(unchanged.Value.Upcoming).Revision);
        Assert.Single(await fixture.Context.SpecialDays.ToListAsync());
        Assert.Equal("SpecialDayAdded", (await fixture.Context.AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task Refusals_ShouldChangeAndRecordNothing()
    {
        await using var fixture = Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var added = Assert.Single((await Add(fixture, Christmas, reason: "Navidad")).Value.Upcoming);

        // A date that already has a special day is named.
        var overlap = await Assert.ThrowsAsync<DomainException>(() => Add(fixture, new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26)));
        // Openings that make no sense together.
        await Assert.ThrowsAsync<DomainException>(() => AddHandler(fixture).Handle(new AddSpecialDayCommand(
            new DateOnly(2026, 12, 31), null, SpecialDayKind.SpecialHours,
            [new SpecialOpeningDto { Opens = "09:00", Closes = "14:00" }, new SpecialOpeningDto { Opens = "13:00", Closes = "18:00" }], null),
            CancellationToken.None));

        Assert.Equal("El viernes 25 de diciembre ya tiene un día especial: cámbialo o quítalo primero.", overlap.Message);
        Assert.Equal(added.Id, (await fixture.Context.SpecialDays.SingleAsync()).PeriodId);
        Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ChangeOnAnOldVersion_ShouldBeRefusedAsStale()
    {
        await using var fixture = Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var seen = Assert.Single((await Add(fixture, Christmas, reason: "Navidad")).Value.Upcoming);
        // Someone else gives it a reason meanwhile.
        await UpdateHandler(fixture).Handle(new UpdateSpecialDayCommand(
            seen.Id, seen.Revision, Christmas, null, SpecialDayKind.Closed, null, "Navidad en familia"), CancellationToken.None);

        var update = await UpdateHandler(fixture).Handle(new UpdateSpecialDayCommand(
            seen.Id, seen.Revision, Christmas, new DateOnly(2026, 12, 26), SpecialDayKind.Closed, null, "Navidad"), CancellationToken.None);
        var remove = await RemoveHandler(fixture).Handle(new RemoveSpecialDayCommand(seen.Id, seen.Revision), CancellationToken.None);

        Assert.Equal(SpecialDayErrors.Stale, update.Error);
        Assert.Equal(SpecialDayErrors.Stale, remove.Error);
        var stored = await fixture.Context.SpecialDays.SingleAsync();
        Assert.Equal(("Navidad en familia", 2), (stored.Reason, stored.Revision));
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task AnotherBusinesssPeriod_ShouldNotBeFound_AndOnlyTheBusinessInScopeChanges()
    {
        await using var fixture = Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        var theirs = Assert.Single((await Add(fixture, Christmas, reason: "Navidad")).Value.Upcoming);
        var mine = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "mio");

        // The same date in another business is free.
        var added = await Add(fixture, Christmas, reason: "Cerrado");
        await Assert.ThrowsAsync<EntityNotFoundException>(() => UpdateHandler(fixture).Handle(new UpdateSpecialDayCommand(
            theirs.Id, theirs.Revision, Christmas, null, SpecialDayKind.Closed, null, "Cambiado"), CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => RemoveHandler(fixture).Handle(
            new RemoveSpecialDayCommand(theirs.Id, theirs.Revision), CancellationToken.None));
        var list = await QueryHandler(fixture).Handle(new GetSpecialDaysQuery(), CancellationToken.None);

        Assert.True(added.IsSuccess, added.Error);
        Assert.Equal("Cerrado", Assert.Single(list.Value.Upcoming).Reason);
        Assert.NotEqual(theirs.Id, list.Value.Upcoming[0].Id);
        var rows = await fixture.Context.SpecialDays.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(("Navidad", 1), rows.Where(row => row.TenantId == other.Id).Select(row => (row.Reason, row.Revision)).Single());
        Assert.Single(rows, row => row.TenantId == mine.Id);
        Assert.Equal(1, await fixture.Context.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.TenantId == other.Id));
    }

    [Theory]
    // 2026-12-25 05:30 UTC is 24 Dec 23:30 in Mexico City; 06:30 UTC is already 25 Dec there, and 24 Dec 22:30 in Tijuana.
    [InlineData(5, "America/Mexico_City", true)]
    [InlineData(6, "America/Mexico_City", false)]
    [InlineData(6, "America/Tijuana", true)]
    public async Task Today_ShouldBeTheBusinesssOwnDate(int utcHour, string timeZone, bool accepted)
    {
        await using var fixture = Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        (await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id)).SetTimeZone(timeZone);
        await fixture.Context.SaveChangesAsync();
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 12, 25, utcHour, 30, 0, TimeSpan.Zero);

        var add = () => Add(fixture, new DateOnly(2026, 12, 24), reason: "Nochebuena");

        if (accepted)
        {
            var result = await add();
            Assert.True(result.IsSuccess, result.Error);
            Assert.Equal(new DateOnly(2026, 12, 24), result.Value.Today);
        }
        else
        {
            var error = await Assert.ThrowsAsync<DomainException>(add);
            Assert.Equal("Elige una fecha de hoy en adelante.", error.Message);
        }
    }

    [Fact]
    public async Task List_ShouldShowWhatIsAhead_AndTheLast90Days_WithTheLimitsAndHolidays()
    {
        await using var fixture = Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        await SeedAsync(fixture, other.Id, new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 24));
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "mio");
        await SeedAsync(fixture, tenant.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10));    // 123 days ago
        await SeedAsync(fixture, tenant.Id, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 13));    // ended 90 days ago
        await SeedAsync(fixture, tenant.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3));
        await SeedAsync(fixture, tenant.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 15));   // under way
        await SeedAsync(fixture, tenant.Id, Christmas, Christmas);

        var result = await QueryHandler(fixture).Handle(new GetSpecialDaysQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var list = result.Value;
        Assert.Equal(new DateOnly(2026, 10, 11), list.Today);
        Assert.False(list.HasWeeklyHours);
        Assert.Equal(
            [(new DateOnly(2026, 10, 5), true), (Christmas, false)],
            list.Upcoming.Select(period => (period.From, period.InProgress)));
        Assert.Equal(
            [new DateOnly(2026, 8, 1), new DateOnly(2026, 7, 10)],
            list.Past.Select(period => period.From));
        Assert.All(list.Past, period => Assert.False(period.InProgress));
        Assert.Equal(new DateOnly(2026, 7, 13), list.Past[1].To);
        Assert.Equal(6, list.UpcomingDates);
        Assert.Equal((120, 92, 366, 40), (list.MaxUpcomingDates, list.MaxDaysPerPeriod, list.MaxDaysAhead, list.MaxReasonLength));
        // The holidays not marked yet: Navidad is.
        Assert.Contains(list.Suggestions, suggestion => suggestion.Date == new DateOnly(2026, 11, 16) && suggestion.Statutory);
        Assert.DoesNotContain(list.Suggestions, suggestion => suggestion.Date == Christmas);
    }

    [Fact]
    public async Task List_ShouldSuggestFromTheWeeklyHours()
    {
        await using var fixture = Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        (await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id)).SetOpeningHours(OpeningHours.Create(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }
                .Select(day => new OpeningInterval(day, new TimeOnly(9, 0), new TimeOnly(20, 0)))));
        await fixture.Context.SaveChangesAsync();

        var list = (await QueryHandler(fixture).Handle(new GetSpecialDaysQuery(), CancellationToken.None)).Value;

        Assert.True(list.HasWeeklyHours);
        var nochebuena = Assert.Single(list.Suggestions, suggestion => suggestion.Name == "Nochebuena");
        Assert.Equal(SpecialDayKind.SpecialHours, nochebuena.Kind);
        Assert.Equal(["09:00-18:00"], nochebuena.Openings.Select(o => $"{o.Opens}-{o.Closes}"));
    }

    [Theory]
    [MemberData(nameof(InvalidDays))]
    public void Validator_ShouldRefuseDaysThatDoNotMakeSense(string field, AddSpecialDayCommand command, string message)
    {
        var result = new AddSpecialDayCommandValidator().Validate(command);

        Assert.False(result.IsValid, field);
        Assert.Contains(result.Errors, error => error.ErrorMessage == message);
    }

    public static TheoryData<string, AddSpecialDayCommand, string> InvalidDays()
    {
        var day = new DateOnly(2026, 12, 25);
        SpecialOpeningDto Opening(string opens, string closes) => new() { Opens = opens, Closes = closes };
        return new()
        {
            { "From", new AddSpecialDayCommand(default, null, SpecialDayKind.Closed, null, null), "Elige la fecha." },
            { "To", new AddSpecialDayCommand(day, day.AddDays(-1), SpecialDayKind.Closed, null, null), "La fecha final no puede ser antes de la inicial." },
            { "Kind", new AddSpecialDayCommand(day, null, (SpecialDayKind)7, null, null), "El tipo de día especial no existe." },
            { "Closed", new AddSpecialDayCommand(day, null, SpecialDayKind.Closed, [Opening("09:00", "14:00")], null), "Un día cerrado no lleva horario." },
            { "None", new AddSpecialDayCommand(day, null, SpecialDayKind.SpecialHours, [], null), "Escribe uno o dos horarios para ese día." },
            { "Null", new AddSpecialDayCommand(day, null, SpecialDayKind.SpecialHours, null, null), "Escribe uno o dos horarios para ese día." },
            { "Three", new AddSpecialDayCommand(day, null, SpecialDayKind.SpecialHours,
                [Opening("08:00", "09:00"), Opening("10:00", "11:00"), Opening("12:00", "13:00")], null), "Escribe uno o dos horarios para ese día." },
            { "Opens", new AddSpecialDayCommand(day, null, SpecialDayKind.SpecialHours, [Opening("9:00", "18:00")], null), "Escribe la hora de apertura como 09:00." },
            { "Blank", new AddSpecialDayCommand(day, null, SpecialDayKind.SpecialHours, [Opening("", "18:00")], null), "Escribe la hora de apertura como 09:00." },
            { "Missing", new AddSpecialDayCommand(day, null, SpecialDayKind.SpecialHours, [null!], null), "Escribe la hora de apertura como 09:00." },
            { "Closes", new AddSpecialDayCommand(day, null, SpecialDayKind.SpecialHours, [Opening("09:00", "24:00")], null), "Escribe la hora de cierre como 20:00." },
            { "Long", new AddSpecialDayCommand(day, null, SpecialDayKind.Closed, null, new string('a', 41)), "El motivo tiene máximo 40 caracteres y va en una sola línea." },
            { "Newline", new AddSpecialDayCommand(day, null, SpecialDayKind.Closed, null, "Navidad\ny más"), "El motivo tiene máximo 40 caracteres y va en una sola línea." },
        };
    }

    [Fact]
    public void Validator_ShouldTakeAWholeDay_AndAskForTheVersionToChangeIt()
    {
        var day = new DateOnly(2026, 12, 24);
        List<SpecialOpeningDto> openings = [new() { Opens = "09:00", Closes = "14:00" }, new() { Opens = "16:00", Closes = "02:00" }];

        Assert.True(new AddSpecialDayCommandValidator().Validate(
            new AddSpecialDayCommand(day, day.AddDays(2), SpecialDayKind.SpecialHours, openings, new string('a', 40))).IsValid);
        Assert.True(new AddSpecialDayCommandValidator().Validate(new AddSpecialDayCommand(day, null, SpecialDayKind.Closed, null, null)).IsValid);
        var update = new UpdateSpecialDayCommandValidator().Validate(
            new UpdateSpecialDayCommand(Guid.NewGuid(), 0, day, null, SpecialDayKind.Closed, null, null));
        var remove = new RemoveSpecialDayCommandValidator().Validate(new RemoveSpecialDayCommand(Guid.Empty, 1));
        Assert.Equal("Recarga para ver la versión actual del día especial.", Assert.Single(update.Errors).ErrorMessage);
        Assert.Equal("Recarga para ver la versión actual del día especial.", Assert.Single(remove.Errors).ErrorMessage);
    }

    [Fact]
    public async Task Add_WhenTheSamePeriodWasSavedAtTheSameTime_ShouldAnswerItWithoutSavingItTwice()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(host);
        var command = new AddSpecialDayCommand(new DateOnly(2026, 3, 16), null, SpecialDayKind.Closed, null, "Natalicio de Benito Juárez");

        // A second tap saves the same day after this request read the calendar and before it saves: the unique date
        // refuses this copy.
        host.BeforeSave.Arm(
            when: context => context.ChangeTracker.Entries<SpecialDay>().Any(e => e.State == EntityState.Added),
            action: _ => host.HandleAsync<AddSpecialDayCommandHandler, Result<SpecialDaysDto>>(handler => handler.Handle(command, CancellationToken.None)));

        var result = await host.HandleAsync<AddSpecialDayCommandHandler, Result<SpecialDaysDto>>(handler => handler.Handle(command, CancellationToken.None));

        Assert.True(host.BeforeSave.Fired);
        Assert.True(result.IsSuccess, result.Error);
        var stored = await host.QueryAsync(async db => new { Days = await db.SpecialDays.ToListAsync(), Audit = await db.AuditLogs.ToListAsync() });
        Assert.Equal(Assert.Single(stored.Days).PeriodId, Assert.Single(result.Value.Upcoming).Id);
        Assert.Equal("SpecialDayAdded", Assert.Single(stored.Audit).Action);
    }

    [Fact]
    public async Task Add_WhenADateWasTakenAtTheSameTime_ShouldSaySoAndSaveNothing()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(host);

        host.BeforeSave.Arm(
            when: context => context.ChangeTracker.Entries<SpecialDay>().Any(e => e.State == EntityState.Added),
            action: _ => host.HandleAsync<AddSpecialDayCommandHandler, Result<SpecialDaysDto>>(handler => handler.Handle(
                new AddSpecialDayCommand(new DateOnly(2026, 3, 17), null, SpecialDayKind.Closed, null, "Inventario"), CancellationToken.None)));

        var result = await host.HandleAsync<AddSpecialDayCommandHandler, Result<SpecialDaysDto>>(handler => handler.Handle(
            new AddSpecialDayCommand(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 18), SpecialDayKind.Closed, null, "Vacaciones"),
            CancellationToken.None));

        Assert.True(host.BeforeSave.Fired);
        Assert.Equal(SpecialDayErrors.TakenMeanwhile, result.Error);
        var stored = await host.QueryAsync(async db => new { Days = await db.SpecialDays.ToListAsync(), Audit = await db.AuditLogs.ToListAsync() });
        Assert.Equal("Inventario", Assert.Single(stored.Days).Reason);
        Assert.Single(stored.Audit);
    }

    private static SalesTestFixture Create()
    {
        var fixture = SalesTestFixture.Create();
        fixture.Clock.UtcNow = Sunday;
        return fixture;
    }

    private static Task<Result<SpecialDaysDto>> Add(SalesTestFixture fixture, DateOnly from, DateOnly? to = null, string? reason = null)
        => AddHandler(fixture).Handle(new AddSpecialDayCommand(from, to, SpecialDayKind.Closed, null, reason), CancellationToken.None);

    /// <summary>Closed days saved as they were when they were marked, months ago (the rows of the past are history).</summary>
    private static async Task SeedAsync(SalesTestFixture fixture, Guid tenantId, DateOnly from, DateOnly to)
    {
        var change = new SpecialDayCalendar(tenantId, from, []).Add(from, to, SpecialDayRule.Closed("Cerrado"));
        fixture.Context.SpecialDays.AddRange(change.Added);
        await fixture.Context.SaveChangesAsync();
    }

    // SqliteTestHost's clock: 2026-02-16 16:00 UTC, 10:00 in Mexico City.
    private static async Task SeedBusinessAsync(SqliteTestHost host)
    {
        var tenant = new Tenant("Fonda", "fonda", BusinessType.Food, "fonda@demo.com");
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(tenant);
            db.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Food));
            db.Branches.Add(Branch.CreateDefault(tenant.Id));
            await db.SaveChangesAsync();
        });
        host.SetTenant(tenant.Id);
    }

    private static JsonElement Metadata(string? metadata) => JsonDocument.Parse(metadata!).RootElement;

    private static AddSpecialDayCommandHandler AddHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, fixture.TenantService, fixture.Clock);

    private static UpdateSpecialDayCommandHandler UpdateHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, fixture.TenantService, fixture.Clock);

    private static RemoveSpecialDayCommandHandler RemoveHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, fixture.TenantService, fixture.Clock);

    private static GetSpecialDaysQueryHandler QueryHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, fixture.TenantService, fixture.Clock);
}

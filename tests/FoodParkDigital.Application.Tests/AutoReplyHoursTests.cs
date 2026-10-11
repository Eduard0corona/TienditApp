using System.Globalization;
using FoodParkDigital.Application.AiAgents;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A question about the hours on WhatsApp gets the real ones, read as the public menu reads them: whether the business
/// is open now and why not, its week and its special days of the next two weeks. The texts of the business go around
/// them and give way when the reply is too long; a question that only sounds like the hours does not get them; and
/// when the hours cannot be read, the customer gets the reply of before, with the error logged.
/// </summary>
public class AutoReplyHoursTests
{
    private const string AppUrl = "https://app.tienditapp.mx";
    private const string MenuLink = "https://app.tienditapp.mx/m/demo";
    private const string Description = "Estamos en Av. Juárez 10, Centro.";
    private const string Products = "Tacos $25, aguas $15.";
    private const string Week = "Nuestro horario:\nLunes a sábado: 9:00 a 20:00\nDomingo: cerrado";

    // Times in Mexico City (UTC-6).
    private static readonly DateTimeOffset MondayNoon = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SundayMorning = new(2026, 10, 4, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChristmasEveNoon = new(2026, 12, 24, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChristmasNoon = new(2026, 12, 25, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ChristmasEve = new(2026, 12, 24);
    private static readonly DateOnly Christmas = new(2026, 12, 25);

    private static readonly SpecialDayRule Navidad = SpecialDayRule.Closed("Navidad");
    private static readonly SpecialDayRule Nochebuena = SpecialDayRule.Hours([Opening("09:00", "18:00")], "Nochebuena");

    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    [Fact]
    public async Task WhenDoYouClose_WhileOpen_ShouldTellTheStatusTheWeekAndTheMenu()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿A qué hora cierran?", null);

        Assert.Equal(
            $"Ahora estamos abiertos: cerramos a las 20:00.\n\n{Week}\n\nMira el menú y haz tu pedido aquí: {MenuLink}",
            reply);
    }

    [Fact]
    public async Task OpenTomorrow_WhileClosed_ShouldSayWhenItOpens_AndOfferTheMenuOnlyToSee()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        fixture.Clock.UtcNow = SundayMorning;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿Abre mañana?", null);

        Assert.Equal($"Ahora estamos cerrados: abrimos mañana a las 9:00.\n\n{Week}\n\nMira el menú aquí: {MenuLink}", reply);
    }

    [Fact]
    public async Task ClosedDay_ShouldGiveItsReason_AndTheSpecialDaysAhead()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, Shop());
        await SeedAsync(fixture, tenant, Christmas, Christmas, Navidad);
        await SeedAsync(fixture, tenant, new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31),
            SpecialDayRule.Hours([Opening("09:00", "18:00")], "Fin de año"));
        await SeedAsync(fixture, tenant, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), SpecialDayRule.Closed("Año Nuevo"));
        fixture.Clock.UtcNow = ChristmasNoon;

        var reply = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Están abiertos hoy?", null);

        Assert.Equal(
            "Estamos cerrados por Navidad: abrimos mañana a las 9:00.\n\n" +
            $"{Week}\n\n" +
            "Días especiales:\nJueves 31 de diciembre: 9:00 a 18:00 (Fin de año)\nViernes 1 de enero: cerrado (Año Nuevo)\n\n" +
            $"Mira el menú aquí: {MenuLink}",
            reply);
    }

    [Fact]
    public async Task SpecialHoursToday_ShouldSayWhenItClosesAndWhy()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, Shop());
        await SeedAsync(fixture, tenant, ChristmasEve, ChristmasEve, Nochebuena);
        await SeedAsync(fixture, tenant, Christmas, Christmas, Navidad);
        fixture.Clock.UtcNow = ChristmasEveNoon;

        var reply = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Hasta qué hora abren hoy?", null);

        Assert.Equal(
            "Hoy tenemos horario especial por Nochebuena: cerramos a las 18:00.\n\n" +
            $"{Week}\n\n" +
            "Días especiales:\nHoy: 9:00 a 18:00 (Nochebuena)\nViernes 25 de diciembre: cerrado (Navidad)\n\n" +
            $"Mira el menú y haz tu pedido aquí: {MenuLink}",
            reply);
    }

    [Fact]
    public async Task SpecialHoursUnderWay_ShouldTellUntilWhenTheyLast_AndTodaysSecondOpening()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, Shop());
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 12), new DateOnly(2026, 11, 7),
            SpecialDayRule.Hours([Opening("10:00", "14:00")], "Remodelación"));
        await using var other = SalesTestFixture.Create();
        var inventory = await BusinessAsync(other, Shop());
        await SeedAsync(other, inventory, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19),
            SpecialDayRule.Hours([Opening("09:00", "14:00"), Opening("17:00", "20:00")], "Inventario"));

        // Friday 16 October at noon, five days into the remodeling; Monday 19 at noon, the inventory day.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
        var duringRemodeling = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Qué horario tienen mañana?", null);
        other.Clock.UtcNow = new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);
        var onInventoryDay = await Replies(other).BuildReplyAsync(inventory.Id, "¿Qué horario tienen?", null);

        Assert.Equal(
            "Hoy tenemos horario especial por Remodelación: cerramos a las 14:00.\n\n" +
            $"{Week}\n\n" +
            "Días especiales:\nHasta el sábado 7 de noviembre: 10:00 a 14:00 (Remodelación)\n\n" +
            $"Mira el menú y haz tu pedido aquí: {MenuLink}",
            duringRemodeling);
        Assert.Equal(
            "Hoy tenemos horario especial por Inventario: cerramos a las 14:00.\n\n" +
            $"{Week}\n\n" +
            "Días especiales:\nHoy: 9:00 a 14:00 y 17:00 a 20:00 (Inventario)\n\n" +
            $"Mira el menú y haz tu pedido aquí: {MenuLink}",
            onInventoryDay);
    }

    [Fact]
    public async Task SpecialDays_ShouldBeAtMostFour_WithinTwoWeeks()
    {
        var sundayNoon = new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);

        await using var many = SalesTestFixture.Create();
        var crowded = await BusinessAsync(many, Shop());
        foreach (var (day, reason) in new[] { (12, "Puente"), (14, "Inventario"), (16, "Junta"), (20, "Pintura"), (24, "Quinto") })
            await SeedAsync(many, crowded, new DateOnly(2026, 10, day), new DateOnly(2026, 10, day), SpecialDayRule.Closed(reason));
        many.Clock.UtcNow = sundayNoon;

        await using var edge = SalesTestFixture.Create();
        var sparse = await BusinessAsync(edge, Shop());
        await SeedAsync(edge, sparse, new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 25), SpecialDayRule.Closed("A dos semanas"));
        await SeedAsync(edge, sparse, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 26), SpecialDayRule.Closed("Más adelante"));
        edge.Clock.UtcNow = sundayNoon;

        var first = await Replies(many).BuildReplyAsync(crowded.Id, "horario", null);
        var second = await Replies(edge).BuildReplyAsync(sparse.Id, "horario", null);

        Assert.Contains(
            "Días especiales:\nLunes 12 de octubre: cerrado (Puente)\nMiércoles 14 de octubre: cerrado (Inventario)\n" +
            "Viernes 16 de octubre: cerrado (Junta)\nMartes 20 de octubre: cerrado (Pintura)\n\n",
            first);
        Assert.DoesNotContain("Quinto", first);
        Assert.Contains("Días especiales:\nDomingo 25 de octubre: cerrado (A dos semanas)\n\n", second);
        Assert.DoesNotContain("Más adelante", second);
    }

    [Theory]
    [InlineData("¿A qué hora llega mi pedido?")]
    [InlineData("¿A qué horas pasan por el pedido?")]
    public async Task OrderQuestionWithAnHour_ShouldGetTheCatalogReply_WithoutTheHours(string message)
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop(), products: Products);
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, message, null);

        Assert.Equal($"{Products}\nMira el menú y haz tu pedido aquí: {MenuLink}", reply);
    }

    [Theory]
    [InlineData("¿Quién me atiende?")]
    [InlineData("¿Me cierras la cuenta?")]
    [InlineData("¿Ya van a abrir?")]
    public async Task WordsThatOnlySoundLikeTheHours_ShouldNotGetThem(string message)
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        var agent = await fixture.Context.AiAgents.SingleAsync();
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, message, null);

        Assert.Equal(agent.FallbackMessage, reply);
    }

    [Fact]
    public async Task GreetingWithTheHours_ShouldWelcomeFirst_AndAskForTheNameLast()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        var agent = await fixture.Context.AiAgents.SingleAsync();
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "Hola, ¿a qué hora abren?", null);

        Assert.Equal(
            $"{agent.WelcomeMessage}\n\n" +
            $"Ahora estamos abiertos: cerramos a las 20:00.\n\n{Week}\n\n" +
            $"Mira el menú y haz tu pedido aquí: {MenuLink}\n\n" +
            "¿Me compartes tu nombre?",
            reply);
    }

    [Fact]
    public async Task WhereAndWhen_ShouldAddTheDescriptionAfterTheHours()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿Dónde están y a qué hora abren?", null);

        Assert.Equal(
            $"Ahora estamos abiertos: cerramos a las 20:00.\n\n{Week}\n\n{Description}\n\nMira el menú y haz tu pedido aquí: {MenuLink}",
            reply);
    }

    [Fact]
    public async Task IsTheMenuOpen_ShouldGetTheHoursAndTheMenu()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿El menú está abierto?", null);

        Assert.Equal($"Ahora estamos abiertos: cerramos a las 20:00.\n\n{Week}\n\nMira el menú y haz tu pedido aquí: {MenuLink}", reply);
    }

    [Fact]
    public async Task CatalogQuestion_WhileClosed_ShouldNotInviteAnOrderTheMenuWouldRefuse()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        fixture.Clock.UtcNow = SundayMorning;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "me pasas el menu", null);

        Assert.Equal($"Mira el menú aquí: {MenuLink}\nAhora estamos cerrados: abrimos mañana a las 9:00.", reply);
    }

    [Fact]
    public async Task WithoutHours_ShouldAnswerWithTheDescription_OrTheFallback()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, weekly: null);
        var agent = await fixture.Context.AiAgents.SingleAsync();
        fixture.Clock.UtcNow = SundayMorning;

        var withDescription = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿A qué hora cierran?", null);
        agent.UpdateGeneral(agent.Name, agent.Role, agent.Personality, agent.Language, agent.Tone, agent.BusinessName, null, null);
        await fixture.Context.SaveChangesAsync();
        var withoutDescription = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿A qué hora cierran?", null);

        // Without a schedule the business is never said to be closed.
        Assert.Equal(Description, withDescription);
        Assert.Equal(agent.FallbackMessage, withoutDescription);
    }

    [Fact]
    public async Task WithoutWeeklyHours_ShouldTellOnlyTheSpecialDays_AndAddTheDescription()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, weekly: null);
        await SeedAsync(fixture, tenant, Christmas, Christmas, Navidad);

        fixture.Clock.UtcNow = new DateTimeOffset(2026, 12, 20, 18, 0, 0, TimeSpan.Zero);
        var before = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Qué horario tienen?", null);
        fixture.Clock.UtcNow = ChristmasNoon;
        var christmas = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Qué horario tienen?", null);

        // Open all day is no hours: no week, no «24 horas», no «cerramos a medianoche».
        Assert.Equal(
            $"Días especiales:\nViernes 25 de diciembre: cerrado (Navidad)\n\n{Description}\n\nMira el menú y haz tu pedido aquí: {MenuLink}",
            before);
        Assert.Equal($"Estamos cerrados por Navidad: abrimos mañana.\n\n{Description}\n\nMira el menú aquí: {MenuLink}", christmas);
        Assert.All(new[] { before, christmas }, reply =>
        {
            Assert.DoesNotContain("Nuestro horario", reply);
            Assert.DoesNotContain("24 horas", reply);
        });
    }

    [Fact]
    public async Task WithoutWeeklyHours_SpecialHoursUntilOrPastMidnight_ShouldSayWhenTheyClose()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, weekly: null);
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16),
            SpecialDayRule.Hours([Opening("18:00", "02:00")], "Noche mexicana"));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 23),
            SpecialDayRule.Hours([Opening("09:00", "00:00")], null));

        // Friday 16 October at 20:00 and Friday 23 at noon, in Mexico City.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 17, 2, 0, 0, TimeSpan.Zero);
        var night = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Siguen abiertos?", null);
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
        var untilMidnight = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Siguen abiertos?", null);

        // The days after are open all day by default: that is no hours, and never «abiertos las 24 horas».
        Assert.Equal(
            "Hoy tenemos horario especial por Noche mexicana: cerramos mañana a las 2:00.\n\n" +
            "Días especiales:\nHoy: 18:00 a 2:00 (Noche mexicana)\nViernes 23 de octubre: 9:00 a medianoche\n\n" +
            $"{Description}\n\nMira el menú y haz tu pedido aquí: {MenuLink}",
            night);
        Assert.Equal(
            "Hoy tenemos horario especial: cerramos a medianoche.\n\n" +
            "Días especiales:\nHoy: 9:00 a medianoche\n\n" +
            $"{Description}\n\nMira el menú y haz tu pedido aquí: {MenuLink}",
            untilMidnight);
    }

    [Fact]
    public async Task CutTextOfTheBusiness_ShouldNotSplitAnEmoji()
    {
        // The cut would land between the two halves of 👋: one alone reaches the customer as «�».
        var system = new string('s', 28);
        var welcome = new string('a', 1266) + "👋 bienvenidos a la fonda";
        var composed = new AutoReplyComposer().Owner(welcome).System(system).Compose();

        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, weekly: null);
        var agent = await fixture.Context.AiAgents.SingleAsync();
        var description = new string('b', 1296) + "👋 estamos en el centro";
        agent.UpdateGeneral(agent.Name, agent.Role, agent.Personality, agent.Language, agent.Tone, agent.BusinessName, description, null);
        await fixture.Context.SaveChangesAsync();
        var limited = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿Dónde están?", null);

        Assert.Equal($"{new string('a', 1266)}...\n\n{system}", composed);
        Assert.Equal($"{new string('b', 1296)}...", limited);
    }

    [Fact]
    public async Task LongWelcome_ShouldGiveWay_AndKeepTheHoursWhole()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, Shop());
        var agent = await fixture.Context.AiAgents.SingleAsync();
        var welcome = string.Concat(Enumerable.Repeat("Bienvenidos a nuestra fonda. ", 70))[..2000];
        agent.UpdateMessages(welcome, agent.FallbackMessage, agent.ClosingMessage);
        await fixture.Context.SaveChangesAsync();
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "Hola, ¿qué horario tienen?", null);

        Assert.NotNull(reply);
        Assert.True(reply.Length <= AutoReplyComposer.MaxLength, $"{reply.Length} characters");
        Assert.StartsWith("Bienvenidos a nuestra fonda.", reply);
        Assert.EndsWith(
            $"...\n\nAhora estamos abiertos: cerramos a las 20:00.\n\n{Week}\n\n" +
            $"Mira el menú y haz tu pedido aquí: {MenuLink}\n\n¿Me compartes tu nombre?",
            reply);
    }

    [Fact]
    public async Task LongProducts_ShouldKeepTheMenuLink()
    {
        await using var fixture = SalesTestFixture.Create();
        var products = string.Concat(Enumerable.Repeat("Tacos de pastor $25, de suadero $25. ", 120))[..4000];
        await BusinessAsync(fixture, Shop(), products: products);
        fixture.Clock.UtcNow = MondayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿cuánto cuestan los tacos?", null);

        Assert.NotNull(reply);
        Assert.True(reply.Length <= AutoReplyComposer.MaxLength, $"{reply.Length} characters");
        Assert.StartsWith("Tacos de pastor $25", reply);
        Assert.EndsWith($"...\nMira el menú y haz tu pedido aquí: {MenuLink}", reply);
    }

    [Fact]
    public async Task WorstCase_ShouldFitWithoutCuttingAnything()
    {
        // Two openings every day, all different; a closed period today and four special days, all with 40-character
        // reasons; the welcome and the three questions for the customer's data.
        var reasons = new[]
        {
            "Cumpleaños de la fundadora y del negocio", "Capacitación de todo el equipo de cocina",
            "Remodelación de las cocinas y el comedor", "Juntas de todo el equipo con proveedores",
            "Asamblea anual de socios y proveedores 2"
        };
        Assert.All(reasons, reason => Assert.Equal(SpecialDayRule.MaxReasonLength, reason.Length));
        var week = OpeningHours.Create(Enum.GetValues<DayOfWeek>().SelectMany(day => new[]
        {
            Open(day, "10:00", "14:00"),
            Open(day, "16:00", $"22:{(int)day * 5:00}")
        }));
        DailyOpening[] twoOpenings = [Opening("10:00", "14:00"), Opening("16:00", "23:30")];
        var sundayNoon = new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);

        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, week);
        var agent = await fixture.Context.AiAgents.SingleAsync();
        agent.UpdateDataCapture(askForName: true, askForPhone: true, askForEmail: true, askForAddress: true, customQuestions: null);
        await fixture.Context.SaveChangesAsync();
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 17), SpecialDayRule.Closed(reasons[0]));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19), SpecialDayRule.Hours(twoOpenings, reasons[1]));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 20), SpecialDayRule.Hours(twoOpenings, reasons[2]));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21), SpecialDayRule.Hours(twoOpenings, reasons[3]));
        await SeedAsync(fixture, tenant, new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 25), SpecialDayRule.Hours(twoOpenings, reasons[4]));
        fixture.Clock.UtcNow = sundayNoon;

        var reply = await Replies(fixture).BuildReplyAsync(tenant.Id, "Hola, ¿cuál es su horario?", null);
        var hours = await BusinessHours.ReadAsync(fixture.Context, tenant.Id, week, "America/Mexico_City", sundayNoon, CancellationToken.None);
        var hoursText = BusinessHoursText.ForChat(hours!)!;

        Assert.NotNull(reply);
        Assert.True(hoursText.Length < 950, $"{hoursText.Length} characters of hours");
        Assert.True(reply.Length <= AutoReplyComposer.MaxLength, $"{reply.Length} characters");
        Assert.DoesNotContain("...", reply);
        Assert.Contains(hoursText, reply);
        Assert.StartsWith(
            $"Estamos cerrados por {reasons[0]}: abrimos el domingo 18 de octubre a las 10:00.\n\nNuestro horario:\n" +
            "Lunes: 10:00 a 14:00 y 16:00 a 22:05\n",
            hoursText);
        Assert.Equal(7, hoursText.Split('\n').Count(line => line.Contains(" a 14:00 y 16:00 a 22:")));
        Assert.EndsWith(
            $"Del jueves 22 de octubre al domingo 25 de octubre: 10:00 a 14:00 y 16:00 a 23:30 ({reasons[4]})", hoursText);
        Assert.EndsWith(
            "¿Me compartes tu nombre? Si gustas, compártenos también tu correo. Si es para entrega, compártenos tu dirección.",
            reply);
    }

    [Fact]
    public async Task HoursThatCannotBeRead_ShouldGiveTheReplyOfBefore_AndLogOneError()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, Shop());
        var hoursLog = new RecordingLogger();
        var catalogLog = new RecordingLogger();

        var hoursReply = await Replies(fixture, new BrokenClock(), hoursLog).BuildReplyAsync(tenant.Id, "¿A qué hora cierran?", null);
        var catalogReply = await Replies(fixture, new BrokenClock(), catalogLog).BuildReplyAsync(tenant.Id, "me pasas el menu", null);
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.SetOpeningHours(null);
        await fixture.Context.SaveChangesAsync();
        var withoutSchedule = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿A qué hora cierran?", null);

        Assert.Equal(withoutSchedule, hoursReply);
        Assert.Equal(Description, hoursReply);
        Assert.Equal($"Mira el menú y haz tu pedido aquí: {MenuLink}", catalogReply);
        foreach (var log in new[] { hoursLog, catalogLog })
        {
            var entry = Assert.Single(log.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.IsType<InvalidOperationException>(entry.Exception);
            // The business only: never the customer's phone or words.
            Assert.Equal($"Business hours for the automatic reply failed for tenant {tenant.Id}", entry.Message);
        }
    }

    [Fact]
    public async Task TwoBusinesses_ShouldEachTellTheirOwnHours()
    {
        await using var fixture = SalesTestFixture.Create();
        var closed = await BusinessAsync(fixture, Shop());
        await SeedAsync(fixture, closed, Christmas, Christmas, Navidad);
        var open = await BusinessAsync(fixture, Shop(), slug: "fonda-vecina");
        fixture.Clock.UtcNow = ChristmasNoon;

        // Each message is answered in the scope of the business it was sent to.
        var neighbor = await Replies(fixture).BuildReplyAsync(open.Id, "¿Abren hoy?", null);
        fixture.TenantService.SetTenant(closed.Id);
        var own = await Replies(fixture).BuildReplyAsync(closed.Id, "¿Abren hoy?", null);

        Assert.StartsWith("Ahora estamos abiertos: cerramos a las 20:00.\n\n", neighbor);
        Assert.DoesNotContain("Navidad", neighbor);
        Assert.EndsWith($"{AppUrl}/m/fonda-vecina", neighbor);
        Assert.StartsWith("Estamos cerrados por Navidad: abrimos mañana a las 9:00.\n\n", own);
    }

    [Fact]
    public async Task TijuanaWhenTheClocksGoBack_ShouldAgreeWithTheMenu()
    {
        // Saturday 31 October until 1:30. On Sunday 1 November Tijuana goes from 2:00 PDT back to 1:00 PST: 1:15 comes
        // twice, and the business closes at the first 1:30.
        await using var fixture = SalesTestFixture.Create();
        var saturdayNight = OpeningHours.Create([Open(DayOfWeek.Saturday, "18:00", "01:30")]);
        var tenant = await BusinessAsync(fixture, saturdayNight, timeZone: "America/Tijuana");
        var menu = new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock);

        fixture.Clock.UtcNow = new DateTimeOffset(2026, 11, 1, 8, 15, 0, TimeSpan.Zero);
        var firstMenu = (await menu.Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None))!.OpeningHours!;
        var firstReply = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Siguen abiertos?", null);
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 11, 1, 9, 15, 0, TimeSpan.Zero);
        var repeatedMenu = (await menu.Handle(new GetPublicMenuQuery(tenant.Slug), CancellationToken.None))!.OpeningHours!;
        var repeatedReply = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Siguen abiertos?", null);

        Assert.Equal((true, "cierra a la 1:30"), (firstMenu.IsOpen, firstMenu.StatusDetail));
        Assert.StartsWith("Ahora estamos abiertos: cerramos a la 1:30.\n\n", firstReply);
        Assert.Equal((false, "abre el sábado 7 a las 18:00"), (repeatedMenu.IsOpen, repeatedMenu.StatusDetail));
        Assert.StartsWith("Ahora estamos cerrados: abrimos el sábado 7 a las 18:00.\n\n", repeatedReply);
        Assert.Contains("Nuestro horario:\nLunes a viernes: cerrado\nSábado: 18:00 a 1:30\nDomingo: cerrado", repeatedReply);
    }

    /// <summary>A business with its online menu taking orders, its hours and an active agent with a description.</summary>
    private static async Task<Tenant> BusinessAsync(
        SalesTestFixture fixture, OpeningHours? weekly, string? products = null, string slug = "demo", string? timeZone = null)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.SetOpeningHours(weekly);
        if (timeZone is not null)
            settings.SetTimeZone(timeZone);
        var agent = new AiAgent(tenant.Id, "Respuestas", "Atención");
        agent.UpdateGeneral("Respuestas", "Atención", "Amable", "Español", "Amigable", tenant.Name, Description, products);
        agent.UpdateAiConfiguration("keywords", 0.4m, 800, false, strictMode: true);
        fixture.Context.AiAgents.Add(agent);
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A period saved as it was marked, on its first day.</summary>
    private static async Task SeedAsync(SalesTestFixture fixture, Tenant tenant, DateOnly from, DateOnly to, SpecialDayRule rule)
    {
        fixture.Context.SpecialDays.AddRange(new SpecialDayCalendar(tenant.Id, from, []).Add(from, to, rule).Added);
        await fixture.Context.SaveChangesAsync();
    }

    private static AiAgentAutoReplyService Replies(
        SalesTestFixture fixture, IDateTimeProvider? clock = null, ILogger<AiAgentAutoReplyService>? logger = null)
        => new(fixture.Context, new Links(AppUrl), clock ?? fixture.Clock, logger);

    /// <summary>A shop open Monday to Saturday, 9:00 to 20:00.</summary>
    private static OpeningHours Shop() => OpeningHours.Create(MondayToSaturday.Select(day => Open(day, "09:00", "20:00")));

    private static OpeningInterval Open(DayOfWeek day, string opens, string closes)
        => new(day, TimeOnly.Parse(opens, CultureInfo.InvariantCulture), TimeOnly.Parse(closes, CultureInfo.InvariantCulture));

    private static DailyOpening Opening(string opens, string closes)
        => new(TimeOnly.Parse(opens, CultureInfo.InvariantCulture), TimeOnly.Parse(closes, CultureInfo.InvariantCulture));

    private sealed record Links(string? BaseUrl) : IAppLinks;

    private sealed class BrokenClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => throw new InvalidOperationException("The clock is not available.");
    }

    private sealed class RecordingLogger : ILogger<AiAgentAutoReplyService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}

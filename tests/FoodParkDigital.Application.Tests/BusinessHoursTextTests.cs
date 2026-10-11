using System.Globalization;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.Hours;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Every word the public menu, its orders and the WhatsApp replies say about the hours, written by the server: the chip,
/// its detail, the notices, the message of a refused order and the reply about the hours. A closing from the weekly
/// hours reads as before (0 and 1 days are byte-identical to what the menu said until now); a special day adds its
/// reason; and a business without weekly hours never has its whole-day default worded as hours.
/// </summary>
public class BusinessHoursTextTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly TimeZoneInfo MexicoCity = TenantDateTimeResolver.Resolve("America/Mexico_City");

    private static readonly SpecialDayRule Navidad = SpecialDayRule.Closed("Navidad");
    private static readonly SpecialDayRule Nochebuena = SpecialDayRule.Hours([Opening("09:00", "18:00")], "Nochebuena");
    private static readonly SpecialDayRule Vacaciones = SpecialDayRule.Closed("Vacaciones");

    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    [Theory]
    [InlineData("2026-12-24", "2026-12-24", "hoy")]
    [InlineData("2026-12-25", "2026-12-24", "mañana")]
    [InlineData("2026-12-26", "2026-12-24", "el sábado 26")]
    [InlineData("2026-12-30", "2026-12-24", "el miércoles 30")]
    [InlineData("2027-01-02", "2026-12-28", "el sábado 2 de enero")]
    [InlineData("2026-12-31", "2026-12-24", "el jueves 31 de diciembre")]
    [InlineData("2027-01-05", "2026-12-24", "el martes 5 de enero")]
    [InlineData("2027-06-22", "2026-12-24", "el martes 22 de junio")]
    [InlineData("2027-06-23", "2026-12-24", "el miércoles 23 de junio de 2027")]
    public void DayWords_ShouldNameTheDate_AsFarAsItNeeds(string date, string today, string expected)
    {
        Assert.Equal(expected, BusinessHoursText.DayWords(Date(date), Date(today)));
    }

    [Theory]
    [InlineData("2026-12-24T16:30", "2026-12-24", "hoy a las 16:30")]
    [InlineData("2026-12-25T09:00", "2026-12-24", "mañana a las 9:00")]
    [InlineData("2026-12-26T00:00", "2026-12-24", "el sábado 26")]
    [InlineData("2027-01-05T09:00", "2026-12-24", "el martes 5 de enero a las 9:00")]
    // One o'clock is singular: «a la 1:00», as «a la una».
    [InlineData("2026-12-25T01:00", "2026-12-24", "mañana a la 1:00")]
    [InlineData("2026-12-24T01:59", "2026-12-24", "hoy a la 1:59")]
    [InlineData("2026-12-25T00:30", "2026-12-24", "mañana a las 0:30")]
    [InlineData("2026-12-24T13:00", "2026-12-24", "hoy a las 13:00")]
    public void OpenWhen_ShouldDropTheTime_OfAnOpeningAtMidnight(string at, string today, string expected)
    {
        Assert.Equal(expected, BusinessHoursText.OpenWhen(At(at), Date(today)));
    }

    [Theory]
    [InlineData("2026-12-24T20:00", "2026-12-24", "a las 20:00")]
    [InlineData("2026-12-25T00:00", "2026-12-24", "a medianoche")]
    [InlineData("2026-12-25T02:00", "2026-12-24", "mañana a las 2:00")]
    [InlineData("2026-12-26T00:00", "2026-12-24", "mañana a medianoche")]
    [InlineData("2026-12-25T00:00", "2026-12-18", "el jueves 24 a medianoche")]
    [InlineData("2026-12-25T01:30", "2026-12-24", "mañana a la 1:30")]
    [InlineData("2026-12-24T01:00", "2026-12-24", "a la 1:00")]
    [InlineData("2026-12-24T13:30", "2026-12-24", "a las 13:30")]
    public void CloseWhen_ShouldEndTheDayBefore_AtMidnight(string at, string today, string expected)
    {
        Assert.Equal(expected, BusinessHoursText.CloseWhen(At(at), Date(today)));
    }

    [Fact]
    public void ClosedByTheWeeklyHours_ShouldReadAsBefore()
    {
        var sunday = Hours(Shop(), At("2026-10-04T11:00"));
        var mondayMorning = Hours(Shop(), At("2026-10-05T07:00"));
        var saturdayNight = Hours(Shop(), At("2026-10-03T21:00"));

        Assert.Equal(
            ("Cerrado", "abre mañana a las 9:00",
                "Fuera de horario no recibimos pedidos en línea: vuelve mañana a las 9:00. Mientras, puedes ver el menú.",
                "Ahora está cerrado: abre mañana a las 9:00."),
            Texts(sunday));
        Assert.Equal(
            ("Cerrado", "abre hoy a las 9:00",
                "Fuera de horario no recibimos pedidos en línea: vuelve hoy a las 9:00. Mientras, puedes ver el menú.",
                "Ahora está cerrado: abre hoy a las 9:00."),
            Texts(mondayMorning));
        // Two days or more ahead, the date and not only its weekday.
        Assert.Equal("abre el lunes 5 a las 9:00", BusinessHoursText.StatusDetail(saturdayNight, seatedOverride: false));
        Assert.Equal("Ahora está cerrado: abre el lunes 5 a las 9:00.", BusinessHoursText.ClosedMessage(saturdayNight));
        Assert.Null(BusinessHoursText.UpcomingNotice(sunday));
    }

    [Fact]
    public void Open_ShouldSayWhenItCloses()
    {
        var monday = Hours(Shop(), At("2026-10-05T12:00"));
        var bar = Hours(Weekly(Open(DayOfWeek.Friday, "18:00", "02:00")), At("2026-10-09T20:00"));
        var untilMidnight = Hours(Weekly(Open(DayOfWeek.Monday, "13:00", "00:00")), At("2026-10-05T15:00"));

        Assert.Equal(("Abierto", "cierra a las 20:00", (string?)null), (Label(monday), Detail(monday), Notice(monday)));
        Assert.Equal("cierra mañana a las 2:00", Detail(bar));
        Assert.Equal("cierra a medianoche", Detail(untilMidnight));
    }

    [Fact]
    public void OneOClock_ShouldReadInTheSingular_EverywhereTheTimeIsSaid()
    {
        var bar = Hours(Weekly(Open(DayOfWeek.Friday, "18:00", "01:30")), At("2026-10-09T23:00"));
        var lateOpening = Hours(Weekly(Open(DayOfWeek.Saturday, "01:00", "05:00")), At("2026-10-09T20:00"));

        Assert.Equal(("Abierto", "cierra mañana a la 1:30"), (Label(bar), Detail(bar)));
        Assert.Equal("Ahora estamos abiertos: cerramos mañana a la 1:30.", Chat(bar));
        Assert.Equal(
            ("Cerrado", "abre mañana a la 1:00",
                "Fuera de horario no recibimos pedidos en línea: vuelve mañana a la 1:00. Mientras, puedes ver el menú.",
                "Ahora está cerrado: abre mañana a la 1:00."),
            Texts(lateOpening));
        Assert.Equal("Ahora estamos cerrados: abrimos mañana a la 1:00.", Chat(lateOpening));
    }

    [Fact]
    public void OpenAllTheTime_ShouldSay24Hours_UntilAClosingIsWithinTheWeek()
    {
        var always = Weekly(Enum.GetValues<DayOfWeek>().Select(day => Open(day, "00:00", "00:00")).ToArray());

        var withoutClosing = Hours(always, At("2026-12-10T12:00"));
        var weekBefore = Hours(always, At("2026-12-17T12:00"), Special(Date("2026-12-25"), Navidad));
        var sixDaysBefore = Hours(always, At("2026-12-18T12:00"), Special(Date("2026-12-25"), Navidad));

        Assert.Equal(("Abierto las 24 horas", (string?)null), (Label(withoutClosing), Detail(withoutClosing)));
        Assert.Equal(("Abierto las 24 horas", (string?)null), (Label(weekBefore), Detail(weekBefore)));
        Assert.Equal(("Abierto", "cierra el jueves 24 a medianoche"), (Label(sixDaysBefore), Detail(sixDaysBefore)));
    }

    [Fact]
    public void ClosedDay_ShouldGiveItsReason_OrSayItIsASpecialDay()
    {
        var christmas = Hours(Shop(), At("2026-12-25T12:00"), Special(Date("2026-12-25"), Navidad));
        var withoutReason = Hours(Shop(), At("2026-12-25T12:00"), Special(Date("2026-12-25"), SpecialDayRule.Closed(null)));

        Assert.Equal(
            ("Cerrado", "por Navidad · abre mañana a las 9:00",
                "No recibimos pedidos en línea por Navidad: vuelve mañana a las 9:00. Mientras, puedes ver el menú.",
                "Está cerrado por Navidad: abre mañana a las 9:00."),
            Texts(christmas));
        Assert.Equal(
            ("Cerrado", "día especial · abre mañana a las 9:00",
                "Hoy no abrimos: vuelve mañana a las 9:00. Mientras, puedes ver el menú.",
                "Hoy está cerrado: abre mañana a las 9:00."),
            Texts(withoutReason));
    }

    [Fact]
    public void ClosedDay_ShouldKeepItsReasonPastMidnight_WithoutSayingTodayIsClosed()
    {
        // A bar open every day until 2:00, closed on Christmas: at 0:30 on the 26th, Christmas still closes it.
        var bar = Weekly(Enum.GetValues<DayOfWeek>().Select(day => Open(day, "18:00", "02:00")).ToArray());
        var christmasNight = Hours(bar, At("2026-12-26T00:30"), Special(Date("2026-12-25"), Navidad));
        var withoutReason = Hours(bar, At("2026-12-26T00:30"), Special(Date("2026-12-25"), SpecialDayRule.Closed(null)));

        Assert.Equal(
            ("Cerrado", "por Navidad · abre hoy a las 18:00",
                "No recibimos pedidos en línea por Navidad: vuelve hoy a las 18:00. Mientras, puedes ver el menú.",
                "Está cerrado por Navidad: abre hoy a las 18:00."),
            Texts(christmasNight));
        Assert.Equal("Estamos cerrados por Navidad: abrimos hoy a las 18:00.", Chat(christmasNight));
        // The 26th is not the closed day: no «Hoy no abrimos» before opening today.
        Assert.Equal(
            ("Cerrado", "día especial · abre hoy a las 18:00",
                "Fuera de horario no recibimos pedidos en línea: vuelve hoy a las 18:00. Mientras, puedes ver el menú.",
                "Ahora está cerrado: abre hoy a las 18:00."),
            Texts(withoutReason));
        Assert.Equal("Ahora estamos cerrados: abrimos hoy a las 18:00.", Chat(withoutReason));
    }

    [Fact]
    public void SpecialHours_ShouldGiveTheirReason_WhileTheySetTheClosing()
    {
        var withReason = Hours(Shop(), At("2026-12-24T12:00"), Special(Date("2026-12-24"), Nochebuena));
        var withoutReason = Hours(Shop(), At("2026-12-24T12:00"),
            Special(Date("2026-12-24"), SpecialDayRule.Hours([Opening("09:00", "18:00")], null)));

        Assert.Equal(("Abierto", "cierra a las 18:00 por Nochebuena"), (Label(withReason), Detail(withReason)));
        Assert.Equal("horario especial · cierra a las 18:00", Detail(withoutReason));
    }

    [Fact]
    public void AfterSpecialHours_BeforeAClosedDay_ShouldGiveTheReasonOfTheClosedDay()
    {
        // The owner's line on Christmas Eve after 18:00: the next opening is on the 26th because of Christmas.
        var eve = Hours(Shop(), At("2026-12-24T19:00"), Special(Date("2026-12-24"), Nochebuena), Special(Date("2026-12-25"), Navidad));
        var eveWithoutReason = Hours(Shop(), At("2026-12-24T19:00"),
            Special(Date("2026-12-24"), SpecialDayRule.Hours([Opening("09:00", "18:00")], null)));

        Assert.Equal(
            ("Cerrado", "por Navidad · abre el sábado 26 a las 9:00",
                "No recibimos pedidos en línea por Navidad: vuelve el sábado 26 a las 9:00. Mientras, puedes ver el menú.",
                "Está cerrado por Navidad: abre el sábado 26 a las 9:00."),
            Texts(eve));
        Assert.Equal(
            ("Cerrado", "horario especial · abre mañana a las 9:00",
                "Fuera de horario no recibimos pedidos en línea: vuelve mañana a las 9:00. Mientras, puedes ver el menú.",
                "Ahora está cerrado: abre mañana a las 9:00."),
            Texts(eveWithoutReason));
    }

    [Fact]
    public void LongClosing_ShouldNameTheMonthOfTheNextOpening()
    {
        var vacations = Hours(Shop(), At("2026-12-28T12:00"), Special(Date("2026-12-21"), Date("2027-01-04"), Vacaciones));

        Assert.Equal("por Vacaciones · abre el martes 5 de enero a las 9:00", Detail(vacations));
        Assert.Equal("Está cerrado por Vacaciones: abre el martes 5 de enero a las 9:00.", BusinessHoursText.ClosedMessage(vacations));
    }

    [Fact]
    public void SeatedCustomer_ShouldReadThatTheTableStillOrders_AndGetNoNotice()
    {
        var sunday = Hours(Shop(), At("2026-10-04T11:00"));

        Assert.Equal("Cerrado", Label(sunday));
        Assert.Equal("tu mesa sí puede pedir", BusinessHoursText.StatusDetail(sunday, seatedOverride: true));
        Assert.Null(BusinessHoursText.ClosedNotice(sunday, seatedOverride: true));
    }

    [Fact]
    public void WithoutWeeklyHours_ShouldWordOnlyItsSpecialDays()
    {
        var before = Hours(null, At("2026-12-22T12:00"), Special(Date("2026-12-25"), Navidad));
        var christmas = Hours(null, At("2026-12-25T12:00"), Special(Date("2026-12-25"), Navidad));

        // Open all day is no hours: no chip, no «cierra el jueves 24 a medianoche».
        Assert.True(before.Status.IsOpen);
        Assert.False(BusinessHoursText.MayWordStatus(before));
        Assert.Equal(((string?)null, (string?)null, (string?)null), (Label(before), Detail(before), Notice(before)));
        Assert.Equal("El viernes 25 cerramos por Navidad.", BusinessHoursText.UpcomingNotice(before));
        // On the special day it is: and it opens again at midnight, a day without a time.
        Assert.Equal(
            ("Cerrado", "por Navidad · abre mañana",
                "No recibimos pedidos en línea por Navidad: vuelve mañana. Mientras, puedes ver el menú.",
                "Está cerrado por Navidad: abre mañana."),
            Texts(christmas));
    }

    [Fact]
    public void WithoutWeeklyHours_SpecialHoursPastMidnight_ShouldSayWhenTheyClose_NotThe24Hours()
    {
        var nocheMexicana = SpecialDayRule.Hours([Opening("18:00", "02:00")], "Noche mexicana");
        var night = Hours(null, At("2026-10-16T20:00"), Special(Date("2026-10-16"), nocheMexicana));
        var untilMidnight = Hours(null, At("2026-10-16T12:00"),
            Special(Date("2026-10-16"), SpecialDayRule.Hours([Opening("09:00", "00:00")], null)));

        Assert.Equal(("Abierto", "cierra mañana a las 2:00 por Noche mexicana"), (Label(night), Detail(night)));
        Assert.Equal("Hoy tenemos horario especial por Noche mexicana: cerramos mañana a las 2:00.", Chat(night));
        Assert.Equal(("Abierto", "horario especial · cierra a medianoche"), (Label(untilMidnight), Detail(untilMidnight)));
        Assert.Equal("Hoy tenemos horario especial: cerramos a medianoche.", Chat(untilMidnight));
        Assert.All(new[] { night, untilMidnight }, hours => Assert.DoesNotContain("24 horas", BusinessHoursText.ForChat(hours)));
    }

    [Fact]
    public void UpcomingNotice_ShouldAnnounceTheNextSpecialDayWithinAWeek()
    {
        var sunday = At("2026-12-20T12:00");

        Assert.Equal("El jueves 24 abrimos de 9:00 a 18:00 por Nochebuena.",
            Upcoming(Hours(Shop(), sunday, Special(Date("2026-12-24"), Nochebuena))));
        Assert.Equal("El jueves 24 abrimos de 9:00 a 14:00 y de 16:00 a 18:00 por Nochebuena.",
            Upcoming(Hours(Shop(), sunday, Special(Date("2026-12-24"),
                SpecialDayRule.Hours([Opening("09:00", "14:00"), Opening("16:00", "18:00")], "Nochebuena")))));
        Assert.Equal("El jueves 24 abrimos de 18:00 a medianoche.",
            Upcoming(Hours(Shop(), sunday, Special(Date("2026-12-24"), SpecialDayRule.Hours([Opening("18:00", "00:00")], null)))));
        Assert.Equal("El jueves 24 abrimos las 24 horas por Maratón.",
            Upcoming(Hours(Shop(), sunday, Special(Date("2026-12-24"), SpecialDayRule.Hours([Opening("00:00", "00:00")], "Maratón")))));
        Assert.Equal("Mañana cerramos.",
            Upcoming(Hours(Shop(), At("2026-12-24T12:00"), Special(Date("2026-12-25"), SpecialDayRule.Closed(null)))));
        Assert.Equal("Del lunes 21 de diciembre al lunes 4 de enero cerramos por Vacaciones.",
            Upcoming(Hours(Shop(), At("2026-12-18T12:00"), Special(Date("2026-12-21"), Date("2027-01-04"), Vacaciones))));
        // A week ahead is the limit; today's own special day is no news.
        Assert.Equal("El domingo 18 de octubre cerramos.",
            Upcoming(Hours(Shop(), At("2026-10-11T12:00"), Special(Date("2026-10-18"), SpecialDayRule.Closed(null)))));
        Assert.Null(Upcoming(Hours(Shop(), At("2026-10-11T12:00"), Special(Date("2026-10-19"), SpecialDayRule.Closed(null)))));
        Assert.Null(Upcoming(Hours(Shop(), At("2026-12-24T12:00"), Special(Date("2026-12-24"), Nochebuena))));
    }

    [Fact]
    public void UpcomingNotice_ShouldAnnounceSpecialHoursUnderWay_UntilTheirLastDay()
    {
        var verano = Special(Date("2026-07-01"), Date("2026-07-31"), SpecialDayRule.Hours([Opening("10:00", "14:00")], "Verano"));
        var nextWeek = Special(Date("2026-07-20"), SpecialDayRule.Closed("Inventario"));

        // From its first day: the days after today keep those hours.
        Assert.Equal("Hasta el viernes 31 de julio abrimos de 10:00 a 14:00 por Verano.",
            Upcoming(Hours(Shop(), At("2026-07-01T12:00"), verano)));
        Assert.Equal("Hasta el viernes 31 de julio abrimos de 10:00 a 14:00 por Verano.",
            Upcoming(Hours(Shop(), At("2026-07-15T12:00"), verano, nextWeek)));
        // Its last day is only today's hours, and a closed period under way already says when it opens.
        Assert.Null(Upcoming(Hours(Shop(), At("2026-07-31T12:00"), verano)));
        Assert.Null(Upcoming(Hours(Shop(), At("2026-12-28T12:00"), Special(Date("2026-12-21"), Date("2027-01-04"), Vacaciones))));
    }

    [Fact]
    public void WithoutANextOpening_ShouldOnlySayItIsClosed()
    {
        // Defensive: with any hours at all the next opening is always found.
        var sunday = Hours(Shop(), At("2026-10-04T11:00"));
        var vacations = Hours(Shop(), At("2026-12-28T12:00"), Special(Date("2026-12-21"), Date("2027-01-04"), Vacaciones));
        var sundayNever = sunday with { Status = sunday.Status with { OpensAt = null } };
        var vacationsNever = vacations with { Status = vacations.Status with { OpensAt = null } };

        Assert.Equal(
            ("Cerrado", (string?)null, "Fuera de horario no recibimos pedidos en línea. Mientras, puedes ver el menú.", "Ahora está cerrado."),
            Texts(sundayNever));
        Assert.Equal(
            ("Cerrado", "por Vacaciones", "No recibimos pedidos en línea por Vacaciones. Mientras, puedes ver el menú.", "Está cerrado por Vacaciones."),
            Texts(vacationsNever));
    }

    [Fact]
    public void ChatStatusLine_ShouldSayItInTheFirstPerson()
    {
        var always = Weekly(Enum.GetValues<DayOfWeek>().Select(day => Open(day, "00:00", "00:00")).ToArray());

        Assert.Equal("Ahora estamos abiertos: cerramos a las 20:00.", Chat(Hours(Shop(), At("2026-10-05T12:00"))));
        Assert.Equal("Ahora estamos abiertos: cerramos a medianoche.",
            Chat(Hours(Weekly(Open(DayOfWeek.Monday, "13:00", "00:00")), At("2026-10-05T15:00"))));
        Assert.Equal("Ahora estamos abiertos: cerramos mañana a las 2:00.",
            Chat(Hours(Weekly(Open(DayOfWeek.Friday, "18:00", "02:00")), At("2026-10-09T20:00"))));
        Assert.Equal("Ahora estamos abiertos las 24 horas.", Chat(Hours(always, At("2026-12-10T12:00"))));
        Assert.Equal("Ahora estamos cerrados: abrimos mañana a las 9:00.", Chat(Hours(Shop(), At("2026-10-04T11:00"))));
        // A week ahead, the date; opening at midnight, the day alone.
        Assert.Equal("Ahora estamos cerrados: abrimos el martes 13 de octubre a las 9:00.",
            Chat(Hours(Weekly(Open(DayOfWeek.Tuesday, "09:00", "14:00")), At("2026-10-06T15:00"))));
        Assert.Equal("Ahora estamos cerrados: abrimos mañana.",
            Chat(Hours(Weekly(Open(DayOfWeek.Tuesday, "00:00", "12:00")), At("2026-10-05T15:00"))));
    }

    [Fact]
    public void ChatStatusLine_ShouldGiveTheReasonOfASpecialDay()
    {
        var always = Weekly(Enum.GetValues<DayOfWeek>().Select(day => Open(day, "00:00", "00:00")).ToArray());
        var inventory = SpecialDayRule.Hours([Opening("00:00", "14:00")], "Inventario");

        Assert.Equal("Hoy tenemos horario especial por Nochebuena: cerramos a las 18:00.",
            Chat(Hours(Shop(), At("2026-12-24T12:00"), Special(Date("2026-12-24"), Nochebuena))));
        Assert.Equal("Hoy tenemos horario especial: cerramos a las 18:00.",
            Chat(Hours(Shop(), At("2026-12-24T12:00"), Special(Date("2026-12-24"), SpecialDayRule.Hours([Opening("09:00", "18:00")], null)))));
        // Tomorrow's special hours set today's closing: not «hoy tenemos horario especial».
        Assert.Equal("Ahora estamos abiertos: cerramos mañana a las 14:00 por Inventario.",
            Chat(Hours(always, At("2026-12-20T12:00"), Special(Date("2026-12-21"), inventory))));
        Assert.Equal("Estamos cerrados por Navidad: abrimos mañana a las 9:00.",
            Chat(Hours(Shop(), At("2026-12-25T12:00"), Special(Date("2026-12-25"), Navidad))));
        Assert.Equal("Estamos cerrados por Navidad: abrimos el sábado 26 a las 9:00.",
            Chat(Hours(Shop(), At("2026-12-24T19:00"), Special(Date("2026-12-24"), Nochebuena), Special(Date("2026-12-25"), Navidad))));
        Assert.Equal("Hoy estamos cerrados: abrimos mañana a las 9:00.",
            Chat(Hours(Shop(), At("2026-12-25T12:00"), Special(Date("2026-12-25"), SpecialDayRule.Closed(null)))));
        Assert.Equal("Estamos cerrados por Vacaciones: abrimos el martes 5 de enero a las 9:00.",
            Chat(Hours(Shop(), At("2026-12-28T12:00"), Special(Date("2026-12-21"), Date("2027-01-04"), Vacaciones))));
    }

    [Fact]
    public void ChatStatusLine_WithoutANextOpening_ShouldOnlySayItIsClosed()
    {
        var sunday = Hours(Shop(), At("2026-10-04T11:00"));
        var vacations = Hours(Shop(), At("2026-12-28T12:00"), Special(Date("2026-12-21"), Date("2027-01-04"), Vacaciones));

        Assert.Equal("Ahora estamos cerrados.", Chat(sunday with { Status = sunday.Status with { OpensAt = null } }));
        Assert.Equal("Estamos cerrados por Vacaciones.", Chat(vacations with { Status = vacations.Status with { OpensAt = null } }));
    }

    [Fact]
    public void ForChat_ShouldTellTheWeekInRunsOfDays_MondayFirst()
    {
        var bar = Weekly(
            Open(DayOfWeek.Monday, "09:00", "14:00"), Open(DayOfWeek.Monday, "16:00", "20:00"),
            Open(DayOfWeek.Tuesday, "09:00", "14:00"), Open(DayOfWeek.Tuesday, "16:00", "20:00"),
            Open(DayOfWeek.Wednesday, "13:00", "00:00"),
            Open(DayOfWeek.Thursday, "00:00", "00:00"),
            Open(DayOfWeek.Friday, "18:00", "02:00"), Open(DayOfWeek.Saturday, "18:00", "02:00"));

        Assert.Equal(
            "Ahora estamos abiertos: cerramos a las 14:00.\n\n" +
            "Nuestro horario:\n" +
            "Lunes y martes: 9:00 a 14:00 y 16:00 a 20:00\n" +
            "Miércoles: 13:00 a medianoche\n" +
            "Jueves: abierto las 24 horas\n" +
            "Viernes y sábado: 18:00 a 2:00\n" +
            "Domingo: cerrado",
            BusinessHoursText.ForChat(Hours(bar, At("2026-10-05T12:00"))));
        Assert.Equal(
            "Ahora estamos abiertos: cerramos a las 20:00.\n\nNuestro horario:\nLunes a sábado: 9:00 a 20:00\nDomingo: cerrado",
            BusinessHoursText.ForChat(Hours(Shop(), At("2026-10-05T12:00"))));
    }

    [Fact]
    public void ForChat_ShouldListTheSpecialDaysOfTheNextTwoWeeks()
    {
        var hours = Hours(Shop(), At("2026-12-18T12:00"),
            Special(Date("2026-12-21"), Date("2027-01-04"), SpecialDayRule.Closed(null)),
            Special(Date("2026-12-19"), SpecialDayRule.Hours([Opening("09:00", "14:00"), Opening("16:00", "18:00")], "Posada")),
            Special(Date("2026-12-20"), SpecialDayRule.Hours([Opening("00:00", "00:00")], "Maratón")));

        Assert.EndsWith(
            "Días especiales:\n" +
            "Sábado 19 de diciembre: 9:00 a 14:00 y 16:00 a 18:00 (Posada)\n" +
            "Domingo 20 de diciembre: abierto las 24 horas (Maratón)\n" +
            "Del lunes 21 de diciembre al lunes 4 de enero: cerrado",
            BusinessHoursText.ForChat(hours));
    }

    [Fact]
    public void ForChat_ShouldTellTheSpecialHoursFromToday()
    {
        var remodelacion = Special(Date("2026-10-12"), Date("2026-11-07"),
            SpecialDayRule.Hours([Opening("10:00", "14:00")], "Remodelación"));
        var inventario = Special(Date("2026-10-19"),
            SpecialDayRule.Hours([Opening("09:00", "14:00"), Opening("17:00", "20:00")], "Inventario"));

        // Under way: until when its hours last, and not only that it closes at 14:00 today.
        Assert.Equal(
            "Hoy tenemos horario especial por Remodelación: cerramos a las 14:00.\n\n" +
            "Nuestro horario:\nLunes a sábado: 9:00 a 20:00\nDomingo: cerrado\n\n" +
            "Días especiales:\nHasta el sábado 7 de noviembre: 10:00 a 14:00 (Remodelación)",
            BusinessHoursText.ForChat(Hours(Shop(), At("2026-10-16T12:00"), remodelacion)));
        // Today only: its second opening too.
        Assert.Equal(
            "Hoy tenemos horario especial por Inventario: cerramos a las 14:00.\n\n" +
            "Nuestro horario:\nLunes a sábado: 9:00 a 20:00\nDomingo: cerrado\n\n" +
            "Días especiales:\nHoy: 9:00 a 14:00 y 17:00 a 20:00 (Inventario)",
            BusinessHoursText.ForChat(Hours(Shop(), At("2026-10-19T12:00"), inventario)));
        // A closed period under way is the status line: it says when we open again.
        Assert.Equal(
            "Estamos cerrados por Vacaciones: abrimos el martes 5 de enero a las 9:00.\n\n" +
            "Nuestro horario:\nLunes a sábado: 9:00 a 20:00\nDomingo: cerrado",
            BusinessHoursText.ForChat(Hours(Shop(), At("2026-12-28T12:00"), Special(Date("2026-12-21"), Date("2027-01-04"), Vacaciones))));
    }

    [Fact]
    public void ForChat_WithoutWeeklyHours_ShouldTellOnlyItsSpecialDays()
    {
        var before = Hours(null, At("2026-12-22T12:00"), Special(Date("2026-12-25"), Navidad));
        var farAhead = Hours(null, At("2026-10-11T12:00"), Special(Date("2026-12-25"), Navidad));
        var christmas = Hours(null, At("2026-12-25T12:00"), Special(Date("2026-12-25"), Navidad));

        // Open all day is no hours: no status, no week.
        Assert.Null(BusinessHoursText.ChatStatusLine(before));
        Assert.Equal("Días especiales:\nViernes 25 de diciembre: cerrado (Navidad)", BusinessHoursText.ForChat(before));
        Assert.Null(BusinessHoursText.ForChat(farAhead));
        Assert.Equal("Estamos cerrados por Navidad: abrimos mañana.", BusinessHoursText.ForChat(christmas));
    }

    /// <summary>The chip, its detail, the notice over the menu and the message of a refused order.</summary>
    private static (string? Label, string? Detail, string? Notice, string Message) Texts(BusinessHoursNow hours)
        => (Label(hours), Detail(hours), Notice(hours), BusinessHoursText.ClosedMessage(hours));

    private static string? Label(BusinessHoursNow hours) => BusinessHoursText.StatusLabel(hours);

    private static string? Detail(BusinessHoursNow hours) => BusinessHoursText.StatusDetail(hours, seatedOverride: false);

    private static string? Notice(BusinessHoursNow hours) => BusinessHoursText.ClosedNotice(hours, seatedOverride: false);

    private static string? Upcoming(BusinessHoursNow hours) => BusinessHoursText.UpcomingNotice(hours);

    private static string? Chat(BusinessHoursNow hours) => BusinessHoursText.ChatStatusLine(hours);

    /// <summary>The hours of a business in Mexico City when its clock reads <paramref name="localNow"/>.</summary>
    private static BusinessHoursNow Hours(OpeningHours? weekly, DateTime localNow, params List<SpecialDay>[] specialDays)
        => BusinessHours.At(
            weekly,
            specialDays.SelectMany(period => period).ToList(),
            MexicoCity,
            new DateTimeOffset(localNow, MexicoCity.GetUtcOffset(localNow)))!;

    /// <summary>A shop open Monday to Saturday, 9:00 to 20:00.</summary>
    private static OpeningHours Shop() => OpeningHours.Create(MondayToSaturday.Select(day => Open(day, "09:00", "20:00")));

    private static OpeningHours Weekly(params OpeningInterval[] intervals) => OpeningHours.Create(intervals);

    private static OpeningInterval Open(DayOfWeek day, string opens, string closes)
        => new(day, TimeOnly.Parse(opens, CultureInfo.InvariantCulture), TimeOnly.Parse(closes, CultureInfo.InvariantCulture));

    private static DailyOpening Opening(string opens, string closes)
        => new(TimeOnly.Parse(opens, CultureInfo.InvariantCulture), TimeOnly.Parse(closes, CultureInfo.InvariantCulture));

    /// <summary>The rows of a period, as saved on its first day.</summary>
    private static List<SpecialDay> Special(DateOnly from, DateOnly to, SpecialDayRule rule)
        => new SpecialDayCalendar(Business, from, []).Add(from, to, rule).Added.ToList();

    private static List<SpecialDay> Special(DateOnly date, SpecialDayRule rule) => Special(date, date, rule);

    private static DateOnly Date(string date) => DateOnly.Parse(date, CultureInfo.InvariantCulture);

    private static DateTime At(string at) => DateTime.Parse(at, CultureInfo.InvariantCulture);
}

using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>What a special day does to its date (Configuración › Negocio › Días especiales): closed, or other hours.</summary>
public class SpecialDayRuleTests
{
    private static DailyOpening Open(string opens, string closes) => new(TimeOnly.Parse(opens), TimeOnly.Parse(closes));

    private static string Rejection(Func<SpecialDayRule> create) => Assert.Throws<DomainException>(() => create()).Message;

    [Fact]
    public void Create_ShouldTakeAClosedDayOrUpToTwoOpenings_SortedByOpeningTime()
    {
        var closed = SpecialDayRule.Closed("Navidad");
        Assert.Equal(SpecialDayKind.Closed, closed.Kind);
        Assert.Empty(closed.Openings);
        Assert.Equal("Navidad", closed.Reason);

        var hours = SpecialDayRule.Hours([Open("16:00", "18:00"), Open("09:00", "14:00")], "Nochebuena");
        Assert.Equal(SpecialDayKind.SpecialHours, hours.Kind);
        Assert.Equal(new[] { Open("09:00", "14:00"), Open("16:00", "18:00") }, hours.Openings);

        // Openings that touch are fine: they join.
        SpecialDayRule.Hours([Open("09:00", "14:00"), Open("14:00", "18:00")], null);
    }

    [Fact]
    public void Create_ShouldRejectAClosedDayWithHoursAndSpecialHoursWithoutOrWithTooManyOpenings()
    {
        Assert.Equal("Un día cerrado no lleva horario.",
            Rejection(() => SpecialDayRule.Create(SpecialDayKind.Closed, [Open("09:00", "14:00")], null)));
        Assert.Equal("Escribe al menos un horario para ese día.", Rejection(() => SpecialDayRule.Hours([], null)));
        Assert.Equal("Un día especial admite como máximo dos horarios.", Rejection(() =>
            SpecialDayRule.Hours([Open("08:00", "10:00"), Open("11:00", "13:00"), Open("15:00", "18:00")], null)));
        Assert.Equal("El tipo de día especial no existe.",
            Rejection(() => SpecialDayRule.Create((SpecialDayKind)2, [], null)));
    }

    [Fact]
    public void Create_ShouldRejectOpeningsThatOverlapOrAFirstOneThatEndsTheNextDay()
    {
        Assert.Equal("Los dos horarios de ese día se enciman.",
            Rejection(() => SpecialDayRule.Hours([Open("09:00", "14:00"), Open("13:00", "18:00")], null)));
        Assert.Equal("Solo el último horario de ese día puede terminar al día siguiente.",
            Rejection(() => SpecialDayRule.Hours([Open("08:00", "02:00"), Open("20:00", "23:00")], null)));
    }

    [Fact]
    public void Create_ShouldTakeTwentyFourHoursOnlyAlone_AndALastOpeningThatEndsTheNextDay()
    {
        var allDay = SpecialDayRule.Hours([Open("00:00", "00:00")], null);
        Assert.True(Assert.Single(allDay.Openings).IsAllDay);
        Assert.Equal("Abierto las 24 horas no lleva otro horario ese día.",
            Rejection(() => SpecialDayRule.Hours([Open("00:00", "00:00"), Open("09:00", "14:00")], null)));
        Assert.Equal("Abierto las 24 horas no lleva otro horario ese día.",
            Rejection(() => SpecialDayRule.Hours([Open("09:00", "14:00"), Open("18:00", "18:00")], null)));

        var lateNight = SpecialDayRule.Hours([Open("09:00", "14:00"), Open("18:00", "03:00")], "Fin de año");
        Assert.True(lateNight.Openings[1].EndsNextDay);
    }

    [Fact]
    public void Reason_ShouldBeTrimmed_BlankAsNone_AndOneLineOfAtMostFortyCharacters()
    {
        Assert.Equal("Navidad", SpecialDayRule.Closed("  Navidad ").Reason);
        Assert.Null(SpecialDayRule.Closed("   ").Reason);
        Assert.Null(SpecialDayRule.Closed(null).Reason);
        Assert.Equal(new string('a', 40), SpecialDayRule.Closed(new string('a', 40)).Reason);

        const string message = "El motivo tiene máximo 40 caracteres y va en una sola línea.";
        Assert.Equal(message, Rejection(() => SpecialDayRule.Closed(new string('a', 41))));
        Assert.Equal(message, Rejection(() => SpecialDayRule.Closed("Navidad\nCerrado")));
        Assert.Equal(message, Rejection(() => SpecialDayRule.Hours([Open("09:00", "18:00")], "Nochebuena\tcorta")));
    }

    [Fact]
    public void Rules_ShouldBeEqualByValue()
    {
        Assert.Equal(SpecialDayRule.Closed(null), SpecialDayRule.Closed(" "));
        Assert.Equal(SpecialDayRule.Closed(null).GetHashCode(), SpecialDayRule.Closed(null).GetHashCode());
        Assert.True(SpecialDayRule.Closed("Navidad") == SpecialDayRule.Closed("Navidad "));
        Assert.True(SpecialDayRule.Closed("Navidad") != SpecialDayRule.Closed("Inventario"));
        Assert.True(SpecialDayRule.Closed(null) != SpecialDayRule.Hours([Open("00:00", "00:00")], null));

        var hours = SpecialDayRule.Hours([Open("09:00", "14:00"), Open("16:00", "18:00")], "Nochebuena");
        var same = SpecialDayRule.Hours([Open("16:00", "18:00"), Open("09:00", "14:00")], "Nochebuena");
        Assert.Equal(hours, same);
        Assert.Equal(hours.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(hours, SpecialDayRule.Hours([Open("09:00", "14:00"), Open("16:00", "19:00")], "Nochebuena"));
        Assert.NotEqual(hours, SpecialDayRule.Hours([Open("09:00", "14:00")], "Nochebuena"));
    }

    [Fact]
    public void FromStorage_ShouldNeverThrow_AndReadAsTheRuleThatWasSaved()
    {
        var saved = SpecialDayRule.Hours([Open("09:00", "14:00"), Open("16:00", "18:00")], "Nochebuena");
        var read = SpecialDayRule.FromStorage(
            SpecialDayKind.SpecialHours, new TimeOnly(9, 0), new TimeOnly(14, 0), new TimeOnly(16, 0), new TimeOnly(18, 0), "Nochebuena");
        Assert.Equal(saved, read);
        Assert.Equal(SpecialDayRule.Closed(null), SpecialDayRule.FromStorage(SpecialDayKind.Closed, null, null, null, null, null));

        // Shapes the database refuses: read as they are, never thrown at a customer.
        var odd = SpecialDayRule.FromStorage(SpecialDayKind.Closed, new TimeOnly(9, 0), null, null, null, new string('x', 60));
        Assert.Empty(odd.Openings);
        _ = odd.GetHashCode();
        var overlapping = SpecialDayRule.FromStorage(
            SpecialDayKind.SpecialHours, new TimeOnly(9, 0), new TimeOnly(14, 0), new TimeOnly(13, 0), new TimeOnly(13, 0), "");
        Assert.Equal(2, overlapping.Openings.Count);
        Assert.NotEqual(saved, overlapping);
    }

    [Fact]
    public void SpecialDay_ShouldNeedItsBusiness()
    {
        var error = Assert.Throws<DomainException>(() =>
            new SpecialDay(Guid.Empty, Guid.NewGuid(), new DateOnly(2026, 12, 25), SpecialDayRule.Closed(null), revision: 1));
        Assert.Equal("El día especial necesita su negocio.", error.Message);
    }
}

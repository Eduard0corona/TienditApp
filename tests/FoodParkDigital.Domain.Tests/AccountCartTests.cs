using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The cart a customer account leaves in a store (Fase 31J, ADR 0090): what was chosen, never a price, within the limits of
/// the store and of an order, the same line once, and offered again for a week.
/// </summary>
public class AccountCartTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();
    private static readonly Guid Taco = Guid.NewGuid();
    private static readonly Guid Salsa = Guid.NewGuid();
    private static readonly Guid Queso = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private static AccountCartLine Line(Guid product, int quantity, string? notes = null, params Guid[] options)
        => new(product, options, quantity, notes);

    [Fact]
    public void Create_ShouldKeep_TheLines_JoiningTheSameOne_WithItsOptionsInOrder_AndTheNotesTrimmed()
    {
        var cart = AccountCart.Create(Account, Store,
        [
            Line(Taco, 2, null, Salsa, Queso),
            Line(Taco, 3, "  ", Queso, Salsa, Queso),
            Line(Taco, 1, " sin cebolla "),
            Line(Salsa, 98),
            Line(Salsa, 5)
        ], reorderOf: 4, skipped: 0, Now);

        var options = new[] { Salsa, Queso }.Order().ToList();
        Assert.Equal(
            [Line(Taco, 5, null, [.. options]), Line(Taco, 1, "sin cebolla"), Line(Salsa, AccountCart.MaxLineQuantity)],
            cart.Lines);
        Assert.Equal((Account, Store, 4, (int?)null, Now), (cart.UserId, cart.BusinessId, cart.ReorderOf, cart.Skipped, cart.SavedAt));
    }

    [Fact]
    public void Replace_ShouldTake_AllOfIt_AndTheTimeItChanged()
    {
        var cart = AccountCart.Create(Account, Store, [Line(Taco, 1)], 3, 1, Now);

        cart.Replace([Line(Salsa, 2)], reorderOf: null, skipped: null, Now.AddHours(1));

        Assert.Equal([Line(Salsa, 2)], cart.Lines);
        Assert.Equal(((int?)null, (int?)null, Now.AddHours(1)), (cart.ReorderOf, cart.Skipped, cart.SavedAt));
    }

    public static TheoryData<AccountCartLine> BadLines => new()
    {
        Line(Guid.Empty, 1),
        Line(Taco, 0),
        Line(Taco, AccountCart.MaxLineQuantity + 1),
        Line(Taco, 1, new string('a', AccountCart.MaxLineNotesLength + 1)),
        Line(Taco, 1, null, Guid.Empty),
        new(Taco, Enumerable.Range(0, AccountCart.MaxOptionsPerLine + 1).Select(_ => Guid.NewGuid()).ToList(), 1, null)
    };

    [Theory]
    [MemberData(nameof(BadLines))]
    public void Replace_ShouldRefuse_ALineThatCannotBe_AndKeepWhatItHad(AccountCartLine bad)
    {
        var cart = AccountCart.Create(Account, Store, [Line(Taco, 1)], null, null, Now);

        var refused = Assert.Throws<DomainException>(() => cart.Replace([Line(Salsa, 1), bad], null, null, Now.AddHours(1)));

        Assert.Equal(AccountCart.BadLineMessage, refused.Message);
        Assert.Equal([Line(Taco, 1)], cart.Lines);
        Assert.Equal(Now, cart.SavedAt);
    }

    [Fact]
    public void Replace_ShouldTake_UpToFiftyLines()
    {
        var fifty = Enumerable.Range(0, AccountCart.MaxLines).Select(_ => Line(Guid.NewGuid(), 1)).ToList();
        var cart = AccountCart.Create(Account, Store, fifty, null, null, Now);

        var refused = Assert.Throws<DomainException>(() => cart.Replace([.. fifty, Line(Guid.NewGuid(), 1)], null, null, Now));

        Assert.Equal(AccountCart.TooManyLinesMessage, refused.Message);
        Assert.Equal(AccountCart.MaxLines, cart.Lines.Count);
    }

    [Fact]
    public void Replace_ShouldTake_UpTo500Options_AmongAllItsLines()
    {
        List<Guid> Options(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        var full = Enumerable.Range(0, 5).Select(_ => new AccountCartLine(Guid.NewGuid(), Options(AccountCart.MaxOptionsPerCart / 5), 1, null)).ToList();
        var cart = AccountCart.Create(Account, Store, full, null, null, Now);

        var refused = Assert.Throws<DomainException>(() => cart.Replace([.. full, new AccountCartLine(Taco, Options(1), 1, null)], null, null, Now.AddHours(1)));

        Assert.Equal(AccountCart.TooManyOptionsMessage, refused.Message);
        Assert.Equal((5, Now), (cart.Lines.Count, cart.SavedAt));
    }

    [Fact]
    public void Empty_ShouldKeep_TheCartEmpty_WithWhenItWasEmptied_ForAWeek()
    {
        var cart = AccountCart.Create(Account, Store, [Line(Taco, 2)], 3, 1, Now);

        cart.Empty(Now.AddHours(2));

        Assert.Empty(cart.Lines);
        Assert.Equal(((int?)null, (int?)null, Now.AddHours(2)), (cart.ReorderOf, cart.Skipped, cart.SavedAt));
        // Not offered, but a device still compares its own with it for a week.
        Assert.False(cart.IsFresh(Now.AddHours(2)));
        Assert.True(cart.IsRecent(Now.AddHours(2).AddDays(AccountCart.FreshDays).AddTicks(-1)));
        Assert.False(cart.IsRecent(Now.AddHours(2).AddDays(AccountCart.FreshDays)));
    }

    [Fact]
    public void Create_ShouldNeed_AnAccountAndAStore()
    {
        Assert.Equal(AccountCart.AccountRequiredMessage,
            Assert.Throws<DomainException>(() => AccountCart.Create(Guid.Empty, Store, [], null, null, Now)).Message);
        Assert.Equal(AccountCart.BusinessRequiredMessage,
            Assert.Throws<DomainException>(() => AccountCart.Create(Account, Guid.Empty, [], null, null, Now)).Message);
    }

    [Fact]
    public void IsFresh_ShouldBe_ForAWeek_AndOnlyWithSomethingInIt()
    {
        var cart = AccountCart.Create(Account, Store, [Line(Taco, 1)], null, null, Now);

        Assert.True(cart.IsFresh(Now.AddDays(AccountCart.FreshDays).AddTicks(-1)));
        Assert.False(cart.IsFresh(Now.AddDays(AccountCart.FreshDays)));
        cart.Replace([], null, null, Now);
        Assert.False(cart.IsFresh(Now));
    }
}

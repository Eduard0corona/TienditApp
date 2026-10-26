using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Self-invoicing (Fase 28F, ADR 0050): the code each ticket prints, out of the business's key and the order, read back as
/// a customer types it; the business turns it on and off without changing the codes it already printed; and the invoice a
/// customer requested online says so.
/// </summary>
public class SelfInvoicingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrderId = new("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly byte[] Key = Enumerable.Range(0, SelfInvoiceCode.KeyLength).Select(i => (byte)i).ToArray();

    [Fact]
    public void Code_ShouldBeTheFirst50BitsOfTheHmac_SoPrintedTicketsKeepWorking()
    {
        // HMAC-SHA256(key 00..1f, "3f2504e04f8911d39a0c0305e82c3301"), its first 50 bits in Crockford's base32, worked out
        // apart from this code: a change here would stop every ticket already printed.
        Assert.Equal("49S7HFW2BK", SelfInvoiceCode.For(Key, OrderId));
        Assert.Equal("49S7H-FW2BK", SelfInvoiceCode.Format(SelfInvoiceCode.For(Key, OrderId)));
    }

    [Fact]
    public void Code_ShouldDependOnTheKeyAndTheOrder()
    {
        var code = SelfInvoiceCode.For(Key, OrderId);

        Assert.Equal(SelfInvoiceCode.Length, code.Length);
        Assert.All(code, c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
        Assert.Equal(code, SelfInvoiceCode.For(Key.ToArray(), OrderId));
        Assert.NotEqual(code, SelfInvoiceCode.For(Key, Guid.NewGuid()));
        Assert.NotEqual(code, SelfInvoiceCode.For(SelfInvoiceCode.NewKey(), OrderId));
    }

    [Fact]
    public void NewKey_ShouldBe32RandomBytes()
    {
        var key = SelfInvoiceCode.NewKey();

        Assert.Equal(SelfInvoiceCode.KeyLength, key.Length);
        Assert.False(key.SequenceEqual(SelfInvoiceCode.NewKey()));
    }

    [Theory]
    [InlineData("49S7H-FW2BK")]
    [InlineData("49s7hfw2bk")]
    [InlineData(" 49S7H FW2BK ")]
    [InlineData("49S7H--FW2BK")]
    public void Code_ShouldBeTaken_AsACustomerTypesIt(string typed)
    {
        Assert.Equal("49S7HFW2BK", SelfInvoiceCode.Normalize(typed));
        Assert.True(SelfInvoiceCode.Matches(Key, OrderId, typed));
    }

    [Theory]
    // What is easy to misread off a ticket is read as Crockford reads it.
    [InlineData("ABCDO-ILl12", "ABCD0-11112")]
    [InlineData("oooooiiiii", "0000011111")]
    public void Normalize_ShouldReadOAsZeroAndIOrLAsOne(string typed, string expected)
    {
        Assert.Equal(expected.Replace("-", ""), SelfInvoiceCode.Normalize(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("49S7H-FW2B")]
    [InlineData("49S7H-FW2BKX")]
    [InlineData("49S7H-FW2BU")]
    [InlineData("49S7H_FW2BK")]
    [InlineData("49S7H·FW2BK")]
    public void Normalize_ShouldRefuse_WhatCannotBeACode(string? typed)
    {
        Assert.Null(SelfInvoiceCode.Normalize(typed));
        Assert.False(SelfInvoiceCode.Matches(Key, OrderId, typed));
    }

    [Fact]
    public void Matches_ShouldRefuseAnotherOrdersCode_AndAnotherBusinesssCode()
    {
        var otherOrder = Guid.NewGuid();

        Assert.False(SelfInvoiceCode.Matches(Key, OrderId, SelfInvoiceCode.For(Key, otherOrder)));
        Assert.False(SelfInvoiceCode.Matches(SelfInvoiceCode.NewKey(), OrderId, SelfInvoiceCode.For(Key, OrderId)));
        Assert.True(SelfInvoiceCode.Matches(Key, otherOrder, SelfInvoiceCode.For(Key, otherOrder)));
    }

    [Fact]
    public void Profile_ShouldStartWithSelfInvoicingOff_AndWithoutAKey()
    {
        var profile = InvoicingProfile.Create(TenantId);

        Assert.False(profile.SelfInvoicing);
        Assert.Null(profile.SelfInvoiceKey);
        Assert.Null(profile.SelfInvoiceCodeFor(OrderId));
        Assert.False(profile.TakesSelfInvoiceCode(OrderId, "49S7H-FW2BK"));
    }

    [Fact]
    public void Profile_ShouldPrintAndTakeCodes_OnceSelfInvoicingIsOn()
    {
        var profile = InvoicingProfile.Create(TenantId);

        profile.TurnOnSelfInvoicing();

        Assert.True(profile.SelfInvoicing);
        Assert.Equal(SelfInvoiceCode.KeyLength, profile.SelfInvoiceKey!.Length);
        var code = profile.SelfInvoiceCodeFor(OrderId);
        Assert.Equal(SelfInvoiceCode.For(profile.SelfInvoiceKey, OrderId), code);
        Assert.True(profile.TakesSelfInvoiceCode(OrderId, SelfInvoiceCode.Format(code!).ToLowerInvariant()));
        Assert.False(profile.TakesSelfInvoiceCode(Guid.NewGuid(), code));
        Assert.False(profile.TakesSelfInvoiceCode(OrderId, null));
    }

    [Fact]
    public void Profile_ShouldKeepItsKey_WhenSelfInvoicingIsTurnedOffAndOnAgain()
    {
        var profile = InvoicingProfile.Create(TenantId);
        profile.TurnOnSelfInvoicing();
        var key = profile.SelfInvoiceKey!.ToArray();
        var code = profile.SelfInvoiceCodeFor(OrderId);

        profile.TurnOffSelfInvoicing();

        Assert.False(profile.SelfInvoicing);
        Assert.Equal(key, profile.SelfInvoiceKey);
        Assert.Null(profile.SelfInvoiceCodeFor(OrderId));
        Assert.False(profile.TakesSelfInvoiceCode(OrderId, code));

        profile.TurnOnSelfInvoicing();
        profile.TurnOnSelfInvoicing();

        Assert.Equal(key, profile.SelfInvoiceKey);
        Assert.Equal(code, profile.SelfInvoiceCodeFor(OrderId));
        Assert.True(profile.TakesSelfInvoiceCode(OrderId, code));
    }

    [Fact]
    public void Invoice_ShouldSayACustomerRequestedItOnline()
    {
        var receiver = CfdiReceiver.Create("XIQB891116QE4", "KARLA FUENTE NOLASCO", "26015", "612", "G03");
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-6));
        OrderInvoice Requested(Guid by) =>
            OrderInvoice.Request(TenantId, OrderId, "F", 1, receiver, "01", "{}", 100m, 16m, 116m, by, now);

        Assert.NotEqual(Guid.Empty, OrderInvoice.SelfInvoiceRequester);
        Assert.True(Requested(OrderInvoice.SelfInvoiceRequester).RequestedByCustomer);
        Assert.False(Requested(Guid.NewGuid()).RequestedByCustomer);
    }
}

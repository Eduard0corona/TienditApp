using FoodParkDigital.Infrastructure.Services;
using Drawer = FoodParkDigital.Infrastructure.Services.OrderCashService.DrawerCandidate;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>Which open drawer takes the cash of a sale (see <see cref="OrderCashService.PickDrawer"/>).</summary>
public class OrderCashServiceDrawerTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();
    private static readonly Guid Centro = Guid.NewGuid();
    private static readonly Guid Norte = Guid.NewGuid();

    [Fact]
    public void TheCashiersOwnDrawer_ShouldWin_WhereverTheSaleWasMade()
    {
        var anaAtNorte = new Drawer(Guid.NewGuid(), Ana, Norte, null);
        var betoAtCentro = new Drawer(Guid.NewGuid(), Beto, Centro, null);

        Assert.Equal(anaAtNorte.Id, OrderCashService.PickDrawer([anaAtNorte, betoAtCentro], Ana, Centro, null));
    }

    [Fact]
    public void TheOnlyDrawerOfTheBranch_ShouldTakeTheCash()
    {
        var betoAtCentro = new Drawer(Guid.NewGuid(), Beto, Centro, null);
        var otherAtNorte = new Drawer(Guid.NewGuid(), Guid.NewGuid(), Norte, null);

        Assert.Equal(betoAtCentro.Id, OrderCashService.PickDrawer([betoAtCentro, otherAtNorte], Ana, Centro, null));
    }

    [Fact]
    public void CashOfABranch_ShouldNeverGoToTheDrawerOfAnotherBranch()
    {
        var onlyAtNorte = new Drawer(Guid.NewGuid(), Beto, Norte, null);

        Assert.Null(OrderCashService.PickDrawer([onlyAtNorte], Ana, Centro, null));
    }

    [Fact]
    public void SeveralDrawersAtTheBranch_NoneTheCashiers_ShouldNotBeGuessed()
    {
        Drawer[] open = [new(Guid.NewGuid(), Beto, Centro, null), new(Guid.NewGuid(), Guid.NewGuid(), Centro, null)];

        Assert.Null(OrderCashService.PickDrawer(open, Ana, Centro, null));
    }

    [Fact]
    public void ADrawerOpenedBeforeBranches_ShouldTakeTheCash_WhenItIsTheOnlyOneOpen()
    {
        var legacy = new Drawer(Guid.NewGuid(), null, null, null);

        Assert.Equal(legacy.Id, OrderCashService.PickDrawer([legacy], Ana, Centro, null));
    }

    [Fact]
    public void ASaleWithoutBranch_ShouldUseTheLocationOfThePayment()
    {
        var location = Guid.NewGuid();
        Drawer[] open = [new(Guid.NewGuid(), Beto, null, location), new(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid())];

        Assert.Equal(open[0].Id, OrderCashService.PickDrawer(open, Ana, null, location));
        Assert.Null(OrderCashService.PickDrawer(open, Ana, null, null));
    }
}

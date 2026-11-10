using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Taking and releasing a piece of the service board: an open piece goes back to nobody; a completed or cancelled one
/// keeps who worked it, because that is part of its history (it could not be assigned again either). Through the order,
/// both touch the order row, so a change saved meanwhile is caught by its row version.
/// </summary>
public class ServiceOrderPieceAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Worker = Guid.NewGuid();

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Accepted)]
    [InlineData(ServiceOrderPieceStatus.InProgress)]
    public void ReleaseAssignment_ShouldLeaveAnOpenPieceWithNobody(ServiceOrderPieceStatus status)
    {
        var (order, piece) = NewAssignedPiece();
        MoveTo(order, piece, status);

        piece.ReleaseAssignment();

        Assert.Equal(status, piece.Status);
        Assert.Null(piece.AssignedToUserId);
        Assert.Null(piece.AssignedToName);
        Assert.Null(piece.AssignedAt);
    }

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Completed)]
    [InlineData(ServiceOrderPieceStatus.Cancelled)]
    public void ReleaseAssignment_ShouldBeRejected_WhenThePieceIsClosed_KeepingWhoWorkedIt(ServiceOrderPieceStatus status)
    {
        var (order, piece) = NewAssignedPiece();
        MoveTo(order, piece, status);

        var error = Assert.Throws<DomainException>(piece.ReleaseAssignment);

        Assert.Equal("No se puede liberar una pieza completada o cancelada.", error.Message);
        Assert.Equal(status, piece.Status);
        Assert.Equal(Worker, piece.AssignedToUserId);
        Assert.Equal("Técnico", piece.AssignedToName);
        Assert.Equal(Now, piece.AssignedAt);
    }

    [Fact]
    public void AssignServicePiece_ShouldTakeThePiece_AndTouchTheOrder_SoItsRowVersionIsChecked()
    {
        var (order, piece) = NewAssignedPiece();
        order.ReleaseServicePiece(piece, Now);
        var other = Guid.NewGuid();
        var later = Now.AddMinutes(5);

        order.AssignServicePiece(piece, other, "Otra técnica", later);

        Assert.Equal((other, "Otra técnica", later), (piece.AssignedToUserId, piece.AssignedToName, piece.AssignedAt));
        Assert.Equal(later, order.UpdatedAt);
    }

    [Fact]
    public void ReleaseServicePiece_ShouldLeaveThePieceWithNobody_AndTouchTheOrder_SoItsRowVersionIsChecked()
    {
        var (order, piece) = NewAssignedPiece();
        var later = Now.AddMinutes(5);

        order.ReleaseServicePiece(piece, later);

        Assert.Null(piece.AssignedToUserId);
        Assert.Null(piece.AssignedToName);
        Assert.Null(piece.AssignedAt);
        Assert.Equal(later, order.UpdatedAt);
    }

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Completed)]
    [InlineData(ServiceOrderPieceStatus.Cancelled)]
    public void AssignAndReleaseServicePiece_ShouldBeRejected_WhenThePieceIsClosed_WithoutTouchingTheOrder(ServiceOrderPieceStatus status)
    {
        var (order, piece) = NewAssignedPiece();
        MoveTo(order, piece, status);
        var later = Now.AddMinutes(5);

        var assign = Assert.Throws<DomainException>(() => order.AssignServicePiece(piece, Guid.NewGuid(), "Otra técnica", later));
        var release = Assert.Throws<DomainException>(() => order.ReleaseServicePiece(piece, later));

        Assert.Equal("No se puede asignar una pieza completada o cancelada.", assign.Message);
        Assert.Equal("No se puede liberar una pieza completada o cancelada.", release.Message);
        Assert.Equal((Worker, "Técnico", Now), (piece.AssignedToUserId, piece.AssignedToName, piece.AssignedAt));
        Assert.Equal(Now, order.UpdatedAt);
    }

    [Fact]
    public void AssignAndReleaseServicePiece_ShouldRejectAPieceOfAnotherOrder()
    {
        var (order, _) = NewAssignedPiece();
        var (other, foreign) = NewAssignedPiece();

        Assert.Throws<DomainException>(() => order.AssignServicePiece(foreign, Guid.NewGuid(), "Otra técnica", Now.AddMinutes(5)));
        Assert.Throws<DomainException>(() => order.ReleaseServicePiece(foreign, Now.AddMinutes(5)));

        Assert.Equal((Worker, "Técnico", Now), (foreign.AssignedToUserId, foreign.AssignedToName, foreign.AssignedAt));
        Assert.Null(order.UpdatedAt);
        Assert.Null(other.UpdatedAt);
    }

    private static (Order Order, ServiceOrderPiece Piece) NewAssignedPiece()
    {
        var order = new Order(
            tenantId: Guid.NewGuid(),
            customerUserId: Guid.NewGuid(),
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Service,
            orderType: OrderType.Service,
            customerName: "Cliente",
            customerPhone: "+525511112222",
            customerNotes: null,
            tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Limpieza de tenis", 1, 250m);
        order.AddServicePiece("Par Nike", null, null, null, null, null, null, null, null);

        var piece = order.ServicePieces.Single();
        piece.AssignTo(Worker, "Técnico", Now);
        return (order, piece);
    }

    /// <summary>Takes the piece to the status through the transitions the board allows.</summary>
    private static void MoveTo(Order order, ServiceOrderPiece piece, ServiceOrderPieceStatus status)
    {
        ServiceOrderPieceStatus[] path = status switch
        {
            ServiceOrderPieceStatus.Accepted => [],
            ServiceOrderPieceStatus.InProgress => [ServiceOrderPieceStatus.InProgress],
            ServiceOrderPieceStatus.Completed => [ServiceOrderPieceStatus.InProgress, ServiceOrderPieceStatus.Completed],
            ServiceOrderPieceStatus.Cancelled => [ServiceOrderPieceStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        foreach (var next in path)
            order.ChangeServicePieceStatus(piece, next, Now);
    }
}

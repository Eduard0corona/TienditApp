using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Releasing a piece of the service board: an open piece goes back to nobody; a completed or cancelled one keeps who
/// worked it, because that is part of its history (it could not be assigned again either). Assigning and releasing go
/// through the order (<see cref="Order.AssignServicePiece"/> and <see cref="Order.ReleaseServicePiece"/>), which takes
/// only its own pieces and marks its row, so the save checks its row version like a change of status.
/// </summary>
public class ServiceOrderPieceAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);
    private static readonly Guid Worker = Guid.NewGuid();
    private static readonly Guid OtherWorker = Guid.NewGuid();

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

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Accepted)]
    [InlineData(ServiceOrderPieceStatus.InProgress)]
    public void AssignServicePiece_ShouldAssignAnOpenPiece_AndTouchTheOrder_SoItsRowVersionIsChecked(ServiceOrderPieceStatus status)
    {
        var (order, piece) = NewAssignedPiece();
        MoveTo(order, piece, status);

        order.AssignServicePiece(piece, OtherWorker, "Otro técnico", Later);

        Assert.Equal(status, piece.Status);
        Assert.Equal(OtherWorker, piece.AssignedToUserId);
        Assert.Equal("Otro técnico", piece.AssignedToName);
        Assert.Equal(Later, piece.AssignedAt);
        Assert.Equal(Later, order.UpdatedAt);
    }

    [Fact]
    public void AssignServicePiece_ShouldRejectAPieceOfAnotherOrder_WithoutTouchingEither()
    {
        var (order, _) = NewAssignedPiece();
        var (other, foreign) = NewAssignedPiece();
        var orderUpdatedAt = order.UpdatedAt;

        var error = Assert.Throws<DomainException>(() => order.AssignServicePiece(foreign, OtherWorker, "Otro técnico", Later));

        Assert.Equal("La pieza indicada no existe dentro de la orden.", error.Message);
        Assert.Equal(Worker, foreign.AssignedToUserId);
        Assert.Equal(orderUpdatedAt, order.UpdatedAt);
        Assert.Null(other.UpdatedAt);
    }

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Completed)]
    [InlineData(ServiceOrderPieceStatus.Cancelled)]
    public void AssignServicePiece_ShouldBeRejected_WhenThePieceIsClosed_WithoutTouchingTheOrder(ServiceOrderPieceStatus status)
    {
        var (order, piece) = NewAssignedPiece();
        MoveTo(order, piece, status);

        var error = Assert.Throws<DomainException>(() => order.AssignServicePiece(piece, OtherWorker, "Otro técnico", Later));

        Assert.Equal("No se puede asignar una pieza completada o cancelada.", error.Message);
        Assert.Equal(Worker, piece.AssignedToUserId);
        Assert.Equal(Now, order.UpdatedAt);
    }

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Accepted)]
    [InlineData(ServiceOrderPieceStatus.InProgress)]
    public void ReleaseServicePiece_ShouldLeaveAnOpenPieceWithNobody_AndTouchTheOrder_SoItsRowVersionIsChecked(ServiceOrderPieceStatus status)
    {
        var (order, piece) = NewAssignedPiece();
        MoveTo(order, piece, status);

        order.ReleaseServicePiece(piece, Later);

        Assert.Equal(status, piece.Status);
        Assert.Null(piece.AssignedToUserId);
        Assert.Null(piece.AssignedToName);
        Assert.Null(piece.AssignedAt);
        Assert.Equal(Later, order.UpdatedAt);
    }

    [Fact]
    public void ReleaseServicePiece_ShouldRejectAPieceOfAnotherOrder_KeepingItsAssignment()
    {
        var (order, _) = NewAssignedPiece();
        var (other, foreign) = NewAssignedPiece();
        var orderUpdatedAt = order.UpdatedAt;

        var error = Assert.Throws<DomainException>(() => order.ReleaseServicePiece(foreign, Later));

        Assert.Equal("La pieza indicada no existe dentro de la orden.", error.Message);
        Assert.Equal(Worker, foreign.AssignedToUserId);
        Assert.Equal("Técnico", foreign.AssignedToName);
        Assert.Equal(Now, foreign.AssignedAt);
        Assert.Equal(orderUpdatedAt, order.UpdatedAt);
        Assert.Null(other.UpdatedAt);
    }

    [Theory]
    [InlineData(ServiceOrderPieceStatus.Completed)]
    [InlineData(ServiceOrderPieceStatus.Cancelled)]
    public void ReleaseServicePiece_ShouldBeRejected_WhenThePieceIsClosed_KeepingWhoWorkedIt_WithoutTouchingTheOrder(
        ServiceOrderPieceStatus status)
    {
        var (order, piece) = NewAssignedPiece();
        MoveTo(order, piece, status);

        var error = Assert.Throws<DomainException>(() => order.ReleaseServicePiece(piece, Later));

        Assert.Equal("No se puede liberar una pieza completada o cancelada.", error.Message);
        Assert.Equal(Worker, piece.AssignedToUserId);
        Assert.Equal("Técnico", piece.AssignedToName);
        Assert.Equal(Now, piece.AssignedAt);
        Assert.Equal(Now, order.UpdatedAt);
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

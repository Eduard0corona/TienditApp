using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Releasing a piece of the service board: an open piece goes back to nobody; a completed or cancelled one keeps who
/// worked it, because that is part of its history (it could not be assigned again either).
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

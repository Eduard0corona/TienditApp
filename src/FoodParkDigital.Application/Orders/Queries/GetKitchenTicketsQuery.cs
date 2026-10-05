using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>Rounds of open tabs the kitchen still has to prepare or hand over, oldest first.</summary>
[RequiresPermission(TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Kitchen)]
public record GetKitchenTicketsQuery : IRequest<List<KitchenTicketDto>>;

public class GetKitchenTicketsQueryHandler : IRequestHandler<GetKitchenTicketsQuery, List<KitchenTicketDto>>
{
    private readonly IApplicationDbContext _context;

    public GetKitchenTicketsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<KitchenTicketDto>> Handle(GetKitchenTicketsQuery request, CancellationToken cancellationToken)
    {
        var tickets = await _context.KitchenTickets
            .AsNoTracking()
            .Where(t => t.Status != KitchenTicketStatus.Served)
            .Join(
                // Open tabs only: a closed tab leaves the board with whatever it had.
                _context.Orders.Where(o => o.IsTab
                    && o.Status != OrderStatus.Delivered
                    && o.Status != OrderStatus.Cancelled
                    && o.Status != OrderStatus.Completed),
                ticket => ticket.OrderId,
                order => order.Id,
                (ticket, order) => new
                {
                    Ticket = ticket,
                    order.OrderNumber,
                    order.CustomerName,
                    order.TableNumber,
                    order.CustomerNotes
                })
            .OrderBy(x => x.Ticket.SentAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        if (tickets.Count == 0)
            return [];

        var orderIds = tickets.Select(x => x.Ticket.OrderId).Distinct().ToList();
        var items = await _context.OrderItems
            .AsNoTracking()
            .Include(i => i.Options)
            .Where(i => orderIds.Contains(i.OrderId))
            .ToListAsync(cancellationToken);

        return tickets.Select(x => new KitchenTicketDto
        {
            Id = x.Ticket.Id,
            OrderId = x.Ticket.OrderId,
            OrderNumber = x.OrderNumber,
            CustomerName = x.CustomerName,
            TableNumber = x.TableNumber,
            CustomerNotes = x.CustomerNotes,
            Round = x.Ticket.Round,
            Status = x.Ticket.Status,
            SentAt = x.Ticket.SentAt,
            StartedAt = x.Ticket.StartedAt,
            ReadyAt = x.Ticket.ReadyAt,
            Items = items
                .Where(i => i.OrderId == x.Ticket.OrderId && i.Round == x.Ticket.Round)
                .Select(i => i.ToDto())
                .ToList()
        }).ToList();
    }
}

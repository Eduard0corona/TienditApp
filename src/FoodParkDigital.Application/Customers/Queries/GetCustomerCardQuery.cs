using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Queries;

/// <summary>
/// Everything the business knows about a customer, by phone or registered customer: what they buy, what
/// they owe, their appointments and whether they accept campaigns. Used by the chat, the customers list and
/// the orders screen.
/// </summary>
[RequiresPermission(TenantPermission.ManageCustomers, TenantPermission.ManageWhatsApp, TenantPermission.ManageOrders)]
public record GetCustomerCardQuery : IRequest<Result<CustomerCardDto>>
{
    public string? Phone { get; init; }
    public Guid? CustomerId { get; init; }
}

public class GetCustomerCardQueryValidator : AbstractValidator<GetCustomerCardQuery>
{
    public GetCustomerCardQueryValidator()
    {
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Phone) || x.CustomerId.HasValue)
            .WithMessage("Indica el teléfono o el cliente.");
    }
}

public class CustomerCardDto
{
    public string? Phone { get; set; }
    public string? Name { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Email { get; set; }
    public int OrdersCount { get; set; }
    /// <summary>Paid minus refunded, over orders that were not cancelled.</summary>
    public decimal TotalSpent { get; set; }
    public decimal AverageTicket { get; set; }
    /// <summary>Still to be collected over orders that were not cancelled.</summary>
    public decimal Balance { get; set; }
    public DateTimeOffset? FirstOrderAt { get; set; }
    public DateTimeOffset? LastOrderAt { get; set; }
    public List<CustomerCardOrderDto> RecentOrders { get; set; } = [];
    public List<CustomerCardProductDto> FavoriteProducts { get; set; } = [];
    public List<CustomerCardAppointmentDto> UpcomingAppointments { get; set; } = [];
    public DateTimeOffset? LastVisitAt { get; set; }
    public bool OptedOut { get; set; }
    public DateTimeOffset? OptedOutAt { get; set; }
    public Guid? ConversationId { get; set; }
}

public class CustomerCardOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public OrderStatus Status { get; set; }
    public OrderChannel Channel { get; set; }
    public decimal Total { get; set; }
    public decimal Balance { get; set; }
}

public class CustomerCardProductDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public int Quantity { get; set; }
}

public class CustomerCardAppointmentDto
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; } = null!;
    public DateTimeOffset StartAt { get; set; }
    public AppointmentStatus Status { get; set; }
}

public class GetCustomerCardQueryHandler : IRequestHandler<GetCustomerCardQuery, Result<CustomerCardDto>>
{
    private const int RecentOrders = 10;
    private const int FavoriteProducts = 5;
    private const int AppointmentScan = 500;

    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;
    private readonly ICurrentTenantService _tenant;
    private readonly IDateTimeProvider _clock;

    public GetCustomerCardQueryHandler(
        IApplicationDbContext context,
        IPhoneNormalizationService phones,
        ICurrentTenantService tenant,
        IDateTimeProvider clock)
    {
        _context = context;
        _phones = phones;
        _tenant = tenant;
        _clock = clock;
    }

    public async Task<Result<CustomerCardDto>> Handle(GetCustomerCardQuery request, CancellationToken cancellationToken)
    {
        var card = new CustomerCardDto();
        string? rawPhone = request.Phone;
        if (request.CustomerId is { } customerId)
        {
            var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer is null)
                return Result.Failure<CustomerCardDto>("El cliente no existe.");

            card.CustomerId = customer.Id;
            card.Name = customer.Name;
            card.Email = customer.Email;
            rawPhone ??= customer.Phone;
        }

        if (string.IsNullOrWhiteSpace(rawPhone))
            return Result.Success(card);
        if (!_phones.TryNormalizeToE164(rawPhone, "MX", out var phone))
            return Result.Failure<CustomerCardDto>("El teléfono no es válido.");

        var key = MessagingPhone.ToKey(phone);
        var variants = MessagingPhone.Variants(phone).ToList();
        card.Phone = key;

        var orders = _context.Orders.AsNoTracking()
            .Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone) && o.Status != OrderStatus.Cancelled);
        var totals = await orders
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Paid = g.Sum(o => o.PaidAmount - o.RefundedAmount),
                Balance = g.Sum(o => o.Total - o.PaidAmount),
                First = g.Min(o => o.CreatedAt),
                Last = g.Max(o => o.CreatedAt)
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (totals is not null)
        {
            card.OrdersCount = totals.Count;
            card.TotalSpent = totals.Paid;
            card.Balance = totals.Balance;
            card.FirstOrderAt = totals.First;
            card.LastOrderAt = totals.Last;
            card.AverageTicket = totals.Count == 0 ? 0 : Math.Round(totals.Paid / totals.Count, 2, MidpointRounding.ToEven);
        }

        card.RecentOrders = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone))
            .OrderByDescending(o => o.CreatedAt)
            .Take(RecentOrders)
            .Select(o => new CustomerCardOrderDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                CreatedAt = o.CreatedAt,
                Status = o.Status,
                Channel = o.Channel,
                Total = o.Total,
                Balance = o.Total - o.PaidAmount
            })
            .ToListAsync(cancellationToken);
        card.Name ??= await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerPhone != null && variants.Contains(o.CustomerPhone))
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => o.CustomerName)
            .FirstOrDefaultAsync(cancellationToken);

        card.FavoriteProducts = await _context.OrderItems.AsNoTracking()
            .Where(i => _context.Orders.Any(o => o.Id == i.OrderId
                && o.CustomerPhone != null
                && variants.Contains(o.CustomerPhone)
                && o.Status != OrderStatus.Cancelled))
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new CustomerCardProductDto
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                Quantity = g.Sum(i => i.Quantity - i.ReturnedQuantity)
            })
            .Where(p => p.Quantity > 0)
            .OrderByDescending(p => p.Quantity)
            .Take(FavoriteProducts)
            .ToListAsync(cancellationToken);

        var settings = await _context.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
        if (settings?.HasCapability(TenantCapability.Appointments) == true)
            await FillAppointmentsAsync(card, key, cancellationToken);

        var optOut = await _context.MessagingOptOuts.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Phone == key && o.ResubscribedAt == null, cancellationToken);
        card.OptedOut = optOut is not null;
        card.OptedOutAt = optOut?.OptedOutAt;

        card.ConversationId = await _context.WhatsAppConversations.AsNoTracking()
            .Where(c => variants.Contains(c.CustomerPhone))
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        card.Name ??= await _context.WhatsAppConversations.AsNoTracking()
            .Where(c => variants.Contains(c.CustomerPhone))
            .Select(c => c.CustomerName)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(card);
    }

    /// <summary>
    /// Appointment phones are stored as typed, so they are compared normalized, over a bounded window of the
    /// business's recent and upcoming appointments.
    /// </summary>
    private async Task FillAppointmentsAsync(CustomerCardDto card, string key, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var since = now.AddDays(-365);
        var appointments = await _context.Appointments.AsNoTracking()
            .Where(a => a.StartAt >= since)
            .OrderByDescending(a => a.StartAt)
            .Take(AppointmentScan)
            .Select(a => new { a.Id, a.CustomerPhone, a.ServiceName, a.StartAt, a.Status })
            .ToListAsync(cancellationToken);

        var mine = appointments
            .Where(a => _phones.TryNormalizeToE164(a.CustomerPhone, "MX", out var phone) && MessagingPhone.ToKey(phone) == key)
            .ToList();
        card.UpcomingAppointments = mine
            .Where(a => a.StartAt >= now && a.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
            .OrderBy(a => a.StartAt)
            .Take(3)
            .Select(a => new CustomerCardAppointmentDto { Id = a.Id, ServiceName = a.ServiceName, StartAt = a.StartAt, Status = a.Status })
            .ToList();
        card.LastVisitAt = mine
            .Where(a => a.StartAt < now && a.Status == AppointmentStatus.Completed)
            .Select(a => (DateTimeOffset?)a.StartAt)
            .FirstOrDefault();
    }
}

using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Dining;

public class DiningTableDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Area { get; set; }
    public int? Seats { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    /// <summary>The tab open at the table, if any.</summary>
    public OpenTabSummaryDto? OpenTab { get; set; }
}

/// <summary>What the dining room shows of an open tab.</summary>
public class OpenTabSummaryDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string? TableNumber { get; set; }
    public decimal Total { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public int Rounds { get; set; }
    /// <summary>Rounds still in the kitchen (pending or being prepared).</summary>
    public int RoundsInKitchen { get; set; }
    /// <summary>Rounds ready to take to the table.</summary>
    public int RoundsReady { get; set; }
}

public class DiningRoomDto
{
    public List<DiningTableDto> Tables { get; set; } = new();
    /// <summary>Open tabs that go by a name instead of a table (bar, "Juan").</summary>
    public List<OpenTabSummaryDto> TabsWithoutTable { get; set; } = new();
}

/// <summary>Tables with their open tab, and the open tabs without a table.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Food)]
public record GetDiningRoomQuery(bool IncludeInactive = false) : IRequest<DiningRoomDto>;

public class GetDiningRoomQueryHandler : IRequestHandler<GetDiningRoomQuery, DiningRoomDto>
{
    private readonly IApplicationDbContext _context;

    public GetDiningRoomQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DiningRoomDto> Handle(GetDiningRoomQuery request, CancellationToken cancellationToken)
    {
        var tables = await _context.DiningTables
            .AsNoTracking()
            .Where(t => request.IncludeInactive || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var openTabs = await _context.Orders
            .AsNoTracking()
            .Where(o => o.IsTab
                && o.Status != OrderStatus.Delivered
                && o.Status != OrderStatus.Cancelled
                && o.Status != OrderStatus.Completed)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                o.OpenTableId,
                Summary = new OpenTabSummaryDto
                {
                    OrderId = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerName = o.CustomerName,
                    TableNumber = o.TableNumber,
                    Total = o.Total,
                    Balance = o.Total - o.PaidAmount - o.WrittenOffAmount,
                    OpenedAt = o.CreatedAt,
                    Rounds = o.KitchenTickets.Count,
                    RoundsInKitchen = o.KitchenTickets.Count(t => t.Status == KitchenTicketStatus.Pending || t.Status == KitchenTicketStatus.Preparing),
                    RoundsReady = o.KitchenTickets.Count(t => t.Status == KitchenTicketStatus.Ready)
                }
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        var byTable = openTabs
            .Where(t => t.OpenTableId.HasValue)
            .GroupBy(t => t.OpenTableId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Summary);

        return new DiningRoomDto
        {
            Tables = tables.Select(t => new DiningTableDto
            {
                Id = t.Id,
                Name = t.Name,
                Area = t.Area,
                Seats = t.Seats,
                SortOrder = t.SortOrder,
                IsActive = t.IsActive,
                OpenTab = byTable.GetValueOrDefault(t.Id)
            }).ToList(),
            TabsWithoutTable = openTabs.Where(t => !t.OpenTableId.HasValue).Select(t => t.Summary).ToList()
        };
    }
}

[RequiresPermission(TenantPermission.ManageCatalog)]
[RequiresCapability(TenantCapability.Food)]
public record CreateDiningTableCommand : IRequest<Result<DiningTableDto>>
{
    public string Name { get; init; } = null!;
    public string? Area { get; init; }
    public int? Seats { get; init; }
    public int SortOrder { get; init; }
}

public class CreateDiningTableCommandValidator : AbstractValidator<CreateDiningTableCommand>
{
    public CreateDiningTableCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DiningTable.MaxNameLength);
        RuleFor(x => x.Area).MaximumLength(DiningTable.MaxAreaLength);
        RuleFor(x => x.Seats).InclusiveBetween(1, DiningTable.MaxSeats).When(x => x.Seats.HasValue);
    }
}

public class CreateDiningTableCommandHandler : IRequestHandler<CreateDiningTableCommand, Result<DiningTableDto>>
{
    public const int MaxTables = 300;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateDiningTableCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<DiningTableDto>> Handle(CreateDiningTableCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await _context.DiningTables.AnyAsync(t => t.Name == name, cancellationToken))
            return Result.Failure<DiningTableDto>($"Ya existe una mesa llamada {name} (si está desactivada, actívala).");
        if (await _context.DiningTables.CountAsync(cancellationToken) >= MaxTables)
            return Result.Failure<DiningTableDto>($"Un negocio puede tener como máximo {MaxTables} mesas.");

        var table = new DiningTable(_tenantService.TenantId, name, request.Area, request.Seats, request.SortOrder);
        _context.DiningTables.Add(table);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(DiningMapping.ToDto(table));
    }
}

[RequiresPermission(TenantPermission.ManageCatalog)]
[RequiresCapability(TenantCapability.Food)]
public record UpdateDiningTableCommand : IRequest<Result<DiningTableDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Area { get; init; }
    public int? Seats { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public class UpdateDiningTableCommandValidator : AbstractValidator<UpdateDiningTableCommand>
{
    public UpdateDiningTableCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DiningTable.MaxNameLength);
        RuleFor(x => x.Area).MaximumLength(DiningTable.MaxAreaLength);
        RuleFor(x => x.Seats).InclusiveBetween(1, DiningTable.MaxSeats).When(x => x.Seats.HasValue);
    }
}

public class UpdateDiningTableCommandHandler : IRequestHandler<UpdateDiningTableCommand, Result<DiningTableDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateDiningTableCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<DiningTableDto>> Handle(UpdateDiningTableCommand request, CancellationToken cancellationToken)
    {
        var table = await _context.DiningTables.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("DiningTable", request.Id);

        var name = request.Name.Trim();
        if (await _context.DiningTables.AnyAsync(t => t.Id != table.Id && t.Name == name, cancellationToken))
            return Result.Failure<DiningTableDto>($"Ya existe una mesa llamada {name}.");
        if (!request.IsActive && await _context.Orders.AnyAsync(o => o.OpenTableId == table.Id, cancellationToken))
            return Result.Failure<DiningTableDto>("La mesa tiene una cuenta abierta: ciérrala o muévela antes de desactivarla.");

        table.Update(name, request.Area, request.Seats, request.SortOrder, request.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(DiningMapping.ToDto(table));
    }
}

internal static class DiningMapping
{
    public static DiningTableDto ToDto(DiningTable table) => new()
    {
        Id = table.Id,
        Name = table.Name,
        Area = table.Area,
        Seats = table.Seats,
        SortOrder = table.SortOrder,
        IsActive = table.IsActive
    };
}

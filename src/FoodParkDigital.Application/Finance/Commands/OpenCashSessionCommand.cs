using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Commands;

public record OpenCashSessionCommand : IRequest<Result<CashSessionDto>>
{
    public decimal OpeningAmount { get; init; }
    public string? Notes { get; init; }
}

public class OpenCashSessionCommandValidator : AbstractValidator<OpenCashSessionCommand>
{
    public OpenCashSessionCommandValidator()
    {
        RuleFor(x => x.OpeningAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class OpenCashSessionCommandHandler : IRequestHandler<OpenCashSessionCommand, Result<CashSessionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public OpenCashSessionCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CashSessionDto>> Handle(OpenCashSessionCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, Domain.Orders.Enums.OrderType.Retail) &&
            !TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, Domain.Orders.Enums.OrderType.Service))
        {
            return Result.Failure<CashSessionDto>("El tenant no tiene módulos comerciales habilitados.");
        }

        var hasOpenSession = await _context.CashSessions
            .AnyAsync(x => x.Status == CashSessionStatus.Open, cancellationToken);
        if (hasOpenSession)
            return Result.Failure<CashSessionDto>("Ya existe una caja abierta.");

        var session = new CashSession(
            tenantId: _tenantService.TenantId,
            openedAt: _dateTimeProvider.UtcNow,
            openedBy: _currentUserService.UserName ?? "system",
            openingAmount: request.OpeningAmount,
            notes: request.Notes);

        _context.CashSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(session));
    }

    private static CashSessionDto ToDto(CashSession session) => new()
    {
        Id = session.Id,
        OpenedAt = session.OpenedAt,
        OpenedBy = session.OpenedBy,
        OpeningAmount = session.OpeningAmount,
        ClosedAt = session.ClosedAt,
        ClosedBy = session.ClosedBy,
        ClosingAmount = session.ClosingAmount,
        Status = session.Status,
        Notes = session.Notes,
        Inflows = session.Inflows,
        Outflows = session.Outflows,
        ExpectedBalance = session.ExpectedBalance
    };
}

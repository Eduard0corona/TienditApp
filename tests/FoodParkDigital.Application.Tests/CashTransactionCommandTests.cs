using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class CashTransactionCommandTests
{
    [Theory]
    [InlineData(CashTransactionType.SalePayment, 100)]
    [InlineData(CashTransactionType.OpeningAdjustment, 100)]
    [InlineData(CashTransactionType.Expense, 0)]
    [InlineData(CashTransactionType.Expense, -200)]
    [InlineData(CashTransactionType.Deposit, 10.005)]
    public void Validator_ShouldRejectNonManualTypesAndNonPositiveAmounts(CashTransactionType type, decimal amount)
    {
        var result = new AddCashTransactionCommandValidator().Validate(new AddCashTransactionCommand
        {
            CashSessionId = Guid.NewGuid(),
            TransactionType = type,
            Amount = amount
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Handle_ShouldRecordExpenseAsOutflow_WithServerTime()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var session = new CashSession(tenantId, DateTimeOffset.UtcNow.AddHours(-2), "cajero", 1000m);
        context.CashSessions.Add(session);
        await context.SaveChangesAsync();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.Zero));

        var result = await new AddCashTransactionCommandHandler(context, clock).Handle(new AddCashTransactionCommand
        {
            CashSessionId = session.Id,
            TransactionType = CashTransactionType.Expense,
            Amount = 200m
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(-200m, result.Value!.Amount);
        Assert.Equal(clock.UtcNow, result.Value.OccurredAt);

        var stored = await context.CashSessions.Include(x => x.Transactions).SingleAsync();
        Assert.Equal(800m, stored.ExpectedBalance);
    }

    [Fact]
    public async Task Handle_ShouldRejectUnknownOrderReference()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var session = new CashSession(tenantId, DateTimeOffset.UtcNow, "cajero", 0m);
        context.CashSessions.Add(session);
        await context.SaveChangesAsync();

        var result = await new AddCashTransactionCommandHandler(context, new FixedClock(DateTimeOffset.UtcNow)).Handle(
            new AddCashTransactionCommand
            {
                CashSessionId = session.Id,
                TransactionType = CashTransactionType.Refund,
                Amount = 50m,
                OrderId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await context.CashTransactions.ToListAsync());
    }

    [Fact]
    public void CloseValidator_ShouldRejectNegativeClosingAmount()
    {
        var result = new CloseCashSessionCommandValidator().Validate(new CloseCashSessionCommand
        {
            CashSessionId = Guid.NewGuid(),
            ClosingAmount = -1m
        });

        Assert.False(result.IsValid);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var tenantService = new TestCurrentTenantService();
        tenantService.SetTenant(tenantId);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, tenantService, new NullMediator());
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
    }
}

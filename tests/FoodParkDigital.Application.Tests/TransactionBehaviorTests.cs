using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A command marked <see cref="ITransactionalRequest"/> is one transaction: committed with a successful result,
/// undone with a failed one, run again after a transient failure, and its screens hear of it only once committed.
/// </summary>
public class TransactionBehaviorTests
{
    [Fact]
    public async Task SuccessfulResult_ShouldCommit_AndAnnounceTheChangeOnceCommitted()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var tenant = NewTenant();
        var announcedBeforeCommit = true;

        var result = await host.InScopeAsync(services => Run(services, async context =>
        {
            Save(context, tenant);
            await context.SaveChangesAsync();
            announcedBeforeCommit = host.Mediator.Published.Count > 0;
            return Result.Success();
        }));

        Assert.True(result.IsSuccess);
        Assert.False(announcedBeforeCommit);
        Assert.Equal(tenant.Id, Assert.Single(host.Mediator.Published.OfType<TenantSaved>()).TenantId);
        Assert.True(await host.QueryAsync(c => c.Tenants.AnyAsync(t => t.Id == tenant.Id)));
    }

    [Fact]
    public async Task FailedResult_ShouldUndoWhatWasSaved_AndNeverAnnounceIt()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var tenant = NewTenant();

        var result = await host.InScopeAsync(services => Run(services, async context =>
        {
            Save(context, tenant);
            await context.SaveChangesAsync();
            return Result.Failure("No hay existencias suficientes.");
        }));

        Assert.True(result.IsFailure);
        Assert.False(await host.QueryAsync(c => c.Tenants.AnyAsync(t => t.Id == tenant.Id)));
        Assert.Empty(host.Mediator.Published);
    }

    [Fact]
    public async Task TransientFailure_ShouldRunTheHandlerAgain_WithoutWhatTheFailedAttemptTracked()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        host.CommandFaults.FailNext(
            command => command.CommandText.Contains("INSERT INTO \"Tenants\"", StringComparison.Ordinal),
            () => new SimulatedTransientException());
        var attempts = new List<Guid>();

        var result = await host.InScopeAsync(services => Run(services, async context =>
        {
            var tenant = NewTenant();
            attempts.Add(tenant.Id);
            Save(context, tenant);
            await context.SaveChangesAsync();
            return Result.Success();
        }));

        Assert.True(result.IsSuccess);
        Assert.True(host.CommandFaults.Fired);
        Assert.Equal(2, attempts.Count);
        // Only the second attempt's business: the first one's insert was not replayed by the retry.
        Assert.Equal(new[] { attempts[1] }, await host.QueryAsync(c => c.Tenants.Select(t => t.Id).ToListAsync()));
        Assert.Equal(attempts[1], Assert.Single(host.Mediator.Published.OfType<TenantSaved>()).TenantId);
    }

    [Fact]
    public async Task HandlerThatDiscardsItsWork_ShouldLeaveNothing_AndAnswerWhatItReturns()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var tenant = NewTenant();

        // Like a sale that a concurrent request stored first: its own copy is undone and the stored one is the answer.
        var result = await host.InScopeAsync(services => Run(services, async context =>
        {
            Save(context, tenant);
            await context.SaveChangesAsync();
            await context.DiscardUnitOfWorkAsync(CancellationToken.None);
            return Result.Success();
        }));

        Assert.True(result.IsSuccess);
        Assert.False(await host.QueryAsync(c => c.Tenants.AnyAsync(t => t.Id == tenant.Id)));
        Assert.Empty(host.Mediator.Published);
    }

    [Fact]
    public async Task RequestSentInsideATransaction_ShouldBePartOfIt()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        IDbContextTransaction? outer = null;
        IDbContextTransaction? seenByHandler = null;

        var result = await host.InScopeAsync(services =>
        {
            var context = services.GetRequiredService<IApplicationDbContext>();
            return context.ExecuteInResilientTransactionAsync(_ =>
            {
                outer = context.Database.CurrentTransaction;
                return Run(services, inner =>
                {
                    seenByHandler = inner.Database.CurrentTransaction;
                    return Task.FromResult(Result.Success());
                });
            }, CancellationToken.None);
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(outer);
        Assert.Same(outer, seenByHandler);
    }

    [Fact]
    public async Task RequestInsideATransaction_ShouldNotDiscardItsCallersWork()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var callers = NewTenant();

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.InScopeAsync(services =>
        {
            var context = services.GetRequiredService<IApplicationDbContext>();
            return context.ExecuteInResilientTransactionAsync(async _ =>
            {
                Save(context, callers);
                await context.SaveChangesAsync();
                return await Run(services, async inner =>
                {
                    await inner.DiscardUnitOfWorkAsync(CancellationToken.None);
                    return Result.Success();
                });
            }, CancellationToken.None);
        }));

        // The caller's transaction ended with the error, as a whole: nothing half-done is committed.
        Assert.False(await host.QueryAsync(c => c.Tenants.AnyAsync(t => t.Id == callers.Id)));
        Assert.Empty(host.Mediator.Published);
    }

    [Fact]
    public async Task OnlyTransactionalCommandsAnsweredWithAResult_ShouldGetTheBehavior()
    {
        await using var host = await SqliteTestHost.CreateAsync();

        await host.InScopeAsync(scope =>
        {
            var services = new ServiceCollection();
            services.AddSingleton(scope.GetRequiredService<IApplicationDbContext>());
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
            using var provider = services.BuildServiceProvider();

            Assert.Single(provider.GetServices<IPipelineBehavior<SaveTenant, Result>>());
            Assert.Empty(provider.GetServices<IPipelineBehavior<ReadTenant, Result>>());
            Assert.Empty(provider.GetServices<IPipelineBehavior<CountTenants, int>>());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Pipeline_ShouldOpenTheTransactionLast_AroundTheHandlerOnly()
    {
        var behaviors = new ServiceCollection()
            .AddApplicationServices()
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType!)
            .ToList();

        Assert.Equal(
            new[] { typeof(AuthorizationBehavior<,>), typeof(ValidationBehavior<,>), typeof(LoggingBehavior<,>), typeof(TransactionBehavior<,>) },
            behaviors);
    }

    private static Task<Result> Run(IServiceProvider services, Func<IApplicationDbContext, Task<Result>> handler)
    {
        var context = services.GetRequiredService<IApplicationDbContext>();
        return new TransactionBehavior<SaveTenant, Result>(context)
            .Handle(new SaveTenant(), () => handler(context), CancellationToken.None);
    }

    private static Tenant NewTenant()
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant($"Negocio {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
    }

    private static void Save(IApplicationDbContext context, Tenant tenant)
    {
        context.Tenants.Add(tenant);
        tenant.AddDomainEvent(new TenantSaved(tenant.Id));
    }

    private sealed record SaveTenant : IRequest<Result>, ITransactionalRequest;

    private sealed record ReadTenant : IRequest<Result>;

    private sealed record CountTenants : IRequest<int>, ITransactionalRequest;

    private sealed record TenantSaved(Guid TenantId) : DomainEvent;
}

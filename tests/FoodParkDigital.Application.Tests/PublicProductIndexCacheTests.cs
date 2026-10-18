using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The product index of Explorar (Fase 24F) is built once per snapshot, by one search at a time: the others use it, a
/// search with an older snapshot never puts back an older index, and one that failed is not tried again until the next.
/// </summary>
public class PublicProductIndexCacheTests
{
    [Fact]
    public async Task GetAsync_ShouldBuildOncePerSnapshot_AndAgainForTheNext()
    {
        await using var fixture = SalesTestFixture.Create();
        var cache = new PublicProductIndexCache();
        var first = await SnapshotAsync(fixture, 0);
        var next = await SnapshotAsync(fixture, 1);
        var builds = 0;
        Task<PublicProductIndex> Build(PublicDirectorySnapshot snapshot, CancellationToken ct)
        {
            builds++;
            return PublicProductIndex.BuildAsync(fixture.Context, snapshot, ct);
        }

        var index = await cache.GetAsync(first, Build, CancellationToken.None);
        Assert.Same(index, await cache.GetAsync(first, Build, CancellationToken.None));
        Assert.Equal(1, builds);

        var rebuilt = await cache.GetAsync(next, Build, CancellationToken.None);
        Assert.Equal(next.BuiltAt, rebuilt!.BuiltFor);
        Assert.Equal(2, builds);
        // A search that read the older snapshot uses the newer index: it does not build the older one again over it.
        Assert.Same(rebuilt, await cache.GetAsync(first, Build, CancellationToken.None));
        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task GetAsync_ShouldBuildOnce_ForSearchesAtTheSameTime()
    {
        await using var fixture = SalesTestFixture.Create();
        var cache = new PublicProductIndexCache();
        var snapshot = await SnapshotAsync(fixture, 0);
        var release = new TaskCompletionSource();
        var builds = 0;
        async Task<PublicProductIndex> Build(PublicDirectorySnapshot current, CancellationToken ct)
        {
            Interlocked.Increment(ref builds);
            await release.Task;
            return await PublicProductIndex.BuildAsync(fixture.Context, current, ct);
        }

        var searches = Enumerable.Range(0, 5).Select(_ => cache.GetAsync(snapshot, Build, CancellationToken.None)).ToList();
        release.SetResult();
        var indexes = await Task.WhenAll(searches);

        Assert.Equal(1, builds);
        Assert.All(indexes, index => Assert.Same(indexes[0], index));
    }

    [Fact]
    public async Task GetAsync_ShouldKeepBuilding_WhenASearchWaitingForItGivesUp()
    {
        await using var fixture = SalesTestFixture.Create();
        var cache = new PublicProductIndexCache();
        var snapshot = await SnapshotAsync(fixture, 0);
        var release = new TaskCompletionSource();
        async Task<PublicProductIndex> Build(PublicDirectorySnapshot current, CancellationToken ct)
        {
            await release.Task;
            return await PublicProductIndex.BuildAsync(fixture.Context, current, ct);
        }

        var building = cache.GetAsync(snapshot, Build, CancellationToken.None);
        using var gaveUp = new CancellationTokenSource();
        var waiting = cache.GetAsync(snapshot, Build, gaveUp.Token);
        await gaveUp.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();

        Assert.Equal(snapshot.BuiltAt, (await building)!.BuiltFor);
    }

    [Fact]
    public async Task GetAsync_ShouldNotTryAgain_AfterItFailedForTheSnapshot_UntilTheNextOne()
    {
        await using var fixture = SalesTestFixture.Create();
        var cache = new PublicProductIndexCache();
        var failed = await SnapshotAsync(fixture, 0);
        var next = await SnapshotAsync(fixture, 1);
        var builds = 0;
        Task<PublicProductIndex> Failing(PublicDirectorySnapshot snapshot, CancellationToken ct)
        {
            builds++;
            throw new InvalidOperationException("La base no respondió.");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(failed, Failing, CancellationToken.None));
        Assert.Null(await cache.GetAsync(failed, Failing, CancellationToken.None));
        Assert.Equal(1, builds);

        var index = await cache.GetAsync(next, (snapshot, ct) => PublicProductIndex.BuildAsync(fixture.Context, snapshot, ct), CancellationToken.None);
        Assert.Equal(next.BuiltAt, index!.BuiltFor);
    }

    /// <summary>A snapshot of Explorar built <paramref name="minutesLater"/> after the fixture's now.</summary>
    private static Task<PublicDirectorySnapshot> SnapshotAsync(SalesTestFixture fixture, int minutesLater)
        => PublicDirectorySnapshot.BuildAsync(fixture.Context, SalesTestFixture.Now.AddMinutes(minutesLater), CancellationToken.None);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Runs <paramref name="save"/> once, right before the context it is attached to writes: someone else's save that lands
/// between what a use case read and what it writes.
/// </summary>
internal sealed class SavedMeanwhile(Func<Task> save) : SaveChangesInterceptor
{
    private bool _saved;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!_saved)
        {
            _saved = true;
            await save();
        }

        return result;
    }
}

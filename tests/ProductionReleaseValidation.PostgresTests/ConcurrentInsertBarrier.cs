using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProductionReleaseValidation.Api.Domain;

namespace ProductionReleaseValidation.PostgresTests;

// Hold both inserts after the API existence checks, so a real unique violation is inevitable.
internal sealed class ConcurrentInsertBarrier : SaveChangesInterceptor
{
    private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<ValidationResult>().Any(e => e.State == EntityState.Added))
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _bothReady.TrySetResult();
            await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}
